using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireMouse.Timeline;

namespace SpireMouse;
internal static partial class RuntimeVerification
{
    private static async Task VerifyRestoreLifecycle()
    {
        var room = NCombatRoom.Instance!;
        var player = RunManager.Instance.DebugOnlyGetState()!.Players[0];
        var card = NPlayerHand.Instance!.ActiveHolders.First(h => h.CardModel is StrikeIronclad);
        await Click(Center(card.Hitbox), MouseButton.Right);
        await Click(Center(room.CreatureNodes.First(n => n.Entity.IsEnemy).Hitbox), MouseButton.Right);
        await Until(() => TimelineEngine.Settled && TimelineEngine.Cursor == 1, "lifecycle rollback fixture action");
        string before = State();
        bool faultReached = false;
        RestoreTransaction.BeforeValidation = () =>
        {
            RestoreTransaction.BeforeValidation = null;
            faultReached = true;
            Check(RunManager.Instance.ActionExecutor.IsPaused && CombatManager.Instance.PlayerActionsDisabled,
                "validation runs while action execution is locked");
            Check(RunManager.Instance.ActionQueueSet._actionQueues.All(q => q.isPaused), "every player queue remains locked during restore");
            throw new InvalidOperationException("Deliberate verification-only restore fault");
        };
        try
        {
            Check(TimelineEngine.HandleUndoKey(), "fault injection undo accepted");
            await TimelineEngine.NavigationTask!;
        }
        finally { RestoreTransaction.BeforeValidation = null; }
        Check(faultReached && State() == before && TimelineEngine.Cursor == 1, "failed restoration rolls back the exact pre-operation model and cursor");
        Check(TimelineEngine.Settled && !RunManager.Instance.ActionExecutor.IsPaused && !CombatManager.Instance.PlayerActionsDisabled,
            "rollback releases the executor and player input");
        Check(room.CreatureNodes.All(HealthActuallyVisible), "rollback reconstructs opaque health displays");
        Check(TimelineEngine.HandleUndoKey(), "undo remains usable after recovered failure");
        await TimelineEngine.NavigationTask!;
        Check(TimelineEngine.Status == "" && TimelineEngine.Cursor == 0, "subsequent undo succeeds");

        var snapshot = new CombatSnapshot();
        var fingerprint = TimelineEngine.Fingerprint();
        var times = new List<double>();
        for (int repeat = 0; repeat < 12; repeat++)
        {
            var oldViews = room.CreatureNodes.ToArray();
            foreach (var node in oldViews)
            {
#if STABLE_GAME
                node.AnimDisableUi();
#else
                node.AnimateOutStateDisplay();
#endif
                node.AnimShake();
            }
            var timer = System.Diagnostics.Stopwatch.StartNew();
            snapshot.Restore();
            times.Add(timer.Elapsed.TotalMilliseconds);
            Check(TimelineEngine.Fingerprint().SequenceEqual(fingerprint), "rebuild preserves exact gameplay state " + repeat);
            Check(oldViews.All(n => !n.IsInsideTree()), "old presentation owners detach before the next frame " + repeat);
            Check(room.CreatureNodes.Count() == CombatManager.Instance.DebugOnlyGetState()!.Creatures.Count(), "one live view per creature " + repeat);
            Check(room.CreatureNodes.All(HealthActuallyVisible), "new displays immediately visible " + repeat);
            await Frames(3);
            Check(oldViews.All(n => !GodotObject.IsInstanceValid(n)), "old view resources retire without accumulation " + repeat);
        }
        await Frames(100);
        Check(room.CreatureNodes.All(HealthActuallyVisible), "old delayed fades cannot affect the rebuilt views");
        await CheckIdleKeepsMoving(room.GetCreatureNode(player.Creature)!, "rebuilt player");
        var sorted = times.Order().ToArray();
        Check(sorted[(int)(sorted.Length * .95)] < 100, "rebuild and rollback preparation p95 stays under 100 ms");
        File.WriteAllText(Path.Combine(DirectoryPath!, "restore-timing.json"), JsonSerializer.Serialize(new { milliseconds = times, median = sorted[sorted.Length / 2], maximum = sorted[^1] }));
        await Screenshot("restore-lifecycle-final");
    }
}
