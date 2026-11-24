using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using SpireMouse.Timeline;

namespace SpireMouse;

// Counts what the player can perceive while history crosses a restart:
// sounds that reach the audio engine and replayed inputs drawn without a cover.
[HarmonyPatch(typeof(NAudioManager), nameof(NAudioManager.PlayOneShot),
    [typeof(string), typeof(Dictionary<string, float>), typeof(float)])]
internal static class QuietOneShotProbe
{
    internal static int Audible;
    [HarmonyPriority(Priority.Last)]
    static void Prefix(bool __runOriginal) { if (__runOriginal && TimelineEngine.IsNavigationInProgress) Audible++; }
}
[HarmonyPatch(typeof(NDebugAudioManager), nameof(NDebugAudioManager.Play))]
internal static class QuietDebugAudioProbe
{
    [HarmonyPriority(Priority.Last)]
    static void Prefix(bool __runOriginal) { if (__runOriginal && TimelineEngine.IsNavigationInProgress) QuietOneShotProbe.Audible++; }
}
[HarmonyPatch(typeof(ActionQueueSynchronizer), nameof(ActionQueueSynchronizer.RequestEnqueue))]
internal static class QuietReplayProbe
{
    internal static int Covered, Uncovered;
    static void Prefix()
    {
        if (!TimelineEngine.SubmittingReplayInput) return;
        if (RestartPresentation.IsActive) Covered++; else Uncovered++;
    }
}

internal static partial class RuntimeVerification
{
    private sealed record QuietSample(string Step, double MaxCoverDiff, int ChangedFrames, int Frames,
        int Audible, int CoveredReplay, int UncoveredReplay, double Milliseconds);
    private static readonly List<QuietSample> QuietSamples = new();

    private static async Task VerifyRestartQuiet()
    {
        SaveManager.Instance.SetFtuesEnabled(false);
        SettingsStore.Current.HistoryLimit = 100;
        SettingsStore.Current.IncludeChoiceHistory = true;
        var run = await StartTestRun(ModelDb.Character<Ironclad>(), "RESTARTQUIET");
        var player = run.Players[0];
        player.Creature.SetMaxHpInternal(1000); player.Creature.SetCurrentHpInternal(1000);
        await CardPileCmd.RemoveFromDeck(player.Deck.Cards.ToArray(), false);
        foreach (var model in new CardModel[] { ModelDb.Card<StrikeIronclad>(), ModelDb.Card<StrikeIronclad>(),
            ModelDb.Card<DefendIronclad>(), ModelDb.Card<DefendIronclad>(), ModelDb.Card<TrueGrit>() })
        {
            var card = run.CreateCard(model, player);
            if (card is TrueGrit) card.UpgradeInternal();
            await CardPileCmd.Add(card, PileType.Deck, skipVisuals: true);
        }
        await RunManager.Instance.EnterMapCoord(run.Map!.GetAllMapPoints().First(p => p.PointType == MapPointType.Monster).coord);
        await Until(() => TimelineEngine.Settled && TimelineEngine.Points.Count == 1, "quiet opening");
        string initial = State();

        // Several player actions and an enemy turn before the restart.
        await RestartHistoryPlayDefend();
        await RestartHistoryPlayDefend();
        int round = CombatManager.Instance.DebugOnlyGetState()!.RoundNumber;
        await EndStressTurn();
        await Until(() => TimelineEngine.Settled && CombatManager.Instance.DebugOnlyGetState()!.RoundNumber > round
            && TimelineEngine.Points[TimelineEngine.Cursor].State!.SequenceEqual(TimelineEngine.Fingerprint()), "quiet enemy turn");
        await RestartHistoryPlayDefend();
        string beforeRestart = State();
        await RestartHistoryRestart();
        await QuietNavigate(false, beforeRestart, "undo restart");
        await QuietNavigate(true, initial, "redo restart");

        // A pending choice reached by crossing a restart, then left and re-entered
        // inside the rebuilt segment.
        await RestartHistoryPlayDefend();
        var grit = NPlayerHand.Instance!.ActiveHolders.First(h => h.CardModel is TrueGrit);
        await Click(Center(grit.Hitbox), MouseButton.Right);
        await Until(() => TimelineEngine.ChoiceVisible && TimelineEngine.Points[^1].Choice, "quiet pending choice");
        string pending = State();
        await RestartHistoryRestart();
        await QuietNavigate(false, pending, "undo restart to pending choice");
        await QuietNavigate(false, null, "undo before pending choice");
        await QuietNavigate(true, pending, "redo pending choice inside rebuilt segment");

        // A longer combat: several turns of recorded input before the restart.
        var hand = NPlayerHand.Instance!;
        await Click(Center(hand.ActiveHolders.First().Hitbox), MouseButton.Right);
        await Click(new Vector2(50, 450), MouseButton.Right);
        await Until(() => TimelineEngine.Settled && !TimelineEngine.ChoiceVisible, "quiet choice resolves");
        for (int turn = 0; turn < 5; turn++)
        {
            for (int i = 0; i < 2 && NPlayerHand.Instance!.ActiveHolders.Any(h => h.CardModel is DefendIronclad); i++)
                await RestartHistoryPlayDefend();
            int r = CombatManager.Instance.DebugOnlyGetState()!.RoundNumber;
            await EndStressTurn();
            await Until(() => TimelineEngine.Settled && CombatManager.Instance.DebugOnlyGetState()!.RoundNumber > r
                && TimelineEngine.Points[TimelineEngine.Cursor].State!.SequenceEqual(TimelineEngine.Fingerprint()), "quiet long turn");
        }
        string longCombat = State();
        await RestartHistoryRestart();
        await QuietNavigate(false, longCombat, "undo restart after five turns");
        await QuietNavigate(true, initial, "redo restart after five turns");
        await QuietNavigate(false, longCombat, "undo restart again after five turns");

        File.WriteAllText(Path.Combine(DirectoryPath!, "quiet.json"),
            JsonSerializer.Serialize(QuietSamples, new JsonSerializerOptions { WriteIndented = true }));
        foreach (var s in QuietSamples)
        {
            Check(s.ChangedFrames == 0, s.Step + " keeps the cover still while restoring");
            if (s.Step.Contains("restart"))
            {
                Check(s.Audible == 0, s.Step + " plays no sound effects while restoring");
                Check(s.UncoveredReplay == 0, s.Step + " replays no inputs in view");
            }
        }
        // Re-entering a rebuilt pending choice replays only the choice's own card,
        // like an ordinary choice checkpoint, not every action since combat start.
        Check(QuietSamples.Single(q => q.Step == "redo pending choice inside rebuilt segment").UncoveredReplay <= 1, "rebuilt pending choice anchors to its nearest checkpoint");
    }

    private static async Task QuietNavigate(bool redo, string? expected, string step)
    {
        QuietOneShotProbe.Audible = 0; QuietReplayProbe.Covered = 0; QuietReplayProbe.Uncovered = 0;
        Check(redo ? TimelineEngine.HandleRedoKey() : TimelineEngine.HandleUndoKey(), step + " accepted");
        var task = TimelineEngine.NavigationTask!;
        byte[]? cover = null;
        double max = 0; int changed = 0, frames = 0;
        while (!task.IsCompleted)
        {
            await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            if (task.IsCompleted || !RestartPresentation.IsActive) continue;
            var data = NGame.Instance.GetViewport().GetTexture().GetImage().GetData();
            frames++;
            if (cover == null) { cover = data; continue; }
            double diff = QuietDiff(cover, data);
            max = Math.Max(max, diff);
            if (diff > 0.01) changed++;
        }
        await task;
        await Until(() => !RestartPresentation.IsActive, step + " reveal finishes");
        await Frames(5);
        QuietSamples.Add(new(step, max, changed, frames, QuietOneShotProbe.Audible,
            QuietReplayProbe.Covered, QuietReplayProbe.Uncovered, TimelineEngine.LastNavigationMilliseconds));
        Entry.Log.Info($"QUIET {step}: frames={frames} changed={changed} maxDiff={max:F4} audible={QuietOneShotProbe.Audible} covered={QuietReplayProbe.Covered} uncovered={QuietReplayProbe.Uncovered} ms={TimelineEngine.LastNavigationMilliseconds:F0}");
        Check(TimelineEngine.Status == "" && (expected == null || State() == expected), step);
    }

    // Fraction of sampled pixels that differ noticeably from the cover frame.
    private static double QuietDiff(byte[] a, byte[] b)
    {
        if (a.Length != b.Length) return 1;
        int sampled = 0, differ = 0;
        for (int i = 0; i + 2 < a.Length; i += 4 * 37)
        {
            sampled++;
            if (Math.Abs(a[i] - b[i]) + Math.Abs(a[i + 1] - b[i + 1]) + Math.Abs(a[i + 2] - b[i + 2]) > 24) differ++;
        }
        return sampled == 0 ? 0 : (double)differ / sampled;
    }
}
