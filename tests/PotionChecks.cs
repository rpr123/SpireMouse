using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using SpireMouse.Timeline;
namespace SpireMouse;
internal static partial class RuntimeVerification
{
    private static async Task VerifyPotionUndo()
    {
        var player = RunManager.Instance.DebugOnlyGetState()!.Players[0];
        var belt = NRun.Instance!.GlobalUi.TopBar.PotionContainer;
        var potion = player.PotionSlots[0];
        string before = State();
        await belt._holders[0].UsePotion();
        await Until(() => TimelineEngine.Settled && TimelineEngine.Cursor == 1, "potion use checkpoint");
        string after = State();
        Check(player.PotionSlots[0] == null && !belt._holders[0].HasPotion, "used potion removed from model and belt");
        await Move(MouseButton.Xbutton1);
        Check(State() == before && player.PotionSlots[0] == potion, "potion undo restores model and effect");
        Check(belt._holders[0].Potion?.Model == potion && !belt._holders[0]._disabledUntilPotionRemoved, "potion undo restores usable visible slot");
        await Screenshot("potion-restored");
        await Move(MouseButton.Xbutton2);
        Check(State() == after && !belt._holders[0].HasPotion, "potion redo removes visible slot and restores effect");
        await Move(MouseButton.Xbutton1);
        await belt._holders[0].UsePotion();
        await Until(() => TimelineEngine.Settled && TimelineEngine.Cursor == 1, "restored potion reused");
        Check(State() == after, "restored potion can be used again");
        await Move(MouseButton.Xbutton1);
    }
}
