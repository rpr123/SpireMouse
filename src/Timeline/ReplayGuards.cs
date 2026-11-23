using HarmonyLib;
using MegaCrit.Sts2.Core.Audio.Debug;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
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
[HarmonyPatch(typeof(NAudioManager), nameof(NAudioManager.PlayOneShot),
    [typeof(string), typeof(Dictionary<string, float>), typeof(float)])]
internal static class ReplayOneShotGuard
{
    static bool Prefix() => !TimelineEngine.IsRebuildingHistory;
}
[HarmonyPatch(typeof(NDebugAudioManager), nameof(NDebugAudioManager.Play))]
internal static class ReplayDebugAudioGuard
{
    static bool Prefix(ref int __result)
    {
        if (!TimelineEngine.IsRebuildingHistory) return true;
        __result = -1; return false;
    }
}
[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SavePrefsFile))]
internal static class ReplayPrefsGuard
{
    // A rebuild temporarily selects Instant mode; never persist that choice.
    static void Prefix()
    {
        if (TimelineEngine.RebuildOriginalFastMode is { } mode) SaveManager.Instance.PrefsSave.FastMode = mode;
    }
}
[HarmonyPatch(typeof(NBackgroundModeHandler), "EnterBackgroundMode")]
internal static class ReplayBackgroundFpsGuard
{
    // Let background mode save the user's limit, not the rebuild's temporary one.
    static void Prefix()
    {
        if (TimelineEngine.RebuildOriginalMaxFps is { } fps) Godot.Engine.MaxFps = fps;
    }
}
