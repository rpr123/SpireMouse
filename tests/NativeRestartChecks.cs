using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using SpireMouse.Timeline;
using STS2QuickAnimationMode.Utils;

namespace SpireMouse;
internal static partial class RuntimeVerification
{
    private static async Task VerifyNativeRestart()
    {
        SaveManager.Instance.SetFtuesEnabled(false);
        SettingsStore.Current.Speed.AccelerationMode = SpeedAccelerationMode.Off;
        SpeedManager.OnSettingsChanged();
        var run = await NGame.Instance!.StartNewSingleplayerRun(MegaCrit.Sts2.Core.Entities.Players.Player.CreateForNewRun(ModelDb.Character<Ironclad>(), SaveManager.Instance.GenerateUnlockStateFromProgress(), 1uL), true,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "NATIVERESTART", GameMode.Standard, false);
        var point = run.Map!.GetAllMapPoints().First(p => p.PointType == MapPointType.Monster);
        await RunManager.Instance.EnterMapCoord(point.coord);
        await Until(() => TimelineEngine.Settled && TimelineEngine.Points.Count == 1, "native restart initial encounter");
        var initial = State();
        var originalRoom = NCombatRoom.Instance;
        await EndStressTurn();
        await Until(() => TimelineEngine.Settled && CombatManager.Instance.DebugOnlyGetState()!.RoundNumber > 1, "native restart after enemy turn");
        int historyCount = TimelineEngine.Points.Count;
        Check(TimelineEngine.HandleQuickRestartKey(), "native saved combat restart accepted");
        bool sawEmptyHand = false, sawVisibleDraw = false;
        for (int i=0; i<1800; i++)
        {
            var room = NCombatRoom.Instance;
            if (room != null && room != originalRoom)
            {
                int cards = NPlayerHand.Instance?.ActiveHolders.Count() ?? 0;
                sawEmptyHand |= cards == 0;
                if (cards > 0 && cards < 5 && !RestartPresentation.IsActive && !sawVisibleDraw)
                {
                    sawVisibleDraw = true;
                    await Screenshot("native-restart-drawing");
                }
                if (TimelineEngine.NavigationTask!.IsCompleted && TimelineEngine.Settled && TimelineEngine.Points.Count == historyCount + 1) break;
            }
            await Frames(1);
        }
        await TimelineEngine.NavigationTask!;
        Check(TimelineEngine.Status == "" && NCombatRoom.Instance != originalRoom, "native restart rebuilds saved encounter");
        Check(sawEmptyHand, "native restart begins before opening draw");
        Check(sawVisibleDraw, "opening cards visibly draw with restart cover removed");
        Check(State() == initial, "native restart returns exact opening model state");
        Check(SettingsStore.Current.Speed.AccelerationMode == SpeedAccelerationMode.Off && Math.Abs(Engine.TimeScale-1)<.01, "native intro respects configured speed");
        await Screenshot("native-restart-ready");
        await EndStressTurn();
        await Until(() => TimelineEngine.Settled && CombatManager.Instance.DebugOnlyGetState()!.RoundNumber > 1, "restarted turn works");
        await Move(MouseButton.Xbutton1);
        Check(TimelineEngine.Status == "", "undo still works after native restart");
    }
}
