using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Runs;
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

    private static async Task RebuildCheckpoint(Point target)
    {
        var events = target.Segment.Events.Take(target.Events).Select(Decode<CombatReplayEvent>).ToArray();
        // Replay is bounded to the chosen checkpoint, never its retained redo tail.
        DiscardObjectSnapshots();
        var entry = Decode<CombatReplay>(_entry!);
        await LoadNativeRoom(entry.serializableRun, replay: entry);
        _segment = target.Segment;
        ReplayChoices.Clear(); Reserved.Clear();
        await Wait(() => Settled);
        _start = new(0, false, "전투 시작", Fingerprint(), new CombatSnapshot());
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
#if VERIFY
        BeforeHistoryRebuildValidation?.Invoke();
#endif
        if (ReplayChoices.Count != 0 || target.State == null || !Fingerprint().SequenceEqual(target.State))
            throw new InvalidOperationException("재시작 이전 전투 기록이 원래 상태와 일치하지 않습니다.");
        if (target.Choice) target.Anchor = _start;
        else target.Snapshot = new CombatSnapshot();
    }

    private static void CacheRebuiltPoints(int eventCount)
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
