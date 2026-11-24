using System.Reflection;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.FoolArrows;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;

namespace SpireMouse.Timeline;

// Arrow slots (game v0.113) are restored as plain fields, which bypasses the
// events their views listen to. Re-announce the restored model to the arrow bar
// and recreate the arrows stuck in rebuilt creature views, the way ArrowCmd does.
internal static class ArrowPresentation
{
    private static readonly FieldInfo? ArrowsChanged = AccessTools.Field(typeof(PlayerCombatState), "ArrowsChanged");
    private static readonly FieldInfo? StatusUpdated = AccessTools.Field(typeof(ArrowSlot), "ArrowStatusUpdated");

    // Call after the creature views were rebuilt: the retired views have already
    // unsubscribed, so only the arrow bar reacts to the status events.
    // Only call when GameCompat.HasFoolArrows: the stable branch lacks these types.
    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.NoInlining)]
    internal static void Refresh(CombatState state)
    {
        var room = NCombatRoom.Instance!;
        bool any = false;
        foreach (var player in state.Players)
        {
            if (player.PlayerCombatState is not { } combat) continue;
            (ArrowsChanged?.GetValue(combat) as Action)?.Invoke();
            foreach (var slot in combat._arrows)
            {
                any = true;
                (StatusUpdated?.GetValue(slot) as Action)?.Invoke();
            }
        }
        foreach (var creature in state.Creatures)
        {
            if (room.GetCreatureNode(creature) is not { } node) continue;
            foreach (var power in creature.GetPowerInstances<ImpaledPower>())
            {
                var arrows = power.Arrows;
                for (int i = 0; i < arrows.Count; i++)
                {
                    any = true;
                    if (NImpaledArrowVfx.Create(creature, arrows[i]) is not { } arrow) continue;
                    node.Visuals.AddChild(arrow);
                    // Natively each arrow is placed while the power holds only the
                    // earlier ones, which fans them out. Here the power is already
                    // complete, so move this arrow from the last fan position to its own.
                    if (!node.HasSpineArrowSlots) continue;
                    int placed = arrows.Count % arrow._arrowRotationOffsets.Length, own = i % arrow._arrowRotationOffsets.Length;
                    float rotation = arrow._arrowRotationOffsets[own] - arrow._arrowRotationOffsets[placed];
                    var position = arrow._arrowPositionOffsets[own] - arrow._arrowPositionOffsets[placed];
                    arrow._frontArrowVisual.RotationDegrees += rotation; arrow._backArrowVisual.RotationDegrees += rotation;
                    arrow._frontArrowVisual.Position += position; arrow._backArrowVisual.Position += position;
                }
            }
        }
        if (!any) return;
        // Ordinary navigation has no added transition: finish the bar's fade now.
        foreach (var bar in PersistentCreatureEffects.Descendants(room.Ui).OfType<NFoolArrowUi>())
            if (bar._shootTween is { } tween && tween.IsValid()) tween.CustomStep(1);
    }
}
