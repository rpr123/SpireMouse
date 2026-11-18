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
    private static async Task VerifyRestartHistory()
    {
        SaveManager.Instance.SetFtuesEnabled(false);
        SettingsStore.Current.HistoryLimit = 100;
        SettingsStore.Current.IncludeChoiceHistory = true;
        var run = await NGame.Instance!.StartNewSingleplayerRun(
            MegaCrit.Sts2.Core.Entities.Players.Player.CreateForNewRun(ModelDb.Character<Ironclad>(), SaveManager.Instance.GenerateUnlockStateFromProgress(), 1uL), true,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "RESTARTUNDO", GameMode.Standard, false);
        var player = run.Players[0];
        player.Creature.SetMaxHpInternal(1000); player.Creature.SetCurrentHpInternal(1000);
        await CardPileCmd.RemoveFromDeck(player.Deck.Cards.ToArray(), false);
        foreach (var model in new CardModel[] { ModelDb.Card<StrikeIronclad>(), ModelDb.Card<StrikeIronclad>(),
            ModelDb.Card<DefendIronclad>(), ModelDb.Card<DefendIronclad>(), ModelDb.Card<TrueGrit>() })
        {
            var card = run.CreateCard(model, player);
            if (card is TrueGrit) card.UpgradeInternal();
            await CardPileCmd.Add(card, PileType.Deck, skipVisuals: true);
        }
        await RunManager.Instance.EnterMapCoord(run.Map!.GetAllMapPoints().First(p => p.PointType == MapPointType.Monster).coord);
        await Until(() => TimelineEngine.Settled && TimelineEngine.Points.Count == 1, "restart history native opening");
        string initial = State();
        await RestartHistoryPlayDefend();
        int actionIndex = TimelineEngine.Cursor;
        string afterDefend = State();
        var oldRoom = NCombatRoom.Instance;
        await RestartHistoryRestart();
        Check(NCombatRoom.Instance != oldRoom && State() == initial, "restart retains native room reload and exact initial state");
        Check(TimelineEngine.Cursor == actionIndex + 1 && TimelineEngine.Points[^1].Label == "전투 재시작",
            "restart appends one navigable checkpoint instead of clearing history");
        await RestartHistoryNavigate(false, afterDefend, "undo restart restores previous action");
        await RestartHistoryNavigate(true, initial, "redo restart restores initial state");
        await RestartHistoryNavigate(false, afterDefend, "restart can be undone repeatedly");
        await RestartHistoryNavigate(false, initial, "undo continues through history before restart");
        await RestartHistoryNavigate(true, afterDefend, "redo continues through earlier actions");
        await RestartHistoryNavigate(true, initial, "redo crosses restart again");

        // Include an enemy turn/RNG advance in the state across the next boundary.
        await EndStressTurn();
        await Until(() => TimelineEngine.Settled && CombatManager.Instance.DebugOnlyGetState()!.RoundNumber == 2
            && TimelineEngine.Points[TimelineEngine.Cursor].State!.SequenceEqual(TimelineEngine.Fingerprint()), "second turn checkpoint");
        string turnTwo = State();
        await RestartHistoryRestart();
        await RestartHistoryNavigate(false, turnTwo, "undo second restart preserves HP powers cards turn and RNG");
        int round = CombatManager.Instance.DebugOnlyGetState()!.RoundNumber;
        await EndStressTurn();
        await Until(() => TimelineEngine.Settled && CombatManager.Instance.DebugOnlyGetState()!.RoundNumber > round
            && TimelineEngine.Cursor == TimelineEngine.Points.Count - 1, "new branch after undo restart");
        Check(!TimelineEngine.HandleRedoKey(), "new action after undo discards restart redo branch");

        // A choice is a checkpoint even though its game action is still awaiting input.
        await RestartHistoryRestart();
        var grit = NPlayerHand.Instance!.ActiveHolders.First(h => h.CardModel is TrueGrit);
        await Click(Center(grit.Hitbox), MouseButton.Right);
        await Until(() => TimelineEngine.ChoiceVisible && TimelineEngine.Points[^1].Choice, "restart before pending choice");
        string pending = State();
        await RestartHistoryRestart();
        await RestartHistoryNavigate(false, pending, "undo restart recreates pending card selection");
        Check(TimelineEngine.ChoiceVisible, "restored pending selection is interactive");
        await RestartHistoryNavigate(true, initial, "redo restart cancels pending selection cleanly");
        await RestartHistoryNavigate(false, pending, "pending selection survives repeated restart navigation");
        var hand = NPlayerHand.Instance!;
        await Click(Center(hand.ActiveHolders.First().Hitbox), MouseButton.Right);
        await Click(new Vector2(50, 450), MouseButton.Right);
        await Until(() => TimelineEngine.Settled && !TimelineEngine.ChoiceVisible, "restored choice resolves");
        Check(!TimelineEngine.HandleRedoKey(), "resolving restored selection creates a new branch");
        await RestartHistoryRestart();
        int recoveryCursor = TimelineEngine.Cursor;
        string recoveryState = State();
        TimelineEngine.BeforeHistoryRebuildValidation = () =>
        {
            TimelineEngine.BeforeHistoryRebuildValidation = null;
            throw new InvalidOperationException("Injected cross-restart validation failure");
        };
        Check(TimelineEngine.HandleUndoKey(), "fault-injected undo accepted");
        await TimelineEngine.NavigationTask!;
        Check(State() == recoveryState && TimelineEngine.Cursor == recoveryCursor
            && TimelineEngine.Status.Contains("복구했습니다"), "failed cross-restart restore rolls back without moving cursor");
        Check(TimelineEngine.HandleUndoKey(), "navigation remains available after successful recovery");
        await TimelineEngine.NavigationTask!;
        Check(TimelineEngine.Status == "", "retry after validation failure succeeds");
        SettingsStore.Current.HistoryLimit = 3;
        for (int i = 0; i < 4; i++) await RestartHistoryRestart();
        Check(TimelineEngine.Points.Count == 3, "repeated restarts obey history capacity");
        await RestartHistoryNavigate(false, initial, "undo reaches retained restart after trimming");
        await RestartHistoryNavigate(false, initial, "undo reaches oldest retained restart");
        Check(!TimelineEngine.HandleUndoKey(), "trimmed restart history refuses out-of-range undo");
        await RestartHistoryNavigate(true, initial, "redo retained restart after trimming");
        await RestartHistoryNavigate(true, initial, "redo newest restart after trimming");
        SettingsStore.Current.HistoryLimit = 100;
        SettingsStore.Current.ShowHistory = true;
        await Frames(3);
        var panel = NGame.Instance!.GetNode<VBoxContainer>("SpireMouseHistory");
        await Click(Center(panel.GetChild<Button>(0)), MouseButton.Left);
        var rows = panel.GetChild<ScrollContainer>(2).GetChild<VBoxContainer>(0);
        Check(rows.GetChildren().OfType<Button>().Any(b => b.Text.Contains("전투 재시작")), "history panel exposes restart checkpoints");
        await Click(Center(rows.GetChildren().OfType<Button>().Last()), MouseButton.Left);
        await TimelineEngine.NavigationTask!;
        Check(TimelineEngine.Cursor == 0 && TimelineEngine.Status == "" && State() == initial,
            "history panel jumps across restart boundary");
        await Screenshot("restart-history-restored");
    }

    private static async Task RestartHistoryPlayDefend()
    {
        int count = TimelineEngine.Points.Count;
        var holder = NPlayerHand.Instance!.ActiveHolders.First(h => h.CardModel is DefendIronclad);
        await Click(Center(holder.Hitbox), MouseButton.Right);
        await Until(() => TimelineEngine.Settled && TimelineEngine.Points.Count > count, "defend action recorded");
    }

    private static async Task RestartHistoryRestart()
    {
        Check(TimelineEngine.HandleQuickRestartKey(), "combat restart accepted");
        Check(!TimelineEngine.HandleQuickRestartKey() && !TimelineEngine.HandleUndoKey(), "navigation is locked during restart");
        await TimelineEngine.NavigationTask!;
        await Until(() => TimelineEngine.Settled && !RestartPresentation.IsActive, "restart history ready");
        Check(TimelineEngine.Status == "", "restart completed without divergence");
    }

    private static async Task RestartHistoryNavigate(bool redo, string expected, string label)
    {
        Check(redo ? TimelineEngine.HandleRedoKey() : TimelineEngine.HandleUndoKey(), label + " accepted");
        await TimelineEngine.NavigationTask!;
        await Frames(5);
        Check(TimelineEngine.Status == "" && State() == expected, label);
        Check(CombatManager.Instance.DebugOnlyCurrentTurnLoopTask?.IsFaulted == false, label + " keeps turn loop alive");
        Check(NCombatRoom.Instance!.Ui.EndTurnButton.IsEnabled == !TimelineEngine.ChoiceVisible,
            label + " keeps end-turn UI consistent");
    }
}
