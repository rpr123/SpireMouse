using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Events;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Events.Custom;
using MegaCrit.Sts2.Core.Nodes.Events.Custom.CrystalSphere;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Nodes.Screens.CardSelection;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace SpireMouse;
internal static partial class RuntimeVerification
{
    private static async Task VerifyRoomProceed()
    {
        await VerifyTreasureProceed();
        var run = RunManager.Instance.DebugOnlyGetState()!;
        var blank = new Vector2(50, 450);
        foreach (var kind in new[] { MapPointType.Shop, MapPointType.RestSite })
        {
            await RunManager.Instance.EnterMapCoord(run.Map!.GetAllMapPoints().First(p => p.PointType == kind).coord);
            await Until(() => !NGame.Instance!.Transition.InTransition && !NMapScreen.Instance!.IsOpen, kind+" entered");
            await Frames(40);
            if (kind == MapPointType.Shop)
            {
                var shop = NRun.Instance!.MerchantRoom!;
                await Click(Center(shop.MerchantButton), MouseButton.Left);
                await Until(() => shop.Inventory.IsOpen, "merchant inventory opened");
                await Click(blank, MouseButton.Right);
                Check(!NMapScreen.Instance!.IsOpen, "inventory right click does not leave shop");
                shop.Inventory.Close();
                await Until(() => !shop.Inventory.IsOpen && shop.ProceedButton.IsEnabled, "merchant inventory closed");
            }
            else
            {
                var rest = NRun.Instance!.RestSiteRoom!;
                await Click(blank, MouseButton.Right);
                Check(!NMapScreen.Instance!.IsOpen && rest.Options.Count > 0, "rest choices are not skipped by blank right click");
                var option = rest.Options.First(o => o.OptionId == "HEAL");
                await Click(Center(rest.GetButtonForOption(option)!), MouseButton.Left);
                await Until(() => rest.Options.Count == 0 && rest.ProceedButton.IsEnabled, "rest finished");
            }
            await Click(Center(NRun.Instance!.GlobalUi.TopBar.Gold), MouseButton.Right);
            Check(!NMapScreen.Instance!.IsOpen, kind+" top bar does not proceed");
            await Screenshot("proceed-ready-"+kind);
            await Click(blank, MouseButton.Right);
            await Until(() => NMapScreen.Instance!.IsOpen, kind+" right click proceeds");
            Check(true, kind+" blank right click opens map");
        }
        await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<SapphireSeed>(), showTransition: false);
        await Until(() => NEventRoom.Instance?.DefaultFocusedControl?.IsVisibleInTree() == true && !NMapScreen.Instance!.IsOpen, "event options visible");
        await Frames(40);
        var ev = NEventRoom.Instance!;
        await Click(blank, MouseButton.Right);
        Check(!ev._event.IsFinished && !NMapScreen.Instance!.IsOpen, "blank right click does not choose event option");
        var eat = Descendants(ev.Layout!).OfType<NEventOptionButton>().OrderBy(b => b.Index).First();
        await Click(Center(eat), MouseButton.Left);
        await Until(() => NOverlayStack.Instance!.Peek() is ICardSelectionScreen, "event upgrade selector opened");
        await Click(blank, MouseButton.Right);
        Check(!NMapScreen.Instance!.IsOpen && !ev._event.IsFinished, "incomplete event selector not skipped");
        var selector = (Node)NOverlayStack.Instance!.Peek()!;
        var card = Descendants(selector).OfType<NCardHolder>().First(h => h.IsVisibleInTree());
        await Click(Center(card), MouseButton.Right);
        await Click(blank, MouseButton.Right);
        await Until(() => ev._event.IsFinished && NOverlayStack.Instance!.ScreenCount == 0, "event choice completed");
        await Frames(40);
        await Screenshot("event-complete");
        await Click(blank, MouseButton.Right);
        await Until(() => NMapScreen.Instance!.IsOpen, "completed event proceeds");
        Check(true, "finished event blank right click opens map");
        await Screenshot("event-proceeded");
        await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<Neow>(), showTransition: false);
        await Until(() => NEventRoom.Instance?.DefaultFocusedControl?.IsVisibleInTree() == true && !NMapScreen.Instance!.IsOpen, "ancient options visible");
        await Frames(40);
        ev = NEventRoom.Instance!;
        await Click(blank, MouseButton.Right);
        Check(!ev._event.IsFinished && !NMapScreen.Instance!.IsOpen, "ancient rewards not auto-selected");
        // Neow rolls its offers; any plain relic offer exercises the same path.
        var offers = Descendants(ev.Layout!).OfType<NEventOptionButton>().Where(b => b.IsEnabled && b.Option.Relic != null).ToArray();
        Entry.Log.Info("VERIFY ancient offers: " + string.Join(", ", offers.Select(b => b.Option.Relic!.Id.Entry)));
        var gift = offers.FirstOrDefault(b => b.Option.Relic is MegaCrit.Sts2.Core.Models.Relics.BoomingConch) ?? offers.First();
        await Click(Center(gift), MouseButton.Left);
        // Some offers (e.g. a card removal) ask for a card first.
        for (int i = 0; i < 900 && !ev._event.IsFinished; i++)
        {
            if (NOverlayStack.Instance!.Peek() is ICardSelectionScreen && NOverlayStack.Instance.Peek() is Node choice)
            {
                await Frames(40);
                await Click(blank, MouseButton.Right);
                Check(!ev._event.IsFinished && NOverlayStack.Instance.Peek() == choice, "ancient offer's required card choice not skipped");
                await Click(Center(Descendants(choice).OfType<NCardHolder>().First(h => h.IsVisibleInTree())), MouseButton.Right);
                await Click(blank, MouseButton.Right);
                await Frames(40);
            }
            else await Frames(1);
        }
        await Until(() => ev._event.IsFinished && NOverlayStack.Instance!.ScreenCount == 0, "ancient reward chosen");
        await Frames(40);
        await Screenshot("ancient-complete");
        await Click(blank, MouseButton.Right);
        await Until(() => NMapScreen.Instance!.IsOpen, "ancient right click proceeds");
        Check(true, "finished ancient event blank right click opens map");
        await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<FakeMerchant>(), showTransition: false);
        await Until(() => NEventRoom.Instance?.CustomEventNode is NFakeMerchant && !NMapScreen.Instance!.IsOpen, "fake merchant ready");
        await Frames(40);
        var fake = (NFakeMerchant)NEventRoom.Instance!.CustomEventNode!;
        await Click(Center(fake.MerchantButton), MouseButton.Left);
        await Until(() => fake.Inventory.IsOpen, "fake inventory open");
        await Click(blank, MouseButton.Right);
        Check(!NMapScreen.Instance!.IsOpen, "fake inventory not skipped");
        fake.Inventory.Close();
        await Until(() => !fake.Inventory.IsOpen && fake._proceedButton.IsEnabled, "fake inventory closed");
        await Frames(20);
        await Click(blank, MouseButton.Right);
        await Until(() => NMapScreen.Instance!.IsOpen, "fake merchant proceeds");
        Check(true, "fake merchant blank right click opens map");
        await RunManager.Instance.EnterRoomDebug(RoomType.Event, model: ModelDb.Event<CrystalSphere>(), showTransition: false);
        await Until(() => NEventRoom.Instance?.DefaultFocusedControl?.IsVisibleInTree() == true && !NMapScreen.Instance!.IsOpen, "crystal event ready");
        await Frames(40);
        var sphereOption = Descendants(NEventRoom.Instance!.Layout!).OfType<NEventOptionButton>().OrderBy(b => b.Index).First();
        await Click(Center(sphereOption), MouseButton.Left);
        await Until(() => NOverlayStack.Instance!.Peek() is NCrystalSphereScreen, "crystal minigame open");
        var sphere = (NCrystalSphereScreen)NOverlayStack.Instance!.Peek()!;
        await Frames(40);
        await Click(blank, MouseButton.Right);
        Check(!sphere._entity.IsFinished && !NMapScreen.Instance!.IsOpen, "unfinished crystal minigame not skipped");
        for (int i = 0; i < 8 && !sphere._entity.IsFinished; i++)
        {
            var cell = Descendants(sphere).OfType<NCrystalSphereCell>().First(c => c.Entity.IsHidden);
            int count = sphere._entity.DivinationCount;
            await Click(Center(cell), MouseButton.Left);
            await Until(() => sphere._entity.IsFinished || sphere._entity.DivinationCount < count, "crystal native cell revealed");
            await Frames(40);
        }
        await Until(() => NOverlayStack.Instance!.Peek() is MegaCrit.Sts2.Core.Nodes.Screens.NRewardsScreen, "crystal winnings shown");
        var winnings = (MegaCrit.Sts2.Core.Nodes.Screens.NRewardsScreen)NOverlayStack.Instance!.Peek()!;
        await Click(blank, MouseButton.Right);
        Check(!winnings.IsComplete && NOverlayStack.Instance.Peek() == winnings, "crystal unclaimed winnings not skipped");
        var reward = Descendants(winnings).OfType<MegaCrit.Sts2.Core.Nodes.Rewards.NRewardButton>().First(b => b.IsVisibleInTree() && b.IsEnabled);
        await Click(Center(reward), MouseButton.Left);
        await Until(() => NOverlayStack.Instance!.Peek() == sphere || winnings.IsComplete, "crystal gold collected");
        if (NOverlayStack.Instance!.Peek() != sphere) await Click(blank, MouseButton.Right);
        await Until(() => NOverlayStack.Instance.Peek() == sphere, "crystal winnings closed");
        await Until(() => sphere._entity.IsFinished && sphere._proceedButton.IsEnabled, "crystal minigame finished");
        await Screenshot("crystal-complete");
        await Click(blank, MouseButton.Right);
        await Until(() => NMapScreen.Instance!.IsOpen, "crystal right click proceeds");
        Check(true, "finished crystal minigame blank right click opens map");
    }
}








