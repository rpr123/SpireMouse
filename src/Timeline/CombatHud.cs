using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace SpireMouse.Timeline;

// Adapts the existing HUD to the restored model. Creature-owned UI is created
// by the game itself through CreaturePresentation, not repaired here.
internal static class CombatHud
{
    internal static void Refresh(CombatState state)
    {
        var room = NCombatRoom.Instance!;
        // Native HasMelody also finds hidden symbols queued for deletion. If
        // Undo/Redo run in one frame, detach those retired icons before the
        // combat-state notification asks the native UI to recreate melodies.
        if (GameCompat.FoolCombatSymbol is { } symbolType)
            foreach (var symbol in PersistentCreatureEffects.Descendants(room.Ui).Where(n => symbolType.IsInstanceOfType(n) && n.IsQueuedForDeletion()).ToArray())
                symbol.GetParent()?.RemoveChild(symbol);
        // Model restoration does not emit PotionProcured/UsedPotionRemoved.
        // Reconcile the belt explicitly so restored potions are visible and usable.
        var belt = MegaCrit.Sts2.Core.Nodes.NRun.Instance!.GlobalUi.TopBar.PotionContainer;
        var player = belt._player!;
        belt.GrowPotionHolders(player.MaxPotionCount);
        for (int i = 0; i < belt._holders.Count; i++)
        {
            var holder = belt._holders[i];
            var model = i < player.PotionSlots.Count ? player.PotionSlots[i] : null;
            holder.CancelPotionUseOrDiscard();
            if (holder._popup != null && GodotObject.IsInstanceValid(holder._popup)) holder._popup.Remove();
            MegaCrit.Sts2.Core.Nodes.HoverTips.NHoverTipSet.Remove(holder);
            holder._emptyPotionTween?.Kill();
            // A previously consumed icon may still have a removal tween pending.
            // Hide it without interfering with that tween's cleanup callback.
            foreach (var child in holder.GetChildren().OfType<MegaCrit.Sts2.Core.Nodes.Potions.NPotion>())
                if (child != holder.Potion) child.Hide();
            if (holder.Potion?.Model != model)
            {
                if (holder.Potion is { } oldPotion)
                {
                    holder.RemoveChild(oldPotion);
                    oldPotion.QueueFree();
                    holder.Potion = null;
                }
                if (model != null)
                {
                    var icon = MegaCrit.Sts2.Core.Nodes.Potions.NPotion.Create(model);
                    icon.Position = new Vector2(-30, -30);
                    holder.AddPotion(icon);
                }
            }
            holder._emptyIcon.Modulate = model == null ? Colors.White : Colors.Transparent;
            holder.Visible = i < player.MaxPotionCount;
        }
        belt._focusedHolder = null;
        MegaCrit.Sts2.Core.Nodes.NRun.Instance!.GlobalUi.TopBar.Hp.UpdateHealth(0, 0);
        var gold = MegaCrit.Sts2.Core.Nodes.NRun.Instance!.GlobalUi.TopBar.Gold;
        // A pending gold-count animation must not keep displaying the old total.
        // Its continuation reads these fields, so let it finish with a zero delta.
        gold._currentGold = player.Gold;
        gold._additionalGold = 0;
        gold._goldLabel.SetTextAutoSize(player.Gold.ToString());
        gold._goldPopupLabel.SetTextAutoSize("");
        var hand = NPlayerHand.Instance!;
        hand.CancelAllCardPlay();
        foreach (var holder in hand.Holders.ToArray()) hand.RemoveCardHolder(holder);
        foreach (var card in PileType.Hand.GetPile(state.Players[0]).Cards)
            hand.Add(NCard.Create(card)!);
        hand.ForceRefreshCardIndices();
        hand.OnCombatStateChanged(state);
        foreach (var holder in hand.ActiveHolders)
        {
            holder._positionCancelToken?.Cancel();
            holder.Position = holder.TargetPosition;
            holder.SetAngleInstantly(holder.TargetAngle);
            holder.SetScaleInstantly(holder._targetScale);
        }
        var endTurn = room.Ui.EndTurnButton;
        endTurn.OnTurnStarted(state);
        endTurn._positionTween?.Kill();
        endTurn.Position = endTurn.ShowPos;
        endTurn.RefreshEnabled();
        foreach (var pile in new NCombatCardPile[] { room.Ui.DrawPile, room.Ui.DiscardPile, room.Ui.ExhaustPile })
        {
            pile._bumpTween?.Kill();
            pile._currentCount = pile._pile.Cards.Count;
            pile._countLabel.SetTextAutoSize(pile._currentCount.ToString());
            pile._countLabel.Scale = Vector2.One;
        }
    }
}
