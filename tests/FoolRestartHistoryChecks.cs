using Godot;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using SpireMouse.Timeline;

namespace SpireMouse;

internal static partial class RuntimeVerification
{
    private static async Task VerifyFoolRestartHistory()
    {
        SaveManager.Instance.SetFtuesEnabled(false);
        var run = await NGame.Instance!.StartNewSingleplayerRun(
            MegaCrit.Sts2.Core.Entities.Players.Player.CreateForNewRun(ModelDb.Character<Fool>(), SaveManager.Instance.GenerateUnlockStateFromProgress(), 1uL), true,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "RESTARTFOOL", GameMode.Standard, false);
        var player = run.Players[0];
        await CardPileCmd.RemoveFromDeck(player.Deck.Cards.ToArray(), false);
        foreach (var model in new CardModel[] { ModelDb.Card<FoolishShot>(), ModelDb.Card<StrikeFool>(), ModelDb.Card<StrikeFool>(),
            ModelDb.Card<DefendFool>(), ModelDb.Card<DefendFool>() })
            await CardPileCmd.Add(run.CreateCard(model, player), PileType.Deck, skipVisuals: true);
        await RunManager.Instance.EnterMapCoord(run.Map!.GetAllMapPoints().First(p => p.PointType == MapPointType.Monster).coord);
        await Until(() => TimelineEngine.Settled && TimelineEngine.Points.Count == 1, "Fool restart opening");
        string initial = State();
        var holder = NPlayerHand.Instance!.ActiveHolders.First(h => h.CardModel is FoolishShot);
        await Click(Center(holder.Hitbox), MouseButton.Right);
        await Click(Center(NCombatRoom.Instance!.CreatureNodes.First(n => n.Entity.IsEnemy).Hitbox), MouseButton.Right);
        await Until(() => TimelineEngine.Settled && TimelineEngine.Points.Count == 2, "Fool arrow shot checkpoint");
        string shot = State();
        void CheckArrows(bool impaled, string label)
        {
            var combat = RunManager.Instance.DebugOnlyGetState()!.Players[0].PlayerCombatState!;
            var room = NCombatRoom.Instance!;
            int stuck = room.CreatureNodes.SelectMany(n => Descendants(n)).OfType<NImpaledArrowVfx>()
                .Count(v => !v.IsQueuedForDeletion() && v._retrieveTween == null);
            int modelStuck = room.CreatureNodes.Sum(n => n.Entity.GetPowerInstances<ImpaledPower>().Sum(p => p.Arrows.Count));
            Check(combat.AvailableArrowCount == (impaled ? 0 : 1) && modelStuck == (impaled ? 1 : 0)
                && stuck == modelStuck, label);
        }
        CheckArrows(true, "Fool shot model and stuck-arrow view agree");
        await RestartHistoryRestart();
        Check(State() == initial, "Fool restart reproduces opening");
        CheckArrows(false, "Fool restart retrieves arrow and clears stuck visual");
        for (int i = 0; i < 2; i++)
        {
            await RestartHistoryNavigate(false, shot, "Fool undo restart restores shot");
            CheckArrows(true, "Fool undo restart reconstructs arrow model and visual");
            await RestartHistoryNavigate(true, initial, "Fool redo restart restores quiver");
            CheckArrows(false, "Fool redo restart clears stuck visual");
        }
        await Screenshot("restart-fool");
    }
}
