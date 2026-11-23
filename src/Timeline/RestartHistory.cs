using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Settings;
using STS2QuickAnimationMode.Utils;

namespace SpireMouse.Timeline;

internal static partial class TimelineEngine
{
#if VERIFY
    internal static Action? BeforeHistoryRebuildValidation;
#endif
    // Each restart starts a fresh input segment in the same linear history.
    // Object snapshots never survive a native room reload; segment journals do.
    private static void DiscardObjectSnapshots()
    {
        foreach (var point in Points) { point.Snapshot = null; point.Anchor = null; }
        _start = null;
    }

    private static async Task RestartCombatWithHistory()
    {
        IsNavigationInProgress = true;
        Status = "현재 층 재시작 중…";
        bool save = RunManager.Instance.ShouldSave;
        var originalCombat = CombatManager.Instance.DebugOnlyGetState();
        var presentation = RestartPresentation.Begin();
        Point? recovery = null;
        int originalCursor = Cursor;
        bool replaced = false;
        MouseInput.CancelTarget();
        SpeedManager.BeginTimelineRestore();
        SpeedManager.EndTimelineRestore();
        try
        {
            // Preserve a pending selection, or wait for the owning action/turn
            // to finish. Never capture a half-executed action as a restart edge.
            await Wait(() => ChoiceVisible || Settled || CombatManager.Instance.IsOverOrEnding
                || !ReferenceEquals(originalCombat, CombatManager.Instance.DebugOnlyGetState()));
            if (CombatManager.Instance.IsOverOrEnding || !ReferenceEquals(originalCombat, CombatManager.Instance.DebugOnlyGetState()))
            { Status = ""; return; }
            Branch();
            AddPoint(ChoiceVisible, ChoiceVisible ? "카드 선택" : _nextLabel);
            recovery = Points[Cursor];
            originalCursor = Cursor;
            if (ChoiceVisible)
            {
                // Finish the old choice continuation while its player and
                // synchronizer still belong to the live run.
                await DismissCardChoice();
                await Wait(() => Settled || CombatManager.Instance.IsOverOrEnding
                    || !ReferenceEquals(originalCombat, CombatManager.Instance.DebugOnlyGetState()));
                if (CombatManager.Instance.IsOverOrEnding || !ReferenceEquals(originalCombat, CombatManager.Instance.DebugOnlyGetState()))
                { Status = ""; return; }
            }
            DiscardObjectSnapshots();
            replaced = true;
            var entry = Decode<CombatReplay>(_entry!);
            await LoadNativeRoom(entry.serializableRun, replay: entry);
            // Reveal now so the native opening draw remains visible at 1x.
            presentation?.Reveal(); presentation = null;
            _segment = new();
            await Wait(() => Settled);
            AddPoint(false, "전투 재시작");
            _dirty = false; _choiceWasVisible = false; _settledFrames = 0;
            ReplayChoices.Clear(); Reserved.Clear();
            Status = "";
        }
        catch (Exception error)
        {
            if (replaced && recovery != null)
            {
                try
                {
                    await RebuildCheckpoint(recovery);
                    Cursor = originalCursor; _dirty = false; _choiceWasVisible = recovery.Choice;
                    Status = "층 재시작 실패 — 변경 전 상태로 복구했습니다";
                    Entry.Log.Warn($"Combat restart recovered: {error}");
                    return;
                }
                catch (Exception rollbackError) { Entry.Log.Error($"Combat restart recovery failed: {rollbackError}"); }
            }
            Status = "층 재시작 실패 — 로그를 확인하세요";
            Entry.Log.Error($"Combat restart failed: {error}");
            if (replaced) { _entry = null; save = false; }
        }
        finally
        {
            RunManager.Instance.ShouldSave = save;
            IsNavigationInProgress = false;
            presentation?.Reveal();
        }
    }

    // True only while a covered rebuild replays recorded input behind the frozen frame.
    internal static bool IsRebuildingHistory { get; private set; }

    private static FastModeType? _rebuildFastMode;
    internal static FastModeType? RebuildOriginalFastMode => _rebuildFastMode;
    internal static int? RebuildOriginalMaxFps { get; private set; }

    private static async Task RebuildCheckpoint(Point target)
    {
        // The cover hides the replay; its one-shot sounds would otherwise still play,
        // and nothing behind it needs presentation time. The game's own Instant mode
        // skips command waits; the user's preference is restored afterward.
        var prefs = SaveManager.Instance.PrefsSave;
        _rebuildFastMode = prefs.FastMode;
        IsRebuildingHistory = true;
        prefs.FastMode = FastModeType.Instant;
        SpeedManager.SetRebuildBoost(true);
        // The frozen cover is already on screen; skip drawing the hidden replay.
        // Without drawing, the frame limit is the only remaining pace.
        bool render = RenderingServer.RenderLoopEnabled;
        RebuildOriginalMaxFps = Engine.MaxFps;
        RenderingServer.RenderLoopEnabled = false;
        Engine.MaxFps = 0;
        try { await RebuildCheckpointCore(target); }
        finally
        {
            RenderingServer.RenderLoopEnabled = render;
            // Background mode may have taken over the limit meanwhile; keep its value.
            if (Engine.MaxFps == 0) Engine.MaxFps = RebuildOriginalMaxFps.Value;
            RebuildOriginalMaxFps = null;
            prefs.FastMode = _rebuildFastMode.Value;
            _rebuildFastMode = null;
            IsRebuildingHistory = false;
            SpeedManager.SetRebuildBoost(false);
        }
    }

    private static async Task RebuildCheckpointCore(Point target)
    {
        var events = target.Segment.Events.Take(target.Events).Select(Decode<CombatReplayEvent>).ToArray();
        // Replay is bounded to the chosen checkpoint, never its retained redo tail.
        _cacheMs = 0;
        var clock = System.Diagnostics.Stopwatch.StartNew();
        ulong frame0 = Engine.GetProcessFrames();
        bool inPlace = _start?.Snapshot != null;
        if (inPlace)
        {
            // Every segment of this combat starts from the same entry state, and the
            // live room's opening snapshot still owns the current models. Restoring it
            // replaces a native room reload; retained snapshots stay valid.
            var start = _start!;
            RestoreTransaction.Apply(start.Snapshot!, () =>
            {
                if (start.State != null && !Fingerprint().SequenceEqual(start.State))
                    throw new InvalidOperationException("Opening checkpoint does not match its recorded model.");
            });
        }
        else
        {
            DiscardObjectSnapshots();
            var entry = Decode<CombatReplay>(_entry!);
            await LoadNativeRoom(entry.serializableRun, replay: entry);
        }
        double loadMs = clock.Elapsed.TotalMilliseconds;
        ulong loadFrames = Engine.GetProcessFrames() - frame0;
        _segment = target.Segment;
        ReplayChoices.Clear(); Reserved.Clear();
        await Wait(() => Settled);
        double openMs = clock.Elapsed.TotalMilliseconds;
        ulong openFrames = Engine.GetProcessFrames() - frame0;
        if (!inPlace) _start = new(0, false, "전투 시작", Fingerprint(), new CombatSnapshot());
        CacheRebuiltPoints(0);
        foreach (var e in events.Where(e => e.eventType == CombatReplayEventType.PlayerChoice))
            ReplayChoices[(e.playerId!.Value, e.choiceId!.Value)] = e.playerChoiceResult!.Value;
        var actions = events.Select((e, index) => (e, index))
            .Where(item => item.e.eventType == CombatReplayEventType.GameAction).ToArray();
        for (int i = 0; i < actions.Length; i++)
        {
            var e = actions[i].e;
            var manager = RunManager.Instance;
            var state = manager.DebugOnlyGetState()!;
            var action = e.action!.ToGameAction(state.Players.First(p => p.NetId == e.playerId));
            SubmittingReplayInput = true;
            try { manager.ActionQueueSynchronizer.RequestEnqueue(action); }
            finally { SubmittingReplayInput = false; }
            await Frames();
            await Wait(() => Settled || ChoiceVisible || CombatManager.Instance.IsOverOrEnding);
            if (CombatManager.Instance.IsOverOrEnding)
                throw new InvalidOperationException("기록 복원 중 전투가 종료되었습니다.");
            if (Settled) CacheRebuiltPoints(i + 1 < actions.Length ? actions[i + 1].index : events.Length);
        }
        await Wait(() => target.Choice ? ChoiceVisible : Settled);
        Entry.Log.Info($"History rebuild ({(inPlace ? "in place" : "room reload")}): load={loadMs:F0}ms/{loadFrames}f open={openMs - loadMs:F0}ms/{openFrames - loadFrames}f "
            + $"replay={clock.Elapsed.TotalMilliseconds - openMs:F0}ms/{Engine.GetProcessFrames() - frame0 - openFrames}f actions={actions.Length} snapshots={_cacheMs:F0}ms");
#if VERIFY
        BeforeHistoryRebuildValidation?.Invoke();
#endif
        if (ReplayChoices.Count != 0 || target.State == null || !Fingerprint().SequenceEqual(target.State))
            throw new InvalidOperationException("재시작 이전 전투 기록이 원래 상태와 일치하지 않습니다.");
        if (target.Choice)
        {
            // Anchor to the nearest rebuilt settled point, as AddPoint does, so a later
            // in-place visit replays only the choice's own action, not the whole combat.
            int at = Points.IndexOf(target);
            target.Anchor = Points.Take(at < 0 ? Points.Count : at)
                .LastOrDefault(p => p.Segment == _segment && p.Snapshot != null && p.Events <= target.Events) ?? _start;
        }
        else target.Snapshot = new CombatSnapshot();
    }

    private static double _cacheMs;
    private static void CacheRebuiltPoints(int eventCount)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        try { CacheRebuiltPointsCore(eventCount); }
        finally { _cacheMs += timer.Elapsed.TotalMilliseconds; }
    }
    private static void CacheRebuiltPointsCore(int eventCount)
    {
        // Rehydrate intermediate settled points too, so later ordinary Undo
        // remains an in-place snapshot restore after crossing a restart.
        var candidates = Points.Where(p => p.Segment == _segment && p.Events == eventCount && !p.Choice).ToArray();
        if (candidates.Length == 0) return;
        var fingerprint = Fingerprint();
        foreach (var point in candidates)
        {
            if (point.State == null || !fingerprint.SequenceEqual(point.State))
                throw new InvalidOperationException("재구성한 중간 전투 기록이 원래 상태와 일치하지 않습니다.");
            point.Snapshot = new CombatSnapshot();
        }
    }
}
