using MegaCrit.Sts2.Core.Nodes.Combat;

namespace SpireMouse.Timeline;

// Reevaluates a rebuilt view's targeting and health display. Since v0.113 the game
// owns this rule (NCreature.UpdateInteractability): hidden companions, creatures
// that are dead but stay in combat, and views switched off by flee/reattach are
// neither targetable nor show a health bar. Native resurrection recomputes it
// itself, so the former AnimEnableUi hook is no longer needed.
internal static class CreatureHealthLifecycle
{
    internal static void Refresh(NCreature __instance) => __instance.UpdateInteractability(changeVisibilityImmediately: true);
}
