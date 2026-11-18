using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Runs;
using SpireMouse.Timeline;
namespace SpireMouse;
internal static partial class RuntimeVerification
{
    private static async Task VerifyGeneratedPotionChoice()
    {
        var player=RunManager.Instance.DebugOnlyGetState()!.Players[0];
        string before=State();
        var potion=player.PotionSlots[2]!;
        potion.EnqueueManualUse(player.Creature);
        await Until(()=>NOverlayStack.Instance?.Peek() is NChooseACardSelectionScreen && TimelineEngine.Points[^1].Choice,"attack potion card choice");
        await Move(MouseButton.Xbutton1);
        Check(State()==before && !TimelineEngine.ChoiceVisible,"generated-choice potion undo cancels and restores potion");
        await Move(MouseButton.Xbutton2);
        Check(NOverlayStack.Instance?.Peek() is NChooseACardSelectionScreen,"generated-choice potion redo reopens screen");
        var screen=(NChooseACardSelectionScreen)NOverlayStack.Instance!.Peek()!;
        await Frames(30);
        var holder=Descendants(screen).OfType<NCardHolder>().First(h=>h.IsVisibleInTree());
        await Click(Center(holder.Hitbox),MouseButton.Right);
        await Until(()=>TimelineEngine.Settled && !TimelineEngine.ChoiceVisible && !TimelineEngine.Points[^1].Choice,"generated choice completes");
        string after=State();
        await Move(MouseButton.Xbutton1);
        Check(TimelineEngine.ChoiceVisible,"undo generated selection returns to choice");
        await Move(MouseButton.Xbutton1);
        Check(State()==before,"second undo restores generated-choice baseline");
        await Move(MouseButton.Xbutton2);
        await Move(MouseButton.Xbutton2);
        Check(State()==after && !TimelineEngine.ChoiceVisible,"two redos reproduce generated card exactly");
        TimelineEngine.Navigate(0);await TimelineEngine.NavigationTask!;
        potion.EnqueueManualUse(player.Creature);
        await Until(()=>TimelineEngine.ChoiceVisible && TimelineEngine.Points[^1].Choice,"pending choice restart");
        Check(TimelineEngine.HandleQuickRestartKey(),"restart accepts pending generated choice");
        await TimelineEngine.NavigationTask!;
        await Until(()=>TimelineEngine.Settled && !RestartPresentation.IsActive,"generated choice restart ends");
        Check(State()==before && !TimelineEngine.ChoiceVisible,"restart cancels generated choice without stuck overlay");
        await Screenshot("generated-choice-restored");
    }
}
