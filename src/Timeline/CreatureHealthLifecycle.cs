using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace SpireMouse.Timeline;

// Reevaluates a rebuilt view's targeting and health display. Since v0.113 the game
// owns this rule (NCreature.UpdateInteractability): hidden companions, creatures
// that are dead but stay in combat, and views switched off by flee/reattach are
// neither targetable nor show a health bar, and native resurrection recomputes it.
// The stable branch (v0.111) predates that rule; there the mod applies the
// monster's health-bar rule itself, including when native resurrection fades the
// display back in (AnimEnableUi).
internal static class CreatureHealthLifecycle
{
    internal static void Refresh(NCreature __instance) => GameCompat.RefreshInteractability(__instance);
}

[HarmonyPatch(typeof(NCreature), "AnimEnableUi")]
internal static class LegacyCreatureHealthLifecycle
{
    // Native resurrection fades alpha back in but assumes Visible was never changed.
    static bool Prepare() => !GameCompat.HasNativeInteractability && AccessTools.Method(typeof(NCreature), "AnimEnableUi") != null;
    static void Prefix(NCreature __instance) => __instance._stateDisplay.Visible = GameCompat.HealthBarShown(__instance);
}
