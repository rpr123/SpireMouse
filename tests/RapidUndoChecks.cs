using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireMouse.Timeline;
namespace SpireMouse;
internal static partial class RuntimeVerification
{
    private static void BurstUndo(int count)
    {
        var viewport = NGame.Instance!.GetViewport();
        for (int i = 0; i < count; i++)
        {
            viewport.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Xbutton1, Pressed = true }, true);
            viewport.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Xbutton1, Pressed = false }, true);
        }
    }
    private static async Task EndStressTurn()
    {
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.E, Pressed = true });
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.E, Pressed = false });
        await Frames(2);
    }
    private static async Task VerifyRapidUndo()
    {
        var manager = RunManager.Instance;
        var player = manager.DebugOnlyGetState()!.Players[0];
        player.Creature.SetMaxHpInternal(10000);
        player.Creature.SetCurrentHpInternal(10000);
        TimelineEngine.Capture(manager.CombatReplayWriter._replay!);
        await Until(() => TimelineEngine.Settled && TimelineEngine.Points.Count == 1, "stress baseline");
        for (int turn = 0; turn < 8; turn++)
        {
            int round = CombatManager.Instance.DebugOnlyGetState()!.RoundNumber;
            await EndStressTurn();
            await Until(() => TimelineEngine.Settled && CombatManager.Instance.DebugOnlyGetState()!.RoundNumber > round
                && TimelineEngine.Points.Count == turn + 2, "long combat checkpoint");
        }
        await EndStressTurn();
        await Until(() => CombatManager.Instance.IsEnemyTurnStarted, "stress enemy turn");
        var oldSide = CombatManager.Instance.DebugOnlyGetState()!.CurrentSide;
        BurstUndo(20);
        Check(TimelineEngine.IsNavigationInProgress && CombatManager.Instance.DebugOnlyGetState()!.CurrentSide == oldSide,
            "rapid undo waits for live enemy turn instead of replacing its side");
        await TimelineEngine.NavigationTask!;
        Check(TimelineEngine.Status == "" && CombatManager.Instance.DebugOnlyCurrentTurnLoopTask?.IsFaulted == false,
            "enemy turn loop survives queued undo");
        BurstUndo(30);
        await TimelineEngine.NavigationTask!;
        await Frames(40);
        var button = NCombatRoom.Instance!.Ui.EndTurnButton;
        Check(TimelineEngine.Cursor == 0 && button.CanTurnBeEnded && button.IsVisibleInTree()
            && button.Position.DistanceTo(button.ShowPos) < 1,
            "long history undo burst keeps turn button visible and actionable");
        await CheckIdleKeepsMoving(NCombatRoom.Instance.GetCreatureNode(player.Creature)!, "player after 50 rapid undo inputs");
        await Screenshot("rapid-undo-turn-button");
        int restoredRound = CombatManager.Instance.DebugOnlyGetState()!.RoundNumber;
        await EndStressTurn();
        await Until(() => TimelineEngine.Settled && CombatManager.Instance.DebugOnlyGetState()!.RoundNumber > restoredRound,
            "turn continues after rapid undo");
        Check(CombatManager.Instance.DebugOnlyCurrentTurnLoopTask?.IsFaulted == false, "combat continues after long undo burst");
    }
}
