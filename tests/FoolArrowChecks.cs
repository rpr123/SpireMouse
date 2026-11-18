using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.FoolArrows;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Vfx;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using SpireMouse.Timeline;

namespace SpireMouse;

// The Fool (game v0.113): an arrow slot is shared by the player's combat state and
// the target's ImpaledPower, and is shown both on the arrow bar and stuck in the
// creature. Every probe is evaluated and logged; the suite fails at the end if any
// probe failed, so one run reports the whole picture.
internal static partial class RuntimeVerification
{
    private static async Task VerifyFoolArrows()
    {
        SaveManager.Instance.SetFtuesEnabled(false);
        var run = await NGame.Instance!.StartNewSingleplayerRun(
            MegaCrit.Sts2.Core.Entities.Players.Player.CreateForNewRun(ModelDb.Character<Fool>(), SaveManager.Instance.GenerateUnlockStateFromProgress(), 1uL), true,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "FOOLARROWS", GameMode.Standard, false);
        var player = run.Players[0];
        await CardPileCmd.RemoveFromDeck(player.Deck.Cards.ToArray(), false);
        foreach (var model in new CardModel[] { ModelDb.Card<FoolishShot>(), ModelDb.Card<StrikeFool>(), ModelDb.Card<StrikeFool>(), ModelDb.Card<DefendFool>(), ModelDb.Card<DefendFool>() })
            await CardPileCmd.Add(run.CreateCard(model, player), PileType.Deck, skipVisuals: true);
        TimelineEngine.DiagnosticRoomLoader = async () => await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: ModelDb.Encounter<CultistsNormal>().ToMutable(), showTransition: false);
        await TimelineEngine.DiagnosticRoomLoader();
        await Until(() => TimelineEngine.Settled && TimelineEngine.Points.Count == 1, "fool initial checkpoint");

        var failures = new List<string>();
        void Probe(bool ok, string name)
        {
            if (ok) { Results.Add(name); Entry.Log.Info("VERIFY PASS: " + name); }
            else { failures.Add(name); Entry.Log.Info("VERIFY PROBE FAIL: " + name); }
        }

        var slot = player.PlayerCombatState!._arrows[0];
        Creature enemy = NCombatRoom.Instance!.CreatureNodes.First(n => n.Entity.IsEnemy).Entity;
        ImpaledPower? Impaled() => enemy.GetPowerInstances<ImpaledPower>().FirstOrDefault();
        int StuckArrows() => NCombatRoom.Instance!.GetCreatureNode(enemy) is { } node
            ? Descendants(node).OfType<NImpaledArrowVfx>().Count(v => !v.IsQueuedForDeletion() && v._retrieveTween == null) : -1;
        NFoolArrowUi[] Bar() => Descendants(NCombatRoom.Instance!).OfType<NFoolArrowUi>().Where(u => !u.IsQueuedForDeletion()).ToArray();
        bool BarShowsArrow() => Bar() is { Length: 1 } bar && bar[0].ArrowSlot == slot && bar[0].Visible && bar[0]._visual.Modulate.A > 0.9f;
        bool BarShowsEmpty() => Bar() is { Length: 1 } bar && bar[0].ArrowSlot == slot && bar[0]._visual.Modulate.A < 0.1f;

        async Task ProbeHolding(string label)
        {
            await Frames(90);
            Entry.Log.Info($"FOOL {label}: status={slot.Status} impaled={Impaled()?.Arrows.Count ?? -1} stuck={StuckArrows()} bar={Bar().Length} barAlpha={(Bar().FirstOrDefault()?._visual.Modulate.A ?? -1f)}");
            Probe(slot.Status == ArrowStatus.Retrieved && player.PlayerCombatState!.AvailableArrowCount == 1, label + ": arrow slot is back in the quiver");
            Probe(Impaled() == null, label + ": enemy has no Impaled power");
            Probe(StuckArrows() == 0, label + ": no arrow is drawn stuck in the enemy");
            Probe(BarShowsArrow(), label + ": arrow bar shows the arrow as available");
        }
        async Task ProbeShot(string label)
        {
            await Frames(90);
            Entry.Log.Info($"FOOL {label}: status={slot.Status} impaled={Impaled()?.Arrows.Count ?? -1} stuck={StuckArrows()} bar={Bar().Length} barAlpha={(Bar().FirstOrDefault()?._visual.Modulate.A ?? -1f)}");
            Probe(slot.Status == ArrowStatus.Away && player.PlayerCombatState!.AvailableArrowCount == 0, label + ": arrow slot is away");
            Probe(Impaled() is { } power && power.Arrows.Count == 1 && ReferenceEquals(power.Arrows[0], slot), label + ": Impaled power holds the player's own slot object");
            Probe(StuckArrows() == 1, label + ": one arrow is drawn stuck in the enemy");
            Probe(BarShowsEmpty(), label + ": arrow bar shows the arrow as shot");
        }
        async Task Shoot(string label)
        {
            var holder = NPlayerHand.Instance!.ActiveHolders.First(h => h.CardModel is FoolishShot);
            int before = TimelineEngine.Cursor;
            await Click(Center(holder.Hitbox), MouseButton.Right);
            if (MouseInput.PendingHolder != null)
                await Click(Center(NCombatRoom.Instance!.GetCreatureNode(enemy)!.Hitbox), MouseButton.Right);
            await Until(() => TimelineEngine.Settled && TimelineEngine.Cursor > before, label);
        }

        string initial = State();
        await ProbeHolding("initial");
        await Screenshot("fool-initial");

        await Shoot("fool shot resolves");
        string after = State(); int latest = TimelineEngine.Cursor;
        await ProbeShot("after shot");
        await Screenshot("fool-shot");

        for (int repeat = 0; repeat < 2; repeat++)
        {
            Probe(TimelineEngine.Navigate(0), $"undo {repeat} accepted"); await TimelineEngine.NavigationTask!;
            Probe(TimelineEngine.Status == "" && State() == initial, $"undo {repeat}: model fingerprint equals initial");
            await ProbeHolding($"undo {repeat}");
            if (repeat == 0) await Screenshot("fool-undo");
            Probe(TimelineEngine.Navigate(latest), $"redo {repeat} accepted"); await TimelineEngine.NavigationTask!;
            Probe(TimelineEngine.Status == "" && State() == after, $"redo {repeat}: model fingerprint equals post-shot");
            await ProbeShot($"redo {repeat}");
            if (repeat == 0) await Screenshot("fool-redo");
        }

        // A fresh shot after undo must behave like the first one.
        TimelineEngine.Navigate(0); await TimelineEngine.NavigationTask!;
        await Shoot("fool shot after undo resolves");
        Probe(State() == after, "shot after undo: same model result as the first shot");
        await ProbeShot("shot after undo");
        latest = TimelineEngine.Cursor;

        // The arrow stays in the enemy across the enemy turn; undoing the turn must keep it there.
        int round = CombatManager.Instance.DebugOnlyGetState()!.RoundNumber;
        await EndStressTurn();
        await Until(() => TimelineEngine.Settled && CombatManager.Instance.DebugOnlyGetState()!.RoundNumber > round && TimelineEngine.Cursor > latest, "fool next turn checkpoint");
        Probe(TimelineEngine.Navigate(latest), "turn undo accepted"); await TimelineEngine.NavigationTask!;
        Probe(State() == after, "turn undo: model fingerprint equals post-shot");
        await ProbeShot("turn undo");
        await Screenshot("fool-final");

        foreach (var failure in failures) Entry.Log.Info("VERIFY PROBE FAIL (summary): " + failure);
        Check(failures.Count == 0, $"Fool arrow probes: {failures.Count} failed");
    }
}
