using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput;
using MegaCrit.Sts2.Core.Multiplayer.Replay;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using STS2QuickAnimationMode.Utils;

namespace SpireMouse.Timeline;

// Stable checkpoints restore managed model state in place. A pending choice
// resumes only its own action from the preceding checkpoint, never the room.
internal static partial class TimelineEngine
{
    internal sealed class Segment
    {
        internal readonly List<byte[]> Events = new();
    }
    internal sealed class Point(int events, bool choice, string label, byte[]? state = null,
        CombatSnapshot? snapshot = null, Point? anchor = null)
    {
        internal int Events { get; } = events;
        internal bool Choice { get; } = choice;
        internal string Label { get; } = label;
        internal byte[]? State { get; } = state;
        internal CombatSnapshot? Snapshot { get; set; } = snapshot;
        internal Point? Anchor { get; set; } = anchor;
        internal Segment Segment { get; init; } = _segment;
    }
    private static byte[]? _entry;
    private static Point? _start;
    private static Segment _segment = new();
    private static List<byte[]> Journal => _segment.Events;
    internal static readonly List<Point> Points = new();
    private static readonly Dictionary<(ulong, uint), NetPlayerChoiceResult> ReplayChoices = new();
    private static readonly Dictionary<ulong, uint> Reserved = new();
    internal static int Cursor { get; private set; } = -1;
    internal static bool IsNavigationInProgress { get; private set; }
    internal static string Status { get; private set; } = "";
    private static bool _dirty;
    private static bool _choiceWasVisible;
    private static int _settledFrames;
    private static string _nextLabel = "전투 시작";
#if VERIFY
    internal static Func<Task>? DiagnosticRoomLoader;
#endif
    internal static Task? NavigationTask;
    internal static double LastNavigationMilliseconds { get; private set; }
    internal static bool SubmittingReplayInput { get; private set; }
    internal static byte[] Fingerprint() => Encode(NetFullCombatState.FromRun(RunManager.Instance.DebugOnlyGetState()!, null));

    internal static byte[] Encode<T>(T value) where T : IPacketSerializable
    {
        var writer = new PacketWriter { WarnOnGrow = false };
        writer.Write(value); writer.ZeroByteRemainder();
        return writer.Buffer.AsSpan(0, writer.BytePosition).ToArray();
    }
    internal static T Decode<T>(byte[] bytes) where T : IPacketSerializable, new()
    {
        var reader = new PacketReader(); reader.Reset(bytes); return reader.Read<T>();
    }
    internal static bool Singleplayer => RunManager.Instance.NetService?.Type == NetGameType.Singleplayer;
    internal static bool ChoiceVisible => NPlayerHand.Instance?.IsInCardSelection == true
        || NOverlayStack.Instance?.Peek() is MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.ICardSelectionScreen;
    private static bool PlayerTurnReady => CombatManager.Instance.DebugOnlyGetState()?.CurrentSide == MegaCrit.Sts2.Core.Combat.CombatSide.Player
        && !CombatManager.Instance.IsEnemyTurnStarted
        && !CombatManager.Instance.EndingPlayerTurnPhaseOne && !CombatManager.Instance.EndingPlayerTurnPhaseTwo
        && CombatManager.Instance._turnState?.EndTurnSignalSource?.Task.IsCompleted == false;
    internal static bool Settled => PlayerTurnReady && !RunManager.Instance.ActionExecutor.IsRunning
        && RunManager.Instance.ActionQueueSet.IsEmpty
        && NCombatRoom.Instance?.Ui.EndTurnButton.IsEnabled == true;

    internal static void Capture(CombatReplay replay)
    {
        if (IsNavigationInProgress || !Singleplayer) return;
        _start = null; _entry = Encode(replay); _segment = new(); Points.Clear(); Reserved.Clear();
        Cursor = -1; _dirty = true; _choiceWasVisible = false; _settledFrames = 0;
        _nextLabel = "전투 시작"; Status = "";
    }
    private static void Branch()
    {
        if (Cursor < 0 || Cursor >= Points.Count - 1) return;
        int length = Points[Cursor].Events;
        Journal.RemoveRange(length, Journal.Count - length);
        Points.RemoveRange(Cursor + 1, Points.Count - Cursor - 1);
    }
    internal static void Record(GameAction action)
    {
        if (IsNavigationInProgress || _entry == null || !Singleplayer || CombatManager.Instance.IsOverOrEnding) return;
        // These are player inputs. Hooks and ready-to-begin-enemy-turn are
        // consequences generated by the game and must not be submitted twice.
        if (action is not (PlayCardAction or UsePotionAction or DiscardPotionGameAction or EndPlayerTurnAction or UndoEndPlayerTurnAction)) return;
        Branch();
        Journal.Add(Encode(new CombatReplayEvent { eventType = CombatReplayEventType.GameAction,
            playerId = action.OwnerId, action = action.ToNetAction() }));
        _nextLabel = action switch { PlayCardAction => "카드 사용", EndPlayerTurnAction => "다음 턴", UsePotionAction => "포션 사용", _ => "행동" };
        _dirty = true; _settledFrames = 0;
    }
    internal static void RecordChoice(Player player, uint id, NetPlayerChoiceResult result)
    {
        if (IsNavigationInProgress || _entry == null || !CombatManager.Instance.IsInProgress || !Singleplayer) return;
        Branch();
        Journal.Add(Encode(new CombatReplayEvent { eventType = CombatReplayEventType.PlayerChoice,
            playerId = player.NetId, choiceId = id, playerChoiceResult = result }));
        _dirty = true; _settledFrames = 0; _choiceWasVisible = false;
    }
    internal static void Reserve(Player player, uint id) => Reserved[player.NetId] = id;
    internal static bool HasRecordedChoice(Player player) => IsNavigationInProgress
        && Reserved.TryGetValue(player.NetId, out uint id) && ReplayChoices.ContainsKey((player.NetId, id));
    internal static bool TryChoice(Player player, uint id, out PlayerChoiceResult result)
    {
        if (IsNavigationInProgress && ReplayChoices.Remove((player.NetId, id), out var packet))
        { result = PlayerChoiceResult.FromNetData(player, RunManager.Instance.DebugOnlyGetState()!, packet); return true; }
        result = null!; return false;
    }
    internal static void Tick()
    {
        if (IsNavigationInProgress || _entry == null || !Singleplayer || CombatManager.Instance.IsOverOrEnding) return;
        bool choice = ChoiceVisible;
        if (choice && !_choiceWasVisible && SettingsStore.Current.IncludeChoiceHistory)
        { AddPoint(true, "카드 선택"); _dirty = true; }
        _choiceWasVisible = choice;
        if (choice || !Settled) { _settledFrames = 0; return; }
        if (_dirty && ++_settledFrames >= 4) { AddPoint(false, _nextLabel); _dirty = false; }
    }
    private static void AddPoint(bool choice, string label)
    {
        if (Points.Count > 0 && Points[^1].Segment == _segment && Points[^1].Events == Journal.Count && Points[^1].Choice == choice) return;
        var anchor = choice ? Points.LastOrDefault(p => p.Segment == _segment && p.Snapshot != null) ?? _start : null;
        if (choice && anchor == null) return;
        Points.Add(new(Journal.Count, choice, label, Fingerprint(), choice ? null : new CombatSnapshot(), anchor)); Cursor = Points.Count - 1;
        if (!choice && _start == null) _start = Points[^1];
        int limit = SettingsStore.Current.HistoryLimit;
        if (limit > 0 && Points.Count > limit) { Points.RemoveRange(0, Points.Count - limit); Cursor = Points.Count - 1; }
    }
    internal static bool HandleUndoKey()
    {
        // Journal retains the redo tail. Only newly recorded, unsettled input
        // can require restoring the current point instead of the previous one.
        if (Cursor >= 0 && ((_dirty && Journal.Count > Points[Cursor].Events) || (ChoiceVisible && !Points[Cursor].Choice)))
        {
            if (!CanNavigate()) return false;
            NavigationTask = Restore(Points[Cursor], Cursor, false); return true;
        }
        return Navigate(Cursor - 1);
    }
    internal static bool HandleRedoKey() => Navigate(Cursor + 1);
    internal static bool HandleQuickRestartKey()
    {
        if (!CombatManager.Instance.IsInProgress) return RestartSavedRoom();
        if (!CanNavigate() || _start == null) return false;
        NavigationTask = RestartCombatWithHistory(); return true;
    }
    internal static bool Navigate(int index)
    {
        if (!CanNavigate() || index < 0 || index >= Points.Count || index == Cursor) return false;
        NavigationTask = Restore(Points[index], index, false); return true;
    }
    private static bool CanNavigate() => !IsNavigationInProgress && _entry != null && Singleplayer && !CombatManager.Instance.IsOverOrEnding
        && NGame.Instance?.Transition.InTransition != true && RunManager.Instance.DebugOnlyGetState() != null;
    internal static async Task Frames(int count = 1)
    {
        for (int i = 0; i < count; i++) await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    private static async Task Wait(Func<bool> condition)
    {
        ulong deadline = Time.GetTicksMsec() + 45000;
        while (!condition()) { if (Time.GetTicksMsec() > deadline) throw new TimeoutException("행동 재생 대기 시간 초과"); await Frames(); }
    }
    private static async Task DismissCardChoice()
    {
        if (!ChoiceVisible) return;
        NPlayerHand.Instance?._selectionCompletionSource?.TrySetResult(Array.Empty<MegaCrit.Sts2.Core.Models.CardModel>());
        var selector = NOverlayStack.Instance?.Peek();
        if (selector is MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NCardGridSelectionScreen grid)
        {
            grid._completionSource.TrySetResult(Array.Empty<MegaCrit.Sts2.Core.Models.CardModel>());
            if (NOverlayStack.Instance?.Peek() == grid) NOverlayStack.Instance.Remove(grid);
        }
        else if (selector is MegaCrit.Sts2.Core.Nodes.Screens.CardSelection.NChooseACardSelectionScreen choose)
            choose._completionSource.TrySetResult(Array.Empty<MegaCrit.Sts2.Core.Models.CardModel>());
        await Frames(3);
    }
    private static async Task Restore(Point target, int index, bool restart)
    {
        var timer = System.Diagnostics.Stopwatch.StartNew();
        IsNavigationInProgress = true; Status = "기록 복원 중…";
        var originalCombat = CombatManager.Instance.DebugOnlyGetState();
        bool save = RunManager.Instance.ShouldSave;
        var presentation = restart ? RestartPresentation.Begin() : null;
        Point? recovery = null;
        bool rebuilding = false;
        int originalCursor = Cursor;
        
        MouseInput.CancelTarget(); SpeedManager.BeginTimelineRestore();
        try
        {
            
            // A room reload discards every object-backed snapshot, so any snapshot that
            // still exists belongs to the live models, whichever segment recorded it.
            // Only points without one need the serialized entry plus recorded input.
            rebuilding = target.Snapshot == null && target.Anchor?.Snapshot == null;
            if (rebuilding && ChoiceVisible)
                recovery = new(Journal.Count > (Cursor >= 0 ? Points[Cursor].Events : 0) && _dirty
                    ? Journal.Count : Cursor >= 0 ? Points[Cursor].Events : 0,
                    ChoiceVisible, "복원 전", Fingerprint());
            await DismissCardChoice();
            await Wait(() => CombatManager.Instance.IsOverOrEnding || !ReferenceEquals(originalCombat, CombatManager.Instance.DebugOnlyGetState())
                || (PlayerTurnReady && !RunManager.Instance.ActionExecutor.IsRunning && RunManager.Instance.ActionQueueSet.IsEmpty));
            // The last action can finish combat while navigation waits for it.
            // Never restore a combat snapshot over reward/proceed UI.
            if (CombatManager.Instance.IsOverOrEnding || !ReferenceEquals(originalCombat, CombatManager.Instance.DebugOnlyGetState())) { Status = ""; return; }
            if (rebuilding)
            {
                recovery ??= new(_dirty ? Journal.Count : Cursor >= 0 ? Points[Cursor].Events : 0,
                    false, "복원 전", Fingerprint());
                presentation = RestartPresentation.Begin();
                await RebuildCheckpoint(target);
                Cursor = index; _dirty = false; _choiceWasVisible = target.Choice;
                Status = "";
                return;
            }
            var anchor = target.Snapshot != null ? target : target.Anchor
                ?? throw new InvalidOperationException("No snapshot before choice");
            RestoreTransaction.Apply(anchor.Snapshot!, () =>
            {
                if (anchor.State != null && !Fingerprint().SequenceEqual(anchor.State))
                    throw new InvalidOperationException("Restored checkpoint does not match its recorded model.");
            });
            _segment = target.Segment;
            var events = Journal.Skip(anchor.Events).Take(target.Events - anchor.Events).Select(Decode<CombatReplayEvent>).ToArray();
            ReplayChoices.Clear(); Reserved.Clear();
            foreach (var e in events.Where(e => e.eventType == CombatReplayEventType.PlayerChoice))
                ReplayChoices[(e.playerId!.Value, e.choiceId!.Value)] = e.playerChoiceResult!.Value;
            var manager = RunManager.Instance;
            var state = manager.DebugOnlyGetState()!;
            foreach (var e in events.Where(e => e.eventType == CombatReplayEventType.GameAction))
            {
                var action = e.action!.ToGameAction(state.Players.First(p => p.NetId == e.playerId));
                SubmittingReplayInput = true;
                try { manager.ActionQueueSynchronizer.RequestEnqueue(action); }
                finally { SubmittingReplayInput = false; }
                await Frames(1);
                await Wait(() => Settled || ChoiceVisible);
            }
            await Wait(() => target.Choice ? ChoiceVisible : Settled);
            if (ReplayChoices.Count != 0) throw new InvalidOperationException("일부 선택 기록을 재생하지 못했습니다.");
            if (target.State != null && !Fingerprint().SequenceEqual(target.State))
                throw new InvalidOperationException("재생한 전투 상태가 기록과 다릅니다. 원래 층 저장은 보존됩니다.");
            Cursor = index; _dirty = false; _choiceWasVisible = target.Choice;
            if (restart) { Journal.Clear(); Points.Clear(); Cursor = -1; _dirty = true; _nextLabel = "전투 시작"; }
            Status = "";
        }
        catch (RestoreRecoveredException error)
        {
            Status = "복원 실패 — 변경 전 상태로 복구했습니다";
            Entry.Log.Warn(error.Message);
        }
        catch (Exception error)
        {
            if (rebuilding && recovery != null)
            {
                try
                {
                    await RebuildCheckpoint(recovery);
                    Cursor = originalCursor; _dirty = false; _choiceWasVisible = recovery.Choice;
                    Status = "복원 실패 — 변경 전 상태로 복구했습니다";
                    Entry.Log.Warn($"Restart history recovered: {error}");
                    return;
                }
                catch (Exception rollbackError) { Entry.Log.Error($"Restart history recovery failed: {rollbackError}"); }
            }
            Status = "복원 실패 — 로그를 확인하세요";
            Entry.Log.Error($"Independent timeline restore failed: {error}");
            // Disable further navigation after divergence; keep the original save intact.
            _entry = null;
            save = false;
        }
        finally
        {

            RunManager.Instance.ShouldSave = save;
            ReplayChoices.Clear(); IsNavigationInProgress = false;
            SpeedManager.EndTimelineRestore();
            presentation?.Reveal();
            LastNavigationMilliseconds = timer.Elapsed.TotalMilliseconds;
            Entry.Log.Info($"Snapshot navigation: {LastNavigationMilliseconds:F1}ms; choice={target.Choice}; restart={restart}; status={Status}");
            
        }
    }
}

[HarmonyPatch(typeof(CombatReplayWriter), nameof(CombatReplayWriter.RecordInitialState))]
internal static class EntryRecordPatch
{
    static void Postfix(CombatReplayWriter __instance) { if (__instance._replay != null) TimelineEngine.Capture(__instance._replay); }
}
[HarmonyPatch(typeof(CombatReplayWriter), "RecordGameAction")]
internal static class InputRecordPatch { static void Prefix(GameAction gameAction) => TimelineEngine.Record(gameAction); }
[HarmonyPatch(typeof(PlayerChoiceSynchronizer), nameof(PlayerChoiceSynchronizer.SyncLocalChoice))]
internal static class ChoiceRecordPatch
{
    static void Prefix(Player player, uint choiceId, PlayerChoiceResult result) => TimelineEngine.RecordChoice(player, choiceId, result.ToNetData());
}
[HarmonyPatch(typeof(PlayerChoiceSynchronizer), nameof(PlayerChoiceSynchronizer.ReserveChoiceId))]
internal static class ChoiceReservePatch { static void Postfix(Player player, uint __result) => TimelineEngine.Reserve(player, __result); }
[HarmonyPatch(typeof(CardSelectCmd), "ShouldSelectLocalCard")]
internal static class ChoiceRoutingPatch
{
    static void Postfix(Player player, ref bool __result) { if (TimelineEngine.HasRecordedChoice(player)) __result = false; }
}
[HarmonyPatch(typeof(PlayerChoiceSynchronizer), nameof(PlayerChoiceSynchronizer.WaitForRemoteChoice))]
internal static class RecordedChoicePatch
{
    static bool Prefix(Player player, uint choiceId, ref Task<PlayerChoiceResult> __result)
    {
        if (!TimelineEngine.TryChoice(player, choiceId, out var result)) return true;
        __result = Task.FromResult(result); return false;
    }
}
internal partial class TimelineLoop : Node
{
    public override void _Process(double delta) { ModMenuSettings.TryAttach(); TimelineEngine.Tick(); TimelinePanel.Refresh(); }
}













