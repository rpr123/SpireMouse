using MegaCrit.Sts2.Core.Combat;

namespace SpireMouse.Timeline;

// Stable-branch verification builds only: the game has no arrows, and the
// production code calls ArrowPresentation only when GameCompat.HasFoolArrows.
internal static class ArrowPresentation
{
    internal static void Refresh(CombatState state) => throw new InvalidOperationException("No Fool arrows on this game branch.");
}
