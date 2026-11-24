using System.Reflection;
using HarmonyLib;
using Godot;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;

namespace SpireMouse;

// One DLL serves both game branches: stable (v0.111) and public-beta (v0.113).
// Members that exist on only one branch are reached here by reflection, or from
// methods that run only when that branch is present, so the JIT never has to
// resolve a member the running game lacks.
internal static class GameCompat
{
    private static readonly MethodInfo? UpdateInteractability = AccessTools.Method(typeof(NCreature), "UpdateInteractability");
    private static readonly FieldInfo? InteractionAllowed = AccessTools.Field(typeof(NCreature), "_interactionAllowed");
    private static readonly MethodInfo? HealthBarVisible = AccessTools.PropertyGetter(typeof(MonsterModel), "IsHealthBarVisible");
    private static readonly FieldInfo? TreasureButtonState = AccessTools.Field(typeof(NTreasureRoom), "_proceedButtonState");
    private static readonly FieldInfo? TreasureChestOpened = AccessTools.Field(typeof(NTreasureRoom), "_hasChestBeenOpened");

    // The Fool (v0.113): melody symbols and arrow slots.
    internal static readonly Type? FoolCombatSymbol = AccessTools.TypeByName("MegaCrit.Sts2.Core.Nodes.Combat.NFoolCombatSymbol");
    internal static readonly bool HasFoolArrows = AccessTools.TypeByName("MegaCrit.Sts2.Core.Entities.FoolArrows.ArrowSlot") != null;

    // v0.113 renamed the overlay's selection interface (ICardSelector ->
    // ICardSelectionScreen). On both branches only these two screens implement it.
    internal static bool IsCardSelectionScreen(object? overlay) => overlay is NCardGridSelectionScreen or NChooseACardSelectionScreen;

    // v0.113 lets the game own a view's targeting and health-bar rule. Before it,
    // the bar follows the monster's static IsHealthBarVisible and hides remote
    // players and their pets, as the native view does when it is created. A new
    // view starts out alive, so a creature that is dead but stays in combat (a
    // segment waiting to reattach) gets the native death state: no hitbox and
    // no bar. Native revival (StartReviveAnim) re-enables both.
    internal static void RefreshInteractability(NCreature node)
    {
        if (UpdateInteractability != null) { UpdateInteractability.Invoke(node, [true]); return; }
        node._stateDisplay.Visible = node.Entity.IsAlive && HealthBarShown(node);
        if (node.Entity.IsDead) node.DisableInteractionForDeath();
    }
    internal static bool HasNativeInteractability => UpdateInteractability != null;
    internal static bool HealthBarShown(NCreature node) => !NCombatUi.IsDebugHidingHpBar && !node._isRemotePlayerOrPet
        && (node.Entity.Monster is not { } monster || HealthBarVisible?.Invoke(monster, null) is not false);

    // Flee and reattach switch targeting off on the view itself (v0.113 only).
    internal static object? InteractionAllowedOf(NCreature node) => InteractionAllowed?.GetValue(node);
    internal static void RestoreInteractionAllowed(NCreature node, object? value)
    {
        if (value != null) InteractionAllowed?.SetValue(node, value);
    }

    // TweenHelper.AwaitFinished(tween, owner) returns Task<bool> on stable and
    // Task<TweenOutcome> (with an optional token) on v0.113. Callers only wait.
    private static readonly MethodInfo? AwaitFinished = typeof(TweenHelper).GetMethods(BindingFlags.Public | BindingFlags.Static)
        .FirstOrDefault(m => m.Name == nameof(TweenHelper.AwaitFinished) && m.GetParameters() is { Length: >= 2 } p
            && p[0].ParameterType == typeof(Tween) && p[1].ParameterType == typeof(Node));
    internal static Task AwaitTween(Tween tween, Node owner) => (Task)AwaitFinished!.Invoke(null,
        AwaitFinished.GetParameters().Length == 3 ? [tween, owner, CancellationToken.None] : [tween, owner])!;

    // True once native relic picking has completed and the button means Proceed.
    internal static bool TreasureProceedReady(NTreasureRoom room) => TreasureButtonState != null
        ? TreasureButtonState.GetValue(room)?.ToString() == "Proceed"
        : TreasureChestOpened?.GetValue(room) is true;
}
