using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using SpireMouse.Timeline;

namespace SpireMouse;

internal static partial class RuntimeVerification
{
    private static async Task VerifyFoolMelodies()
    {
        SaveManager.Instance.SetFtuesEnabled(false);
        var run = await NGame.Instance!.StartNewSingleplayerRun(
            MegaCrit.Sts2.Core.Entities.Players.Player.CreateForNewRun(ModelDb.Character<Fool>(), SaveManager.Instance.GenerateUnlockStateFromProgress(), 1uL), true,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "FOOLMELODY", GameMode.Standard, false);
        var player = run.Players[0];
        player.Creature.SetMaxHpInternal(1000); player.Creature.SetCurrentHpInternal(1000);
        await CardPileCmd.RemoveFromDeck(player.Deck.Cards.ToArray(), false);
        foreach (var model in new CardModel[] { ModelDb.Card<WahChoKi>(), ModelDb.Card<StrikeFool>(),
            ModelDb.Card<DefendFool>(), ModelDb.Card<DefendFool>(), ModelDb.Card<DefendFool>() })
            await CardPileCmd.Add(run.CreateCard(model, player), PileType.Deck, skipVisuals: true);
        await RunManager.Instance.EnterMapCoord(run.Map!.GetAllMapPoints().First(p => p.PointType == MapPointType.Monster).coord);
        await Until(() => TimelineEngine.Settled && TimelineEngine.Points.Count == 1, "melody native opening");
        await CheckMelodyDisplay(false, "initial");
        var holder = NPlayerHand.Instance!.ActiveHolders.First(h => h.CardModel is WahChoKi);
        await Click(Center(holder.Hitbox), MouseButton.Right);
        await Click(Center(NCombatRoom.Instance!.CreatureNodes.First(n => n.Entity.IsEnemy).Hitbox), MouseButton.Right);
        await Until(() => TimelineEngine.Settled && TimelineEngine.Points.Count == 2, "three melodies recorded");
        string played = State();
        await CheckMelodyDisplay(true, "played Wah Cho Ki");
        await Screenshot("melodies-played");
        Check(TimelineEngine.HandleUndoKey(), "melody ordinary undo accepted"); await TimelineEngine.NavigationTask!;
        await CheckMelodyDisplay(false, "ordinary undo");
        Check(TimelineEngine.HandleRedoKey(), "melody ordinary redo accepted"); await TimelineEngine.NavigationTask!;
        await CheckMelodyDisplay(true, "ordinary redo");
        await RestartHistoryRestart();
        await CheckMelodyDisplay(false, "restart");
        await RestartHistoryNavigate(false, played, "melody undo restart");
        await CheckMelodyDisplay(true, "undo restart");
        await Screenshot("melodies-undo-restart");
        Check(TimelineEngine.HandleRedoKey(), "melody redo restart accepted"); await TimelineEngine.NavigationTask!;
        await CheckMelodyDisplay(false, "redo restart");
        await RestartHistoryNavigate(false, played, "melody second undo restart");
        await EndStressTurn();
        await Until(() => TimelineEngine.Settled && CombatManager.Instance.DebugOnlyGetState()!.RoundNumber == 2
            && TimelineEngine.Points[TimelineEngine.Cursor].State!.SequenceEqual(TimelineEngine.Fingerprint()), "melody turn two");
        await CheckMelodyDisplay(false, "new turn clears melodies");
        Check(TimelineEngine.HandleUndoKey(), "melody turn undo accepted"); await TimelineEngine.NavigationTask!;
        await CheckMelodyDisplay(true, "undo turn restores melodies");

        // Both calls complete without yielding: exercise deferred node deletion.
        Check(TimelineEngine.HandleUndoKey(), "rapid melody undo accepted"); await TimelineEngine.NavigationTask!;
        Check(TimelineEngine.HandleRedoKey(), "rapid melody redo accepted"); await TimelineEngine.NavigationTask!;
        await CheckMelodyDisplay(true, "same-frame undo redo");
        await Screenshot("melodies-final");
    }

    private static async Task CheckMelodyDisplay(bool played, string label)
    {
        await Frames(30);
        var player = RunManager.Instance.DebugOnlyGetState()!.Players[0];
        CardTag[] expected = played ? [CardTag.Wah, CardTag.Cho, CardTag.Ki] : [];
        CardTag[] model = new[] { CardTag.Wah, CardTag.Cho, CardTag.Ki }
            .Where(tag => MelodyCmd.PlayedThisTurn(player, tag)).ToArray();
        var symbols = Descendants(NCombatRoom.Instance!).OfType<NFoolCombatSymbol>()
            .Where(symbol => !symbol.IsQueuedForDeletion() && symbol.IsVisibleInTree()).ToArray();
        Check(model.Order().SequenceEqual(expected.Order()), label + ": actual melody effects match expected tags");
        Check(symbols.Select(s => s.symbolType).Order().SequenceEqual(expected.Order()), label + ": visible symbols match actual Wah Cho Ki without duplicates");
        Check(symbols.All(s => s._symbol.Texture != null && s._symbol.Modulate.A > .9f
            && s._symbol.Scale.DistanceTo(Vector2.One) < .01f), label + ": melody icons are opaque and fully drawn");
        Check(symbols.All(s => s._symbol.Texture.ResourcePath.Contains(s.symbolType.ToString().ToLowerInvariant())),
            label + ": icon textures match melody type");
    }
}
