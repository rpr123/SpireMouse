using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using STS2QuickAnimationMode.Utils;

namespace SpireMouse.Timeline;

internal static partial class TimelineEngine
{
    private static bool RestartSavedRoom()
    {
        if (IsNavigationInProgress || !Singleplayer || NRun.Instance == null
            || NGame.Instance?.Transition.InTransition != false
            || RunManager.Instance.DebugOnlyGetState() == null || !SaveManager.Instance.HasRunSave) return false;
        NavigationTask = ReloadSavedRoom();
        return true;
    }

    private static async Task ReloadSavedRoom(SerializableRun? combatEntry = null)
    {
        IsNavigationInProgress = true;
        Status = "현재 층 재시작 중…";
        var manager = RunManager.Instance;
        bool shouldSave = manager.ShouldSave;
        bool replaced = false;
        bool success = false;
        var presentation = RestartPresentation.Begin();
        MouseInput.CancelTarget();
        // Clear stale speed scopes, then use the configured speed for the native intro.
        SpeedManager.BeginTimelineRestore();
        SpeedManager.EndTimelineRestore();
        try
        {
            if (SaveManager.Instance.CurrentRunSaveTask is { } saving) await saving;
            var save = combatEntry;
            if (save == null)
            {
                var result = SaveManager.Instance.LoadRunSave();
                if (!result.Success || result.SaveData == null) throw new InvalidOperationException("현재 진행 저장을 읽을 수 없습니다.");
                save = result.SaveData;
            }
            var state = ValidateRestartState(save);
            // Use the latest native room-entry checkpoint, including completed
            // room metadata. Do not create a new run or discard earned rewards.
            _entry = null; _start = null; Points.Clear(); Journal.Clear(); Cursor = -1;
            _dirty = false; _choiceWasVisible = false; Reserved.Clear();
            replaced = true;
            await LoadNativeRoom(save, combatEntry != null, state);
            success = true;
            Status = "";
        }
        catch (Exception error)
        {
            Status = "층 재시작 실패 — 로그를 확인하세요";
            Entry.Log.Error($"Room restart failed: {error}");
        }
        finally
        {
            manager.ShouldSave = shouldSave && (!replaced || success);
            IsNavigationInProgress = false;
            presentation?.Reveal();
            if (success && CombatManager.Instance.IsInProgress && manager.CombatReplayWriter._replay is { } replay)
                Capture(replay);
        }
    }

    private static RunState ValidateRestartState(SerializableRun save)
    {
        var manager = RunManager.Instance;
        var state = RunState.FromSerializable(save);
        if (save.StartTime != manager._startTime || state.Rng.Seed != manager.DebugOnlyGetState()!.Rng.Seed)
            throw new InvalidOperationException("현재 진행과 저장 파일이 다릅니다.");
        return state;
    }

    private static async Task LoadNativeRoom(SerializableRun save, bool combat, RunState? preparedState = null)
    {
        var manager = RunManager.Instance;
        if (SaveManager.Instance.CurrentRunSaveTask is { } saving) await saving;
        var state = preparedState ?? ValidateRestartState(save);
        manager.CleanUp(false);
        await Frames(2);
        manager.State = state;
        var net = new NetSingleplayerGameService();
        manager.InitializeShared(net, new PeerInputSynchronizer(net), false, save.DailyTime,
            save.StartTime, save.RunTime, save.WinTime, save.NumReloads);
        manager.InitializeRunLobby(net, state, [new RunLobbyPlayer { id = net.NetId, isModded = true }]);
        manager.InitializeSavedRun(save);
        await NGame.Instance!.LoadRun(state, combat ? null : save.PreFinishedRoom);
#if VERIFY
        if (combat && DiagnosticRoomLoader != null) await DiagnosticRoomLoader();
#endif
    }
}



