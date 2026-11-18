using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using SpireMouse.Timeline;

namespace SpireMouse;

// Game v0.113 keeps "may this creature be targeted" on the creature view
// (flee / reattach) instead of deriving it from the model alone. A Decimillipede
// segment that died and is waiting to reattach must stay untargetable and without
// a health bar after navigation, and become targetable again once it reattaches.
internal static partial class RuntimeVerification
{
    private static async Task VerifyReattach()
    {
        SaveManager.Instance.SetFtuesEnabled(false);
        var run = await NGame.Instance!.StartNewSingleplayerRun(
            MegaCrit.Sts2.Core.Entities.Players.Player.CreateForNewRun(ModelDb.Character<Ironclad>(), SaveManager.Instance.GenerateUnlockStateFromProgress(), 1uL), true,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "REATTACH", GameMode.Standard, false);
        var player = run.Players[0];
        await CardPileCmd.RemoveFromDeck(player.Deck.Cards.ToArray(), false);
        foreach (var model in new CardModel[] { ModelDb.Card<StrikeIronclad>(), ModelDb.Card<StrikeIronclad>(), ModelDb.Card<StrikeIronclad>(), ModelDb.Card<DefendIronclad>(), ModelDb.Card<DefendIronclad>() })
            await CardPileCmd.Add(run.CreateCard(model, player), PileType.Deck, skipVisuals: true);
        TimelineEngine.DiagnosticRoomLoader = async () => await RunManager.Instance.EnterRoomDebug(RoomType.Elite, model: ModelDb.Encounter<DecimillipedeElite>().ToMutable(), showTransition: false);
        await TimelineEngine.DiagnosticRoomLoader();
        player.Creature.SetMaxHpInternal(10000); player.Creature.SetCurrentHpInternal(10000);
        Creature[] segments = CombatManager.Instance.DebugOnlyGetState()!.Creatures.Where(c => c.IsEnemy).ToArray();
        Creature victim = segments[0];
        victim.SetCurrentHpInternal(1);
        TimelineEngine.Capture(RunManager.Instance.CombatReplayWriter._replay!);
        await Until(() => TimelineEngine.Settled && TimelineEngine.Points.Count == 1, "reattach fixture checkpoint");

        var failures = new List<string>();
        void Probe(bool ok, string name)
        {
            if (ok) { Results.Add(name); Entry.Log.Info("VERIFY PASS: " + name); }
            else { failures.Add(name); Entry.Log.Info("VERIFY PROBE FAIL: " + name); }
        }
        string View() => JsonSerializer.Serialize(segments.Select(c =>
        {
            var node = NCombatRoom.Instance!.GetCreatureNode(c);
            return new
            {
                alive = c.IsAlive,
                view = node != null && node.IsVisibleInTree(),
                targetable = node?.IsInteractable == true && node.Hitbox.MouseFilter == Control.MouseFilterEnum.Stop,
                health = node != null && HealthActuallyVisible(node),
            };
        }));
        bool Coherent() => segments.All(c =>
        {
            var node = NCombatRoom.Instance!.GetCreatureNode(c);
            if (node == null) return false;
            bool targetable = node.IsInteractable && node.Hitbox.MouseFilter == Control.MouseFilterEnum.Stop;
            return targetable == c.IsAlive && HealthActuallyVisible(node) == c.IsAlive;
        });

        var points = new List<(string Label, int Cursor, string State, string View)>();
        async Task Record(string label)
        {
            await Frames(240);
            points.Add((label, TimelineEngine.Cursor, State(), View()));
            Entry.Log.Info($"REATTACH {label}: cursor={TimelineEngine.Cursor} view={points[^1].View}");
            Probe(Coherent(), $"{label}: native view is targetable and shows health exactly for live segments");
        }
        async Task EndTurn(string label)
        {
            int round = CombatManager.Instance.DebugOnlyGetState()!.RoundNumber, before = TimelineEngine.Cursor;
            await EndStressTurn();
            await Until(() => TimelineEngine.Settled && CombatManager.Instance.DebugOnlyGetState()!.RoundNumber > round && TimelineEngine.Cursor > before, label + " checkpoint");
            await Record(label);
        }

        await Record("initial");
        var strike = NPlayerHand.Instance!.ActiveHolders.First(h => h.CardModel is StrikeIronclad);
        await Click(Center(strike.Hitbox), MouseButton.Right);
        await Click(Center(NCombatRoom.Instance!.GetCreatureNode(victim)!.Hitbox), MouseButton.Right);
        await Until(() => TimelineEngine.Settled && TimelineEngine.Cursor > 0, "segment kill checkpoint");
        Probe(victim.IsDead && CombatManager.Instance.IsInProgress, "segment died while combat continues");
        await Record("segment dead");
        await Screenshot("reattach-dead");
        for (int turn = 0; turn < 4 && victim.IsDead; turn++) await EndTurn($"enemy turn {turn + 1}");
        Probe(victim.IsAlive, "segment reattached within four enemy turns");
        await Screenshot("reattach-alive");

        async Task Visit(int index, string direction)
        {
            var point = points[index];
            if (TimelineEngine.Cursor != point.Cursor)
            {
                Probe(TimelineEngine.Navigate(point.Cursor), $"{direction} to '{point.Label}' accepted");
                await TimelineEngine.NavigationTask!;
            }
            await Frames(120);
            string view = View();
            if (view != point.View) Entry.Log.Info($"REATTACH {direction} '{point.Label}' got view={view}");
            Probe(TimelineEngine.Status == "" && State() == point.State, $"{direction} to '{point.Label}': combat fingerprint");
            Probe(view == point.View, $"{direction} to '{point.Label}': targetability and health bars as natively shown");
        }
        for (int i = points.Count - 2; i >= 0; i--) await Visit(i, "undo");
        await Screenshot("reattach-rewound");
        for (int i = 1; i < points.Count; i++) await Visit(i, "redo");
        await Visit(1, "jump back"); await Screenshot("reattach-dead-restored");
        await Visit(points.Count - 1, "jump forward");
        // The game must keep running correctly from a restored "waiting to reattach" state.
        await Visit(1, "return");
        for (int turn = 0; turn < 4 && victim.IsDead; turn++)
        {
            int round = CombatManager.Instance.DebugOnlyGetState()!.RoundNumber;
            await EndStressTurn();
            await Until(() => TimelineEngine.Settled && CombatManager.Instance.DebugOnlyGetState()!.RoundNumber > round, "replayed enemy turn");
        }
        await Frames(240);
        Probe(victim.IsAlive && Coherent(), "segment reattaches normally after being restored dead");
        await Screenshot("reattach-final");

        foreach (var failure in failures) Entry.Log.Info("VERIFY PROBE FAIL (summary): " + failure);
        Check(failures.Count == 0, $"Reattach probes: {failures.Count} failed");
    }
}
