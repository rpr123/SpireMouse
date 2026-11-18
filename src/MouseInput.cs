using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using SpireMouse.Timeline;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Events.Custom;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;

namespace SpireMouse;

[HarmonyPatch(typeof(NGame), nameof(NGame._Input))]
internal static class MouseInput
{
    internal static NHandCardHolder? PendingHolder;
    private static readonly HashSet<MouseButton> ConsumedButtons = new();
    private static int _targetGeneration;
    private static bool _forwardingE;
    private static InputBinding? _eSource;
    private static readonly Dictionary<MouseButton, InputEventMouseButton> SidePresses = new();
    private static bool _sideChordUsed;
    private static bool _replayingSide;
    private static ulong _lastEmptyClick;
    private static Vector2 _lastEmptyPosition;
    private static object? _lastEmptyCombat;
    private static int _lastEmptyRound;
    private static MouseButton _aimHeldButton;
    private static Vector2 _aimStart;

    [HarmonyPrefix, HarmonyPriority(Priority.First)]
    internal static bool Prefix(NGame __instance, InputEvent inputEvent)
    {
        if (RuntimeVerification.DirectoryPath != null && inputEvent is InputEventKey { Keycode: Key.E } ek)
            Entry.Log.Info($"VERIFY E down={ek.Pressed} forwarded={_forwardingE} source={_eSource} end={NCombatRoom.Instance?.Ui.EndTurnButton.CanTurnBeEnded} focus={__instance.GetViewport().GuiGetFocusOwner()?.GetPath()}");
        if (_forwardingE) return true;
        if (FinishDrag(inputEvent)) return Consume(__instance, inputEvent);
        if (_eSource != null && IsBindingRelease(inputEvent, _eSource))
        {
            _eSource = null;
            ForwardE(__instance, false);
            return Consume(__instance, inputEvent);
        }
        if (RuntimeVerification.DirectoryPath != null && inputEvent is InputEventMouseButton { Pressed: true } diagnostic)
            Entry.Log.Info($"VERIFY INPUT {diagnostic.ButtonIndex} pos={diagnostic.Position} hovered={__instance.GetViewport().GuiGetHoveredControl()?.GetPath()} modal={NModalContainer.Instance?.OpenModal} settings={SettingsPanel.IsOpen}");
        if (inputEvent is InputEventMouseButton up && !up.Pressed && ConsumedButtons.Remove(up.ButtonIndex))
            return Consume(__instance, inputEvent);
        if (SettingsPanel.Capture(inputEvent)) return Consume(__instance, inputEvent);
        if (SettingsPanel.IsOpen || IsTyping(__instance)) return true;
        if (!_replayingSide && inputEvent is InputEventMouseButton side
            && side.ButtonIndex is MouseButton.Xbutton1 or MouseButton.Xbutton2
            && (SettingsStore.Current.SideButtonRestart || SidePresses.Count > 0)
            && HandleSideButtons(__instance, side)) return false;
        // Use the game's E-key dispatch for every context (confirm, continue, end turn).
        // Preserve separate down/up events, so native button and long-press rules apply.
        if (SettingsStore.Current.EnableExtraE && SettingsStore.Is("end_turn", inputEvent))
        {
            if (inputEvent is InputEventKey { Keycode: Key.E, CtrlPressed: false, AltPressed: false, ShiftPressed: false }) return true;
            if (!TimelineEngine.IsNavigationInProgress && _eSource == null)
            {
                _eSource = InputBinding.From(inputEvent);
                ForwardE(__instance, true);
                if (inputEvent is InputEventMouseButton { ButtonIndex: MouseButton.WheelUp or MouseButton.WheelDown or MouseButton.WheelLeft or MouseButton.WheelRight })
                {
                    _eSource = null;
                    ForwardE(__instance, false);
                }
            }
            __instance.GetViewport().SetInputAsHandled();
            return false;
        }
        if (NModalContainer.Instance?.OpenModal != null) return true;
        if (InputBinding.From(inputEvent) == null) return true;
        if (!SettingsStore.Is("quick", inputEvent)) _lastEmptyClick = 0;
        if (SettingsStore.Is("cancel", inputEvent) && PendingHolder != null)
        {
            CancelTarget();
            return Consume(__instance, inputEvent);
        }
        if (SettingsStore.Is("quick", inputEvent))
        {
            if (PendingHolder != null)
            {
                var tm = NTargetManager.Instance;
                if (tm?.IsInSelection == true && tm.HoveredNode is NCreature creature && IsLegal(PendingHolder, creature.Entity))
                    tm.FinishTargeting(false);
                else CancelTarget();
                return Consume(__instance, inputEvent);
            }
            var hoveredControl = __instance.GetViewport().GuiGetHoveredControl();
            if (ProceedFromCompletedRoom(hoveredControl)) { _lastEmptyClick = 0; return Consume(__instance, inputEvent); }
            if (ConfirmCardSelection(hoveredControl)) { _lastEmptyClick = 0; return Consume(__instance, inputEvent); }
            var hovered = FindHolder(hoveredControl);
            if (hovered != null)
            {
                _lastEmptyClick = 0;
                if (QuickAction(hovered))
                {
                    if (PendingHolder != null && inputEvent is InputEventMouseButton mouse)
                    { _aimHeldButton = mouse.ButtonIndex; _aimStart = mouse.Position; }
                    return Consume(__instance, inputEvent);
                }
            }
            else if (IsEmptySpace(hoveredControl) && HandleEmptyClick(__instance))
                return Consume(__instance, inputEvent);
            else _lastEmptyClick = 0;
        }
        if (SettingsStore.Is("undo", inputEvent))
        {
            CancelTarget();
            if (TimelineEngine.HandleUndoKey()) return Consume(__instance, inputEvent);
        }
        else if (SettingsStore.Is("redo", inputEvent))
        {
            CancelTarget();
            if (TimelineEngine.HandleRedoKey()) return Consume(__instance, inputEvent);
        }
        else if (SettingsStore.Is("restart", inputEvent))
        {
            CancelTarget();
            if (TimelineEngine.HandleQuickRestartKey()) return Consume(__instance, inputEvent);
        }
        return true;
    }

    private static bool IsBindingRelease(InputEvent e, InputBinding binding) => e switch
    {
        InputEventMouseButton m => !m.Pressed && binding.Mouse == (long)m.ButtonIndex,
        InputEventKey k => !k.Pressed && binding.KeyCode == (long)k.Keycode,
        _ => false
    };
    private static bool HandleSideButtons(NGame game, InputEventMouseButton e)
    {
        if (e.Pressed)
        {
            if (SidePresses.ContainsKey(e.ButtonIndex)) return true;
            SidePresses[e.ButtonIndex] = (InputEventMouseButton)e.Duplicate();
            if (SidePresses.Count == 2 && !_sideChordUsed)
            {
                _sideChordUsed = true;
                CancelTarget();
                if (!TimelineEngine.IsNavigationInProgress && NModalContainer.Instance?.OpenModal == null)
                    TimelineEngine.HandleQuickRestartKey();
            }
        }
        else if (SidePresses.Remove(e.ButtonIndex, out var original))
        {
            if (!_sideChordUsed)
            {
                _replayingSide = true;
                try { Prefix(game, original); Prefix(game, e); }
                finally { _replayingSide = false; }
            }
            original.Dispose();
            if (SidePresses.Count == 0) _sideChordUsed = false;
        }
        else return false;
        game.GetViewport().SetInputAsHandled();
        return true;
    }
    private static void ForwardE(NGame game, bool pressed)
    {
        Callable.From(() =>
        {
            _forwardingE = true;
            try
            {
                Input.ParseInputEvent(new InputEventKey
                {
                    Keycode = Key.E, PhysicalKeycode = Key.E, Unicode = 'e', Pressed = pressed
                });
            }
            finally { _forwardingE = false; }
        }).CallDeferred();
    }

    private static bool Consume(NGame game, InputEvent e)
    {
        if (e is InputEventMouseButton m && m.Pressed) ConsumedButtons.Add(m.ButtonIndex);
        game.GetViewport().SetInputAsHandled();
        return false;
    }
    internal static bool CanEndTurn() => !SettingsPanel.IsOpen && PendingHolder == null
        && NOverlayStack.Instance?.ScreenCount is 0 && NTargetManager.Instance?.IsInSelection != true
        && !TimelineEngine.IsNavigationInProgress && NCombatRoom.Instance?.Ui.EndTurnButton.CanTurnBeEnded == true;

    private static bool IsEmptySpace(Node? hovered)
    {
        for (var node = hovered; node != null; node = node.GetParent())
            if (node is NClickableControl or BaseButton or NCreature or NCardHolder) return false;
        var hand = NPlayerHand.Instance;
        if (hand?.IsInCardSelection == true && hovered == hand._selectModeBackstop) return true;
        if (NOverlayStack.Instance?.Peek() is ICardSelectionScreen or NRewardsScreen { IsComplete: true } or NCrystalSphereScreen
            && NOverlayStack.Instance.Peek() is Node overlay)
            return hovered == overlay || (hovered != null && overlay.IsAncestorOf(hovered));
        if (ActiveNonCombatRoom() is { } room)
        {
            var root = room is NFakeMerchant ? (Control)NRun.Instance!.EventRoom! : room;
            return hovered == null || hovered == root || hovered == root.GetParent() || root.IsAncestorOf(hovered);
        }
        var scene = NCombatRoom.Instance?.GetNodeOrNull("CombatSceneContainer");
        return scene != null && (hovered == null || hovered == scene || scene.IsAncestorOf(hovered));
    }
    private static Control? ActiveNonCombatRoom()
    {
        var run = NRun.Instance;
        Control? room = (Control?)(run?.EventRoom?.CustomEventNode as NFakeMerchant) ?? (Control?)run?.TreasureRoom ?? (Control?)run?.MerchantRoom
            ?? (Control?)run?.RestSiteRoom ?? run?.EventRoom;
        return room is IScreenContext context && ActiveScreenContext.Instance.IsCurrent(context) ? room : null;
    }
    private static NEventOptionButton? EventProceed(Node node)
    {
        if (node is NEventOptionButton option && option.Option.IsProceed && !option.Option.IsLocked)
            return option;
        foreach (var child in node.GetChildren())
            if (EventProceed(child) is { } found) return found;
        return null;
    }
    private static bool ProceedFromCompletedRoom(Node? hovered)
    {
        var overlay = NOverlayStack.Instance?.Peek();
        if (overlay != null && overlay is not NRewardsScreen { IsComplete: true }
            && overlay is not NCrystalSphereScreen { _entity.IsFinished: true }) return false;
        var treasure = NRun.Instance?.TreasureRoom;
        // Treasure's enabled button can mean Skip while a relic is still available.
        // Only dispatch Proceed after native relic picking has fully completed.
        if (treasure != null && (overlay != null || !ActiveScreenContext.Instance.IsCurrent(treasure)
            || treasure._proceedButtonState != NTreasureRoom.ProceedButtonState.Proceed || treasure._isRelicCollectionOpen
            || !treasure._relicCollection.RelicPickingFinished().IsCompletedSuccessfully
            || treasure.ProceedButton.IsSkip)) return false;
        var room = ActiveNonCombatRoom();
        NButton? roomButton = room switch
        {
            NTreasureRoom t => t.ProceedButton,
            NMerchantRoom merchant when !merchant.Inventory.IsOpen => merchant.ProceedButton,
            NFakeMerchant fake when !fake.Inventory.IsOpen && fake._inputBlocker.MouseFilter == Control.MouseFilterEnum.Ignore => fake._proceedButton,
            NRestSiteRoom rest when rest.Options.Count == 0 => rest.ProceedButton,
            NEventRoom ev when ev._event.IsFinished && ev.Layout != null => EventProceed(ev.Layout),
            _ => null
        };
        NButton? button = overlay switch
        {
            NRewardsScreen rewards => rewards._proceedButton,
            NCrystalSphereScreen sphere => sphere._proceedButton,
            _ => roomButton ?? NCombatRoom.Instance?.ProceedButton
        };
        if (button == null || !button.IsEnabled || !button.IsVisibleInTree() || !button.CanProcess()
            || NGame.Instance?.Transition.InTransition == true || TimelineEngine.IsNavigationInProgress
            || NPlayerHand.Instance?.IsInCardSelection == true
            || NTargetManager.Instance?.IsInSelection == true) return false;
        if (!IsEmptySpace(hovered) && hovered != button && (hovered == null || !button.IsAncestorOf(hovered))) return false;
        button.ForceClick();
        return true;
    }
    private static bool HandleEmptyClick(NGame game)
    {
        if (TimelineEngine.IsNavigationInProgress) { _lastEmptyClick = 0; return true; }
        var hand = NPlayerHand.Instance;
        if (hand?.IsInCardSelection == true)
        {
            _lastEmptyClick = 0;
            ConfirmCardSelection(hand._selectModeConfirmButton);
            return true;
        }
        if (NOverlayStack.Instance?.Peek() is ICardSelectionScreen && NOverlayStack.Instance.Peek() is Node overlay)
        {
            _lastEmptyClick = 0;
            ConfirmFirst(overlay);
            return true;
        }
        if (RuntimeVerification.DirectoryPath != null)
            Entry.Log.Info($"VERIFY EMPTY canEnd={CanEndTurn()} enabled={NCombatRoom.Instance?.Ui.EndTurnButton.IsEnabled} elapsed={Time.GetTicksMsec()-_lastEmptyClick} pending={PendingHolder != null} overlays={NOverlayStack.Instance?.ScreenCount}");
        if (!CanEndTurn() || NCombatRoom.Instance?.Ui.EndTurnButton.IsEnabled != true)
        { _lastEmptyClick = 0; return true; }
        var state = CombatManager.Instance.DebugOnlyGetState();
        var position = game.GetViewport().GetMousePosition();
        var now = Time.GetTicksMsec();
        bool twice = _lastEmptyClick != 0 && now - _lastEmptyClick <= 350
            && position.DistanceTo(_lastEmptyPosition) <= 24
            && ReferenceEquals(state, _lastEmptyCombat) && state?.RoundNumber == _lastEmptyRound;
        _lastEmptyClick = twice ? 0 : now;
        _lastEmptyPosition = position; _lastEmptyCombat = state; _lastEmptyRound = state?.RoundNumber ?? 0;
        if (twice) NCombatRoom.Instance!.Ui.EndTurnButton.CallReleaseLogic();
        return true;
    }
    private static bool ConfirmFirst(Node node)
    {
        if (node is NConfirmButton && ConfirmCardSelection(node)) return true;
        foreach (var child in node.GetChildren()) if (ConfirmFirst(child)) return true;
        return false;
    }

    private static bool IsTyping(NGame game)
    {
        for (Node? n = game.GetViewport().GuiGetFocusOwner(); n != null; n = n.GetParent())
            if (n is LineEdit or TextEdit || n.Name.ToString().Contains("Console", StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
    internal static NCardHolder? FindHolder(Node? node)
    {
        for (; node != null; node = node.GetParent()) if (node is NCardHolder h) return h;
        return null;
    }
    private static bool ConfirmCardSelection(Node? hovered)
    {
        if (TimelineEngine.IsNavigationInProgress) return false;
        for (var node = hovered; node != null; node = node.GetParent())
        {
            if (node is not NConfirmButton button) continue;
            var hand = NPlayerHand.Instance;
            var overlay = NOverlayStack.Instance?.Peek();
            bool isSelectionConfirm = hand?.IsInCardSelection == true
                && button == hand._selectModeConfirmButton;
            isSelectionConfirm |= overlay is ICardSelectionScreen && overlay is Node overlayNode
                && overlayNode.IsAncestorOf(button);
            if (!isSelectionConfirm || !button.IsEnabled || !button.IsVisibleInTree()
                || !button.CanProcess()) return false;
            // Follow the native confirmation callback; never bypass selection limits.
            button.ForceClick();
            return true;
        }
        return false;
    }
    internal static bool QuickAction(NCardHolder holder)
    {
        if (!holder.IsVisibleInTree() || !holder._isClickable || holder.CardNode == null || TimelineEngine.IsNavigationInProgress) return false;
        var hand = NPlayerHand.Instance;
        if (hand?.IsInCardSelection == true || NOverlayStack.Instance?.Peek() is ICardSelectionScreen)
        {
            // Preserve the game's selector limits, disabled cards and confirmation behavior.
            if (holder is NHandCardHolder) holder.EmitSignal(NHandCardHolder.SignalName.HolderMouseClicked, holder);
            else holder.EmitSignal(NCardHolder.SignalName.Pressed, holder);
            return true;
        }
        if (holder is not NHandCardHolder handHolder || hand == null || !hand.CanPlayCards()
            || hand.CurrentMode != NPlayerHand.Mode.Play || NOverlayStack.Instance?.ScreenCount > 0
            || NTargetManager.Instance?.IsInSelection == true || handHolder.CardModel?.CanPlay() != true) return false;
        var card = handHolder.CardModel;
        if (card.TargetType is not (TargetType.AnyEnemy or TargetType.AnyAlly or TargetType.AnyPlayer))
            return card.TryManualPlay(null);
        var legal = NCombatRoom.Instance!.CreatureNodes.Where(n => IsLegal(handHolder, n.Entity)).ToArray();
        if (legal.Length == 1) return card.TryManualPlay(legal[0].Entity);
        if (legal.Length == 0) return true;
        var manager = NTargetManager.Instance;
        if (manager == null || handHolder.CardNode == null) return false;
        PendingHolder = handHolder;
        var generation = ++_targetGeneration;
        manager.StartTargeting(card.TargetType, handHolder.CardNode, TargetMode.ClickMouseToTarget,
            () => generation != _targetGeneration || !GodotObject.IsInstanceValid(handHolder)
                  || !handHolder.IsInsideTree() || !card.CanPlay() || TimelineEngine.IsNavigationInProgress,
            n => n is NCreature creature && IsLegal(handHolder, creature.Entity));
        TaskHelper.RunSafely(FinishTargetAsync(manager, handHolder, generation));
        return true;
    }
    internal static bool IsLegal(NHandCardHolder holder, Creature creature)
    {
        var card = holder.CardModel;
        if (card == null || !creature.IsAlive || !creature.IsHittable) return false;
        bool correctSide = card.TargetType switch
        {
            TargetType.AnyEnemy => creature.IsEnemy,
            TargetType.AnyAlly => creature.IsPlayer && creature != card.Owner.Creature,
            TargetType.AnyPlayer => creature.IsPlayer,
            _ => false
        };
        return correctSide && card.CanPlayTargeting(creature)
            && Hook.ShouldAllowTargeting(creature.CombatState!, creature, out _);
    }
    private static async Task FinishTargetAsync(NTargetManager manager, NHandCardHolder holder, int generation)
    {
        try
        {
            var selected = await manager.SelectionFinished();
            if (generation == _targetGeneration && GodotObject.IsInstanceValid(holder)
                && selected is NCreature target && IsLegal(holder, target.Entity)
                && holder.CardModel?.CanPlay() == true && NPlayerHand.Instance?.CanPlayCards() == true)
            {
                NCombatRoom.Instance!.LastTargetedCreature = target.Entity;
                holder.CardModel.TryManualPlay(target.Entity);
            }
        }
        finally { if (generation == _targetGeneration) PendingHolder = null; }
    }
    internal static void CancelTarget()
    {
        _lastEmptyClick = 0;
        _aimHeldButton = MouseButton.None;
        if (PendingHolder == null) return;
        ++_targetGeneration;
        PendingHolder = null;
        NTargetManager.Instance?.CancelTargeting();
    }
    internal static bool FinishDrag(InputEvent inputEvent)
    {
        if (inputEvent is not InputEventMouseButton { Pressed: false } release
            || release.ButtonIndex != _aimHeldButton) return false;
        _aimHeldButton = MouseButton.None;
        if (PendingHolder == null || release.Position.DistanceTo(_aimStart) < 18) return false;
        var manager = NTargetManager.Instance;
        if (manager?.HoveredNode is NCreature creature && IsLegal(PendingHolder, creature.Entity))
            manager.FinishTargeting(false);
        else CancelTarget();
        ConsumedButtons.Remove(release.ButtonIndex);
        _lastEmptyClick = 0;
        return true;
    }
}

// Target manager normally treats RMB as cancel before NGame's input callback runs.
[HarmonyPatch(typeof(NTargetManager), nameof(NTargetManager._Input))]
internal static class QuickTargetInput
{
    static bool Prefix(NTargetManager __instance, InputEvent inputEvent)
    {
        if (MouseInput.FinishDrag(inputEvent))
        {
            __instance.GetViewport().SetInputAsHandled();
            return false;
        }
        if (MouseInput.PendingHolder == null || !SettingsStore.Is("quick", inputEvent)) return true;
        if (__instance.HoveredNode is NCreature c && MouseInput.IsLegal(MouseInput.PendingHolder, c.Entity))
            __instance.FinishTargeting(false);
        else MouseInput.CancelTarget();
        __instance.GetViewport().SetInputAsHandled();
        return false;
    }
}









