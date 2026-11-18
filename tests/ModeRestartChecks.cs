using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using SpireMouse.Timeline;

namespace SpireMouse;

// Game v0.113 modes (Dread, Blitz) carry extra run state: a dominion, a different
// map type and different rooms. Restarting a non-combat room must reload the same
// room and keep the run in its mode.
internal static partial class RuntimeVerification
{
    private static async Task VerifyModeRoomRestarts(string name)
    {
        GameMode mode = name == "Blitz" ? GameMode.Blitz : name == "Dread" ? GameMode.Dread : GameMode.Standard;
        SaveManager.Instance.SetFtuesEnabled(false);
        await NGame.Instance!.StartNewSingleplayerRun(
            MegaCrit.Sts2.Core.Entities.Players.Player.CreateForNewRun(ModelDb.Character<Ironclad>(), MegaCrit.Sts2.Core.Unlocks.UnlockState.all, 1uL), true,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "MODERESTART" + name, mode, false, mode == GameMode.Dread ? 10 : 0);
        var manager = RunManager.Instance;
        manager.ShouldSave = true;
        var failures = new List<string>();
        void Probe(bool ok, string label)
        {
            if (ok) { Results.Add(label); Entry.Log.Info("VERIFY PASS: " + label); }
            else { failures.Add(label); Entry.Log.Info("VERIFY PROBE FAIL: " + label); }
        }
        await Until(() => !NGame.Instance!.Transition.InTransition && manager.DebugOnlyGetState()?.Map != null, "mode run ready");
        var first = manager.DebugOnlyGetState()!;
        string mapType = first.Map!.GetType().Name;
        string? dominion = first.Dominion?.Id.ToString();
        Entry.Log.Info($"MODE {name}: map={mapType} dominion={dominion ?? "none"} points={string.Join(",", first.Map.GetAllMapPoints().Select(p => p.PointType).Distinct())} start={first.Map.StartingMapPoint.PointType}");
        Probe(first.GameMode == mode, $"{name}: run started in the requested mode");

        var kinds = first.Map.GetAllMapPoints().Select(p => p.PointType).Distinct()
            .Where(k => k is MapPointType.Shop or MapPointType.RestSite or MapPointType.Treasure or MapPointType.Unknown).ToArray();
        foreach (var kind in kinds)
        {
            var run = manager.DebugOnlyGetState()!;
            var coord = run.Map!.GetAllMapPoints().First(p => p.PointType == kind).coord;
            await manager.EnterMapCoord(coord);
            await Until(() => !NGame.Instance!.Transition.InTransition, name + " room transition");
            await SaveManager.Instance.SaveRun(null);
            run = manager.DebugOnlyGetState()!;
            int gold = run.Players[0].Gold, hp = run.Players[0].Creature.CurrentHp;
            var roomType = run.CurrentRoom!.RoomType;
            string roomClass = run.CurrentRoom.GetType().Name;
            await PlayerCmd.LoseGold(7, run.Players[0]);
            run.Players[0].Creature.SetCurrentHpInternal(hp - 3);
            await Frames(4);
            Probe(TimelineEngine.HandleQuickRestartKey(), $"{name} {kind}: restart accepted");
            await TimelineEngine.NavigationTask!;
            await Until(() => !RestartPresentation.IsActive, name + " restart reveal finishes");
            var restored = manager.DebugOnlyGetState()!;
            Entry.Log.Info($"MODE {name} {kind}: status='{TimelineEngine.Status}' room={restored.CurrentRoom?.GetType().Name}/{restored.CurrentRoom?.RoomType} was {roomClass}/{roomType} mode={restored.GameMode} map={restored.Map?.GetType().Name} dominion={restored.Dominion?.Id.ToString() ?? "none"} gold={restored.Players[0].Gold}/{gold} hp={restored.Players[0].Creature.CurrentHp}/{hp}");
            Probe(TimelineEngine.Status == "" && !ReferenceEquals(run, restored), $"{name} {kind}: restart completed with a reloaded run");
            Probe(restored.CurrentRoom?.RoomType == roomType && restored.CurrentRoom.GetType().Name == roomClass, $"{name} {kind}: same room reloaded");
            Probe(restored.Players[0].Gold == gold && restored.Players[0].Creature.CurrentHp == hp, $"{name} {kind}: gold and HP from the room-entry save");
            // The game itself reloads a non-Blitz map as SavedActMap (RunManager map loading).
            Probe(restored.GameMode == mode && restored.Map?.GetType().Name == (mode == GameMode.Blitz ? mapType : "SavedActMap"), $"{name} {kind}: mode kept and map reloaded as the game does");
            Probe(restored.Dominion?.Id.ToString() == dominion, $"{name} {kind}: dominion kept");
            Probe(!CombatManager.Instance.IsInProgress && TimelineEngine.Points.Count == 0, $"{name} {kind}: no stale combat history");
            await Frames(30);
            await Screenshot($"mode-{name}-{kind}");
        }
        Probe(kinds.Length > 0, $"{name}: map offered non-combat rooms to restart");
        foreach (var failure in failures) Entry.Log.Info("VERIFY PROBE FAIL (summary): " + failure);
        Check(failures.Count == 0, $"{name} room restart probes: {failures.Count} failed");
    }
}
