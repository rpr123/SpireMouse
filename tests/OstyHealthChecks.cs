using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
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
using STS2QuickAnimationMode.Utils;

namespace SpireMouse;
internal static partial class RuntimeVerification
{
    private static bool HealthActuallyVisible(NCreature node)
    {
        var bar = node._stateDisplay._healthBar;
        if (!bar.IsVisibleInTree() || !bar._hpLabel.IsVisibleInTree()) return false;
        for (Node? current = bar._hpLabel; current != null; current = current.GetParent())
            if (current is CanvasItem item && item.Modulate.A < .95f) return false;
        return bar._hpLabel.SelfModulate.A > .95f;
    }

    private static async Task VerifyOstyHealth()
    {
        SettingsStore.Current.Speed.AccelerationMode = SpeedAccelerationMode.Off;
        SpeedManager.OnSettingsChanged();
        SaveManager.Instance.SetFtuesEnabled(false);
        var manager = RunManager.Instance;
        var run = await NGame.Instance!.StartNewSingleplayerRun(MegaCrit.Sts2.Core.Entities.Players.Player.CreateForNewRun(ModelDb.Character<Necrobinder>(), SaveManager.Instance.GenerateUnlockStateFromProgress(), 1uL), true,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "OSTYHEALTH", GameMode.Standard, false);
        var player = run.Players[0];
        await CardPileCmd.RemoveFromDeck(player.Deck.Cards.ToArray(), false);
        foreach (var model in new CardModel[] { ModelDb.Card<Bodyguard>(), ModelDb.Card<DefendNecrobinder>(), ModelDb.Card<DefendNecrobinder>(), ModelDb.Card<StrikeNecrobinder>(), ModelDb.Card<Unleash>() })
            await CardPileCmd.Add(run.CreateCard(model, player), PileType.Deck, skipVisuals: true);
        await manager.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<CultistsNormal>().ToMutable(), showTransition: false);
        await Until(() => TimelineEngine.Settled && TimelineEngine.Points.Count == 1, "Osty health initial checkpoint");
        var room = NCombatRoom.Instance!;
        var osty = player.Osty!;
        await Frames(120);
        Check(HealthActuallyVisible(room.GetCreatureNode(osty)!), "initial Osty HP really visible");

        // Native death command supplies a deterministic dead companion fixture;
        // resurrection and branching below use real card input and timeline navigation.
        await CreatureCmd.Kill(osty, true);
        await Frames(180);
        Check(osty.IsDead, "Osty native death fixture");
        TimelineEngine.Capture(manager.CombatReplayWriter._replay!);
        await Until(() => TimelineEngine.Settled && TimelineEngine.Points.Count == 1, "dead Osty checkpoint");
        for (int repeat = 0; repeat < 3; repeat++)
        {
            var card = NPlayerHand.Instance!.ActiveHolders.First(h => h.CardModel is Bodyguard);
            await Click(Center(card.Hitbox), MouseButton.Right);
            await Until(() => TimelineEngine.Settled && TimelineEngine.Cursor == 1 && osty.IsAlive, "Bodyguard resurrects Osty");
            await Frames(150);
            await Screenshot($"osty-resummoned-{repeat}");
            var node = room.GetCreatureNode(osty)!;
            Entry.Log.Info($"OSTY health repeat={repeat} visible={node._stateDisplay.Visible} alpha={node._stateDisplay.Modulate.A} hp={osty.CurrentHp}");
            Check(HealthActuallyVisible(node), $"Osty resummon {repeat} HP really visible after fade completes");
            Check(node._stateDisplay._healthBar._hpLabel.Text == $"{osty.CurrentHp}/{osty.MaxHp}", "Osty HP label matches model");
            await CheckIdleKeepsMoving(node, "Osty resummon");
            Check(TimelineEngine.HandleUndoKey(), "undo summon accepted");
            await TimelineEngine.NavigationTask!;
            Check(osty.IsDead, "undo restores dead Osty");
            if (repeat == 2)
            {
                Check(TimelineEngine.Navigate(1), "redo summon accepted");
                await TimelineEngine.NavigationTask!;
                await Frames(150);
                Check(HealthActuallyVisible(room.GetCreatureNode(osty)!), "redo live Osty HP visible");
            }
        }

        // Restore before the old death animation/tween finishes, then let it run out.
        for (int repeat = 0; repeat < 3; repeat++)
        {
            var snapshot = new CombatSnapshot();
            var old = room.GetCreatureNode(osty)!;
            var scale = old.Visuals.Scale;
            var kill = CreatureCmd.Kill(osty, true);
            await Until(() => old.DeathAnimationTask != null && osty.IsDead, "Osty death animation started");
            await kill;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            snapshot.Restore();
            watch.Stop();
            Check(watch.ElapsedMilliseconds < 100, "Osty restoration remains immediate");
            var node = room.GetCreatureNode(osty)!;
            Check(HealthActuallyVisible(node), "Osty HP visible immediately after death undo");
            await Frames(180);
            Check(HealthActuallyVisible(node), "old death fade cannot hide restored Osty HP");
            Check(node.Visuals.Scale.IsEqualApprox(scale), "old death continuation cannot shrink restored Osty");
            Check(node._stateDisplay._blockTrackingCreature == player.Creature, "Osty still tracks owner block");
            await CheckIdleKeepsMoving(node, "Osty death undo");
        }
        await Screenshot("osty-health-final");
    }
}
