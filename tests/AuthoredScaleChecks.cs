using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using SpireMouse.Timeline;

namespace SpireMouse;
internal static partial class RuntimeVerification
{
    private static async Task VerifyAuthoredScale()
    {
        var room = NCombatRoom.Instance!;
        var monster = (Vantom)room.CreatureNodes.Single(n => n.Entity.Monster is Vantom).Entity.Monster!;
        monster.ScaleTo(1.25f, .05f);
        await Until(() => !monster._scaleTween!.IsRunning(), "native custom scale settles");
        var original = room.GetCreatureNode(monster.Creature)!;
        var savedScale = original.GetSpecialNode<Node2D>("Visuals/ScalingBone").Scale;
        Check(savedScale.IsEqualApprox(Vector2.One * 1.25f), "custom authored node has a nondefault native scale");
        var snapshot = new CombatSnapshot();
        var fingerprint = TimelineEngine.Fingerprint();
        for (int i = 0; i < 3; i++)
        {
            monster.ScaleTo(.5f, .05f);
            await Until(() => !monster._scaleTween!.IsRunning(), "later custom scale settles");
            snapshot.Restore();
            var restored = room.GetCreatureNode(monster.Creature)!;
            Check(TimelineEngine.Fingerprint().SequenceEqual(fingerprint), "custom scale rebuild preserves gameplay " + i);
            Check(restored.GetSpecialNode<Node2D>("Visuals/ScalingBone").Scale.IsEqualApprox(savedScale), "authored descendant scale restored " + i);
            Check(monster._scaleTween == null, "model drops retired native tween handle " + i);
            await CheckIdleKeepsMoving(restored, "custom scaled creature " + i);
        }
        int round = CombatManager.Instance.DebugOnlyGetState()!.RoundNumber;
        await EndStressTurn();
        await Until(() => TimelineEngine.Settled && CombatManager.Instance.DebugOnlyGetState()!.RoundNumber > round, "custom scaled monster acts after rebuild");
        Check(TimelineEngine.HandleUndoKey(), "native action remains undoable after custom scale rebuild");
        await TimelineEngine.NavigationTask!;
        Check(TimelineEngine.Status == "", "native action undo succeeds");
        await Screenshot("authored-scale-reconstructed");
    }
}
