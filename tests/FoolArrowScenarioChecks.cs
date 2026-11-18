using System.Text.Json;
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

// Deeper Fool arrow coverage: several slots, several arrows in one enemy, an arrow
// destroyed by Quietus, and an arrow knocked out by the third hit. Every step's
// model and native view are recorded, then every recorded point is revisited
// backwards and forwards and must reproduce both.
internal static partial class RuntimeVerification
{
    private static async Task VerifyFoolScenario(string scenario)
    {
        CardModel[] deck = scenario switch
        {
            "multi" or "spine" => [ModelDb.Card<Quiver>(), ModelDb.Card<FoolishShot>(), ModelDb.Card<FoolishShot>(), ModelDb.Card<Recall>(), ModelDb.Card<StrikeFool>()],
            "quietus" => [ModelDb.Card<Quietus>(), ModelDb.Card<FoolishShot>(), ModelDb.Card<StrikeFool>(), ModelDb.Card<DefendFool>(), ModelDb.Card<DefendFool>()],
            _ => [ModelDb.Card<FoolishShot>(), ModelDb.Card<StrikeFool>(), ModelDb.Card<StrikeFool>(), ModelDb.Card<StrikeFool>(), ModelDb.Card<StrikeFool>()],
        };
        SaveManager.Instance.SetFtuesEnabled(false);
        var run = await NGame.Instance!.StartNewSingleplayerRun(
            MegaCrit.Sts2.Core.Entities.Players.Player.CreateForNewRun(ModelDb.Character<Fool>(), SaveManager.Instance.GenerateUnlockStateFromProgress(), 1uL), true,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "FOOL" + scenario.ToUpperInvariant(), GameMode.Standard, false);
        var player = run.Players[0];
        await CardPileCmd.RemoveFromDeck(player.Deck.Cards.ToArray(), false);
        foreach (var model in deck) await CardPileCmd.Add(run.CreateCard(model, player), PileType.Deck, skipVisuals: true);
        EncounterModel encounter = ModelDb.Encounter<CultistsNormal>();
        if (scenario == "spine")
        {
            // Find an enemy whose skeleton has the dedicated arrow slots, where several arrows fan out.
            EncounterModel? found = null;
            foreach (var candidate in ModelDb.AllEncounters.Where(e => e.RoomType == RoomType.Monster).Take(40))
            {
                await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: candidate.ToMutable(), showTransition: false);
                await Until(() => NCombatRoom.Instance != null && NCombatRoom.Instance.CreatureNodes.Any(n => n.Entity.IsEnemy), "scan room " + candidate.Id.Entry);
                await Frames(20);
                bool spine = NCombatRoom.Instance!.CreatureNodes.Any(n => n.Entity.IsEnemy && n.HasSpineArrowSlots);
                Entry.Log.Info($"FOOL[spine] scan {candidate.Id.Entry}: {spine}");
                if (spine) { found = candidate; break; }
            }
            if (found == null) throw new InvalidOperationException("No scanned encounter has spine arrow slots.");
            encounter = found;
        }
        TimelineEngine.DiagnosticRoomLoader = async () => await RunManager.Instance.EnterRoomDebug(RoomType.Monster, model: encounter.ToMutable(), showTransition: false);
        await TimelineEngine.DiagnosticRoomLoader();
        // Keep the fixture alive and the target standing through the scripted turns.
        player.Creature.SetMaxHpInternal(10000); player.Creature.SetCurrentHpInternal(10000);
        Creature enemy = NCombatRoom.Instance!.CreatureNodes.OrderByDescending(n => n.HasSpineArrowSlots).First(n => n.Entity.IsEnemy).Entity;
        enemy.SetMaxHpInternal(10000); enemy.SetCurrentHpInternal(10000);
        TimelineEngine.Capture(RunManager.Instance.CombatReplayWriter._replay!);
        await Until(() => TimelineEngine.Settled && TimelineEngine.Points.Count == 1, "fool scenario checkpoint");

        var failures = new List<string>();
        void Probe(bool ok, string name)
        {
            if (ok) { Results.Add(name); Entry.Log.Info("VERIFY PASS: " + name); }
            else { failures.Add(name); Entry.Log.Info("VERIFY PROBE FAIL: " + name); }
        }
        var combat = player.PlayerCombatState!;
        Creature[] Enemies() => CombatManager.Instance.DebugOnlyGetState()!.Creatures.Where(c => c.IsEnemy).ToArray();
        NImpaledArrowVfx[] Stuck(Creature creature) => NCombatRoom.Instance!.GetCreatureNode(creature) is { } node
            ? Descendants(node).OfType<NImpaledArrowVfx>().Where(v => !v.IsQueuedForDeletion() && v._retrieveTween == null).ToArray() : [];
        NFoolArrowUi[] Bar() => Descendants(NCombatRoom.Instance!).OfType<NFoolArrowUi>().Where(u => !u.IsQueuedForDeletion()).ToArray();
        // The model: every slot's status in order, and which slot indices each enemy's Impaled power holds.
        string Model() => JsonSerializer.Serialize(new
        {
            slots = combat._arrows.Select(a => new { status = a.Status.ToString(), a.DestroyAfterImpaled }),
            impaled = Enemies().Select(e => e.GetPowerInstances<ImpaledPower>().SelectMany(p => p.Arrows).Select(a => combat._arrows.IndexOf(a))),
        });
        // The view, as the game itself would draw this model.
        string View() => JsonSerializer.Serialize(new
        {
            bar = Bar().Select(u => new { slot = combat._arrows.IndexOf(u.ArrowSlot), u.Visible, shown = u.Visible && u._visual.Modulate.A > 0.9f, hidden = u.Visible && u._visual.Modulate.A < 0.1f }).OrderBy(u => u.slot),
            stuck = Enemies().Select(e => Stuck(e).Select(v => combat._arrows.IndexOf(v._arrowSlot!)).Order()),
            fan = Enemies().Select(e => Stuck(e).Select(v => MathF.Round(v._frontArrowVisual.RotationDegrees, 1) + 0f).Order() /* + 0f folds -0 into 0 */),
        });
        bool ViewAgreesWithModel()
        {
            var bar = Bar();
            if (bar.Length != combat._arrows.Count) return false;
            foreach (var ui in bar)
            {
                var status = ui.ArrowSlot.Status;
                if (!combat._arrows.Contains(ui.ArrowSlot) || ui.Visible != (status != ArrowStatus.Destroyed)) return false;
                if (status == ArrowStatus.Retrieved && ui._visual.Modulate.A < 0.9f) return false;
                if (status == ArrowStatus.Away && ui._visual.Modulate.A > 0.1f) return false;
            }
            return Enemies().All(e => Stuck(e).Select(v => v._arrowSlot).ToHashSet()
                .SetEquals(e.GetPowerInstances<ImpaledPower>().SelectMany(p => p.Arrows)));
        }

        var points = new List<(string Label, int Cursor, string State, string Model, string View)>();
        async Task Record(string label)
        {
            await Frames(120);
            var point = (label, TimelineEngine.Cursor, State(), Model(), View());
            points.Add(point);
            Entry.Log.Info($"FOOL[{scenario}] {label}: cursor={point.Item2} model={point.Item4} view={point.Item5}");
            Probe(ViewAgreesWithModel(), $"{label}: native view agrees with model");
        }
        async Task Play<T>(string label, bool targeted) where T : CardModel
        {
            var holder = NPlayerHand.Instance!.ActiveHolders.First(h => h.CardModel is T);
            int before = TimelineEngine.Cursor;
            await Click(Center(holder.Hitbox), MouseButton.Right);
            if (targeted && MouseInput.PendingHolder != null)
                await Click(Center(NCombatRoom.Instance!.GetCreatureNode(enemy)!.Hitbox), MouseButton.Right);
            await Until(() => TimelineEngine.ChoiceVisible || (TimelineEngine.Settled && TimelineEngine.Cursor > before), label + " resolves");
            Probe(!TimelineEngine.ChoiceVisible, label + ": no unexpected choice screen");
            await Record(label);
        }
        async Task EndTurn(string label)
        {
            int round = CombatManager.Instance.DebugOnlyGetState()!.RoundNumber, before = TimelineEngine.Cursor;
            await EndStressTurn();
            await Until(() => TimelineEngine.Settled && CombatManager.Instance.DebugOnlyGetState()!.RoundNumber > round && TimelineEngine.Cursor > before, label + " checkpoint");
            await Record(label);
        }

        await Record("initial");
        switch (scenario)
        {
            case "multi" or "spine":
                await Play<Quiver>("quiver", false);
                Probe(combat._arrows.Count == 2 && Bar().Length == 2, "quiver: two slots and two bar arrows");
                await Play<FoolishShot>("first shot", true);
                await Play<FoolishShot>("second shot", true);
                Probe(enemy.GetPowerInstances<ImpaledPower>().Sum(p => p.Arrows.Count) == 2 && Stuck(enemy).Length == 2, "two shots: two arrows impaled and drawn");
                Entry.Log.Info($"FOOL[multi] spine arrow slots={NCombatRoom.Instance!.GetCreatureNode(enemy)!.HasSpineArrowSlots}");
                await EndTurn("turn two");
                await Play<Recall>("recall", true);
                Probe(combat._arrows.All(a => a.Status == ArrowStatus.Retrieved) && !enemy.HasPower<ImpaledPower>() && Stuck(enemy).Length == 0, "recall: both arrows retrieved, none drawn");
                break;
            case "quietus":
                await Play<Quietus>("quietus", false);
                await Play<FoolishShot>("destroying shot", true);
                Probe(combat._arrows[0].Status == ArrowStatus.Destroyed && Bar().All(u => !u.Visible) && Stuck(enemy).Length == 0, "quietus shot: arrow destroyed, bar hidden, none drawn");
                await EndTurn("turn two");
                break;
            default:
                await Play<FoolishShot>("shot", true);
                await Play<StrikeFool>("hit one", true);
                await Play<StrikeFool>("hit two", true);
                Probe(combat._arrows[0].Status == ArrowStatus.Away && Stuck(enemy).Length == 1, "two hits: arrow still stuck");
                await EndTurn("turn two");
                await Play<StrikeFool>("hit three", true);
                Probe(combat._arrows[0].Status == ArrowStatus.Retrieved && !enemy.HasPower<ImpaledPower>() && Stuck(enemy).Length == 0, "third hit: arrow knocked out and retrieved");
                break;
        }
        await Screenshot($"fool-{scenario}-played");

        async Task Visit(int index, string direction)
        {
            var point = points[index];
            if (TimelineEngine.Cursor != point.Cursor)
            {
                Probe(TimelineEngine.Navigate(point.Cursor), $"{direction} to '{point.Label}' accepted");
                await TimelineEngine.NavigationTask!;
            }
            await Frames(120);
            string model = Model(), view = View();
            if (model != point.Model || view != point.View) Entry.Log.Info($"FOOL[{scenario}] {direction} '{point.Label}' got model={model} view={view}");
            Probe(TimelineEngine.Status == "" && State() == point.State, $"{direction} to '{point.Label}': combat fingerprint");
            Probe(model == point.Model, $"{direction} to '{point.Label}': arrow model");
            Probe(view == point.View, $"{direction} to '{point.Label}': arrow bar and stuck arrows as natively drawn");
            Probe(ViewAgreesWithModel(), $"{direction} to '{point.Label}': view agrees with model");
        }
        for (int i = points.Count - 2; i >= 0; i--) await Visit(i, "undo");
        await Screenshot($"fool-{scenario}-rewound");
        for (int i = 1; i < points.Count; i++) await Visit(i, "redo");
        // Long jumps cross several arrow changes at once.
        await Visit(0, "jump back"); await Visit(points.Count - 1, "jump forward");
        await Screenshot($"fool-{scenario}-final");

        foreach (var failure in failures) Entry.Log.Info("VERIFY PROBE FAIL (summary): " + failure);
        Check(failures.Count == 0, $"Fool {scenario} probes: {failures.Count} failed");
    }
}
