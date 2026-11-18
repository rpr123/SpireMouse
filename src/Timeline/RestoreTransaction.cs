using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace SpireMouse.Timeline;

// Model mutation and presentation replacement are one synchronous operation.
// No frame, player input or queued action can observe a half-restored scene.
internal sealed class RestoreTransaction : IDisposable
{
    private readonly bool _actionsDisabled = CombatManager.Instance.PlayerActionsDisabled;
    private readonly bool _executorPaused = RunManager.Instance.ActionExecutor.IsPaused;
    private readonly List<Action> _releaseQueues = new();
#if VERIFY
    internal static Action? BeforeValidation;
#endif

    internal RestoreTransaction()
    {
        var manager = RunManager.Instance;
        if (manager.ActionExecutor.IsRunning || !manager.ActionQueueSet.IsEmpty)
            throw new InvalidOperationException("Cannot restore while a game action owns the model.");
        foreach (var queue in manager.ActionQueueSet._actionQueues)
        {
            bool paused = queue.isPaused;
            _releaseQueues.Add(() => queue.isPaused = paused);
        }
        Hold();
    }

    internal void Hold()
    {
        RunManager.Instance.ActionExecutor.Pause();
        RunManager.Instance.ActionQueueSet.PauseAllPlayerQueues();
        CombatManager.Instance.PlayerActionsDisabled = true;
    }

    internal static void Apply(CombatSnapshot target, Action? validate = null)
    {
        var rollback = new CombatSnapshot();
        using var transaction = new RestoreTransaction();
        try
        {
            target.RestoreInto(transaction);
#if VERIFY
            BeforeValidation?.Invoke();
#endif
            validate?.Invoke();
        }
        catch (Exception error)
        {
            rollback.RestoreInto(transaction);
            throw new RestoreRecoveredException(error);
        }
    }

    public void Dispose()
    {
        foreach (var release in _releaseQueues) release();
        CombatManager.Instance.PlayerActionsDisabled = _actionsDisabled;
        if (!_executorPaused) RunManager.Instance.ActionExecutor.Unpause();
        NCombatRoom.Instance?.Ui.EndTurnButton.RefreshEnabled();
    }
}

internal sealed class RestoreRecoveredException(Exception cause)
    : Exception("Undo validation failed; the pre-operation state was restored.", cause);
