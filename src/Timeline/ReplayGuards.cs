using HarmonyLib;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Saves;

namespace SpireMouse.Timeline;

[HarmonyPatch(typeof(ActionQueueSynchronizer), nameof(ActionQueueSynchronizer.RequestEnqueue))]
internal static class ReplayInputGuard
{
    static bool Prefix(GameAction action) => !TimelineEngine.IsNavigationInProgress || TimelineEngine.SubmittingReplayInput
        || action is not (PlayCardAction or UsePotionAction or DiscardPotionGameAction or EndPlayerTurnAction);
}
[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveRun))]
internal static class ReplaySaveGuard
{
    static bool Prefix(ref Task __result)
    {
        if (!TimelineEngine.IsNavigationInProgress) return true;
        __result = Task.CompletedTask; return false;
    }
}
[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveProgressFile))]
internal static class ReplayProgressGuard
{
    static bool Prefix() => !TimelineEngine.IsNavigationInProgress;
}
