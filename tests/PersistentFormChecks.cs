using Godot;
using System.Reflection;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx.Forms;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;
using MegaCrit.Sts2.Core.Runs;
using SpireMouse.Timeline;

namespace SpireMouse;
internal static partial class RuntimeVerification
{
    private static async Task VerifyPersistentForms(Type cardType)
    {
        var player = RunManager.Instance.DebugOnlyGetState()!.Players[0];
        var room = NCombatRoom.Instance!;
        string initial = State();
        var card = NPlayerHand.Instance!.ActiveHolders.First(h => h.CardModel.GetType() == cardType);
        await Click(Center(card.Hitbox), MouseButton.Right);
        await Until(() => TimelineEngine.Settled && TimelineEngine.Cursor == 1, "persistent form applied");
        string after = State();
        var original = room.GetCreatureNode(player.Creature)!.Visuals.FormVfxHolder.GetChildren().OfType<NFormVfx>().Single();
        var directions = PersistentCreatureEffects.Descendants(original).OfType<NValueRamp>().Select(r => r._isIncreasing).ToArray();
        for (int i = 0; i < 4; i++)
        {
            var oldView = room.GetCreatureNode(player.Creature)!;
            var oldEffect = oldView.Visuals.FormVfxHolder.GetChildren().OfType<NFormVfx>().Single();
            Check(TimelineEngine.HandleUndoKey(), "form undo accepted " + i);
            await TimelineEngine.NavigationTask!;
            Check(TimelineEngine.Status == "" && State() == initial, "form undo restores exact initial model " + i);
            Check(!player.Creature.Powers.Any(p => p.GetType().Name == cardType.Name + "Power"), "form power removed by undo " + i);
            Check(!room.GetCreatureNode(player.Creature)!.Visuals.FormVfxHolder.GetChildren().OfType<NFormVfx>().Any(), "form appearance removed by undo " + i);
            await Frames(3);
            Check(!GodotObject.IsInstanceValid(oldEffect), "old persistent effect is retired " + i);
            Check(TimelineEngine.Navigate(1), "form redo accepted " + i);
            await TimelineEngine.NavigationTask!;
            Check(TimelineEngine.Status == "" && State() == after, "form redo restores exact model " + i);
            var effect = room.GetCreatureNode(player.Creature)!.Visuals.FormVfxHolder.GetChildren().OfType<NFormVfx>().Single();
            Check(effect.GetType() == original.GetType() && effect.IsInsideTree(), "persistent appearance reconstructed " + i);
            var power = player.Creature.Powers.Single(p => p.GetType().Name == cardType.Name + "Power");
            Check(ReferenceEquals(power.GetType().GetField("_vfx", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(power), effect), "power refers to the reconstructed effect " + i);
            Check(PersistentCreatureEffects.Descendants(effect).OfType<NValueRamp>().Select(r => r._isIncreasing).SequenceEqual(directions), "persistent effect fade direction restored " + i);
        }
        int round = CombatManager.Instance.DebugOnlyGetState()!.RoundNumber;
        await EndStressTurn();
        await Until(() => TimelineEngine.Settled && CombatManager.Instance.DebugOnlyGetState()!.RoundNumber > round, "form next-turn effect");
        await Until(() => TimelineEngine.Points[TimelineEngine.Cursor].State!.SequenceEqual(TimelineEngine.Fingerprint()), "form next-turn checkpoint recorded");
        if (cardType == typeof(DemonForm))
            Check(player.Creature.Powers.OfType<StrengthPower>().Single().Amount == player.Creature.Powers.OfType<DemonFormPower>().Single().Amount,
                "rebound power applies strength exactly once on the next turn");
        var triggeredPower = player.Creature.Powers.Single(p => p.GetType().Name == cardType.Name + "Power");
        var triggered = triggeredPower.GetType().GetField("_vfx", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(triggeredPower) as NFormVfx;
        Check(triggered != null && GodotObject.IsInstanceValid(triggered) && triggered.IsInsideTree(), "triggered form still has a live presentation owner");
        var attack = NPlayerHand.Instance!.ActiveHolders.First(h => h.CardModel is StrikeIronclad);
        int cursor = TimelineEngine.Cursor;
        await Click(Center(attack.Hitbox), MouseButton.Right);
        await Click(Center(room.CreatureNodes.First(n => n.Entity.IsEnemy).Hitbox), MouseButton.Right);
        await Until(() => TimelineEngine.Settled && TimelineEngine.Cursor > cursor
            && TimelineEngine.Points[TimelineEngine.Cursor].State!.SequenceEqual(TimelineEngine.Fingerprint()), "reconstructed form permits subsequent card effect");
        var fingerprint = TimelineEngine.Fingerprint();
        int latest = TimelineEngine.Cursor;
        Check(TimelineEngine.Navigate(cursor), "undo subsequent form card");
        await TimelineEngine.NavigationTask!;
        Check(TimelineEngine.Status == "", "undo under active form succeeds");
        Check(TimelineEngine.Navigate(latest), "redo subsequent form card");
        await TimelineEngine.NavigationTask!;
        Check(TimelineEngine.Status == "" && TimelineEngine.Fingerprint().SequenceEqual(fingerprint), "redo under active form restores exact gameplay state");
        await CheckIdleKeepsMoving(room.GetCreatureNode(player.Creature)!, "persistent form idle");
        await Screenshot("persistent-form-reconstructed");
    }
}
