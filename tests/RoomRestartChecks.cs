using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using SpireMouse.Timeline;
using STS2QuickAnimationMode.Utils;

namespace SpireMouse;
internal static partial class RuntimeVerification
{
    private static async Task VerifyRoomRestarts()
    {
        var manager = RunManager.Instance;
        manager.ShouldSave = true;
        var run = manager.DebugOnlyGetState()!;
        await SaveManager.Instance.SaveRun(run.CurrentRoom);
        int gold = run.Players[0].Gold;
        Check(TimelineEngine.HandleQuickRestartKey(), "restart available after combat on map");
        await TimelineEngine.NavigationTask!;
        await Until(() => !RestartPresentation.IsActive, "map restart reveal finishes");
        Check(TimelineEngine.Status == "" && !CombatManager.Instance.IsInProgress
            && manager.DebugOnlyGetState()!.Players[0].Gold == gold,
            "map restart preserves completed combat and earned gold");
        foreach (var kind in new[] { MapPointType.Shop, MapPointType.RestSite, MapPointType.Treasure, MapPointType.Unknown })
        {
            run = manager.DebugOnlyGetState()!;
            var coord = run.Map!.GetAllMapPoints().First(p => p.PointType == kind).coord;
            await manager.EnterMapCoord(coord);
            await Until(() => !NGame.Instance!.Transition.InTransition, "room transition");
            await SaveManager.Instance.SaveRun(null);
            var entry = SaveManager.Instance.LoadRunSave().SaveData!;
            gold = run.Players[0].Gold;
            int hp = run.Players[0].Creature.CurrentHp;
            var roomType = run.CurrentRoom!.RoomType;
            await PlayerCmd.LoseGold(7, run.Players[0]);
            run.Players[0].Creature.SetCurrentHpInternal(hp - 3);
            await Frames(4); // Let the newly entered room render before capturing its restart frame.
            var previousNavigation = TimelineEngine.NavigationTask;
            if (kind == MapPointType.Shop)
            {
                var viewport = NGame.Instance!.GetViewport();
                foreach (var b in new[] { MouseButton.Xbutton1, MouseButton.Xbutton2 })
                    viewport.PushInput(new InputEventMouseButton { ButtonIndex = b, Pressed = true }, true);
                await Frames(1);
                foreach (var b in new[] { MouseButton.Xbutton1, MouseButton.Xbutton2 })
                    viewport.PushInput(new InputEventMouseButton { ButtonIndex = b, Pressed = false }, true);
            }
            else
            {
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.F5, Pressed = true });
                Input.ParseInputEvent(new InputEventKey { Keycode = Key.F5, Pressed = false });
            }
            await Until(() => !ReferenceEquals(previousNavigation, TimelineEngine.NavigationTask), "restart shortcut dispatched");
            Check(RestartPresentation.IsActive, $"{kind} reload keeps previous frame covered");
            await Screenshot("restart-loading-" + kind);
            await TimelineEngine.NavigationTask!;
            await Until(() => !RestartPresentation.IsActive, "room restart reveal finishes");
            var restored = manager.DebugOnlyGetState()!;
            Check(TimelineEngine.Status == "" && !ReferenceEquals(run, restored) && restored.CurrentRoom!.RoomType == roomType
                && restored.Players[0].Gold == gold && restored.Players[0].Creature.CurrentHp == hp,
                $"{kind} shortcut restarts same room from saved checkpoint");
            Check(TimelineEngine.Points.Count == 0 && !TimelineEngine.HandleUndoKey(), $"{kind} restart clears old combat history");
            using (SpeedManager.BeginScope(SafeSpeedReason.CardPlayResolution))
                Check(Math.Abs(Engine.TimeScale - SettingsStore.Current.Speed.SpeedMultiplier) < .01,
                    $"{kind} restart keeps configured acceleration");
            Check(!RestartPresentation.IsActive, $"{kind} transition releases screen overlay");
            if (kind == MapPointType.Unknown)
            {
                await Until(() => MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom.Instance?.DefaultFocusedControl?.IsVisibleInTree() == true, "restored event options ready");
                await Frames(30);
                Check(MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom.Instance!.DefaultFocusedControl != null, "event reload restores usable options");
            }
            await Screenshot("restart-" + kind);
        }
    }
}







