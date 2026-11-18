using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using SpireMouse.Timeline;
using STS2QuickAnimationMode.Utils;

namespace SpireMouse;

internal static partial class RuntimeVerification
{
    internal static async Task AfterMenu(Task startup)
    {
        await startup;
        if (_ran || DirectoryPath == null) return;
        _ran = true;
        try
        {
            await Frames(20);
            // Verification-only acceleration. The native intro suite explicitly uses 1x.
            SettingsStore.Current.Speed.SpeedMultiplier = 10f;
            SettingsStore.Current.Speed.AccelerationMode = SpeedAccelerationMode.AlwaysOn;
            SpeedManager.OnSettingsChanged();
            if (System.Environment.GetEnvironmentVariable("SPIRE_MOUSE_VERIFY_SUITE") == "fool-melody")
            {
                await VerifyFoolMelodies();
                File.WriteAllText(Path.Combine(DirectoryPath, "results.json"), JsonSerializer.Serialize(new { status = "passed", suite = "fool-melody", checks = Results }));
                return;
            }
            if (System.Environment.GetEnvironmentVariable("SPIRE_MOUSE_VERIFY_SUITE") == "restart-history")
            {
                await VerifyRestartHistory();
                File.WriteAllText(Path.Combine(DirectoryPath, "results.json"), JsonSerializer.Serialize(new { status = "passed", suite = "restart-history", checks = Results }));
                return;
            }
            if (System.Environment.GetEnvironmentVariable("SPIRE_MOUSE_VERIFY_SUITE") == "restart-fool")
            {
                await VerifyFoolRestartHistory();
                File.WriteAllText(Path.Combine(DirectoryPath, "results.json"), JsonSerializer.Serialize(new { status = "passed", suite = "restart-fool", checks = Results }));
                return;
            }
            if (System.Environment.GetEnvironmentVariable("SPIRE_MOUSE_VERIFY_SUITE") == "menu")
            {
                await VerifyModMenu();
                File.WriteAllText(Path.Combine(DirectoryPath, "results.json"), JsonSerializer.Serialize(new { status = "passed", suite = "menu", checks = Results }));
                return;
            }
            if (System.Environment.GetEnvironmentVariable("SPIRE_MOUSE_VERIFY_SUITE") is { } matrix && matrix.StartsWith("matrix-"))
            {
                await VerifyCharacterMatrix(matrix[7..]);
                File.WriteAllText(Path.Combine(DirectoryPath, "results.json"), JsonSerializer.Serialize(new { status = "passed", suite = matrix, checks = Results }));
                return;
            }
            if (System.Environment.GetEnvironmentVariable("SPIRE_MOUSE_VERIFY_SUITE") == "treasure")
            {
                await VerifyRoomProceed();
                File.WriteAllText(Path.Combine(DirectoryPath, "results.json"), JsonSerializer.Serialize(new { status = "passed", suite = "treasure", checks = Results }));
                return;
            }
            if (System.Environment.GetEnvironmentVariable("SPIRE_MOUSE_VERIFY_SUITE") == "native-restart")
            {
                await VerifyNativeRestart();
                File.WriteAllText(Path.Combine(DirectoryPath, "results.json"), JsonSerializer.Serialize(new { status = "passed", suite = "native-restart", checks = Results }));
                return;
            }
            if (System.Environment.GetEnvironmentVariable("SPIRE_MOUSE_VERIFY_SUITE") is "animation" or "animation-slow")
            {
                if (System.Environment.GetEnvironmentVariable("SPIRE_MOUSE_VERIFY_SUITE") == "animation-slow")
                {
                    SettingsStore.Current.Speed.AccelerationMode = SpeedAccelerationMode.Off;
                    SpeedManager.OnSettingsChanged();
                }
                await VerifyAnimationUndo();
                File.WriteAllText(Path.Combine(DirectoryPath, "results.json"), JsonSerializer.Serialize(new { status = "passed", suite = "animation", checks = Results }));
                return;
            }
            if (System.Environment.GetEnvironmentVariable("SPIRE_MOUSE_VERIFY_SUITE") == "gardener")
            {
                await VerifyGardener();
                File.WriteAllText(Path.Combine(DirectoryPath, "results.json"), JsonSerializer.Serialize(new { status = "passed", suite = "gardener", checks = Results }));
                return;
            }
            if (System.Environment.GetEnvironmentVariable("SPIRE_MOUSE_VERIFY_SUITE") is { } fool && fool.StartsWith("fool-"))
            {
                await VerifyFoolScenario(fool[5..]);
                File.WriteAllText(Path.Combine(DirectoryPath, "results.json"), JsonSerializer.Serialize(new { status = "passed", suite = fool, checks = Results }));
                return;
            }
            if (System.Environment.GetEnvironmentVariable("SPIRE_MOUSE_VERIFY_SUITE") is { } modeRestart && modeRestart.StartsWith("mode-restart-"))
            {
                await VerifyModeRoomRestarts(modeRestart[13..]);
                File.WriteAllText(Path.Combine(DirectoryPath, "results.json"), JsonSerializer.Serialize(new { status = "passed", suite = modeRestart, checks = Results }));
                return;
            }
            if (System.Environment.GetEnvironmentVariable("SPIRE_MOUSE_VERIFY_SUITE") == "reattach")
            {
                await VerifyReattach();
                File.WriteAllText(Path.Combine(DirectoryPath, "results.json"), JsonSerializer.Serialize(new { status = "passed", suite = "reattach", checks = Results }));
                return;
            }
            if (System.Environment.GetEnvironmentVariable("SPIRE_MOUSE_VERIFY_SUITE") == "fool")
            {
                await VerifyFoolArrows();
                File.WriteAllText(Path.Combine(DirectoryPath, "results.json"), JsonSerializer.Serialize(new { status = "passed", suite = "fool", checks = Results }));
                return;
            }
            if (System.Environment.GetEnvironmentVariable("SPIRE_MOUSE_VERIFY_SUITE") == "osty-health")
            {
                await VerifyOstyHealth();
                File.WriteAllText(Path.Combine(DirectoryPath, "results.json"), JsonSerializer.Serialize(new { status = "passed", suite = "osty-health", checks = Results }));
                return;
            }
            SaveManager.Instance.SetFtuesEnabled(false);
            var game = NGame.Instance!;
            var run = await game.StartNewSingleplayerRun(MegaCrit.Sts2.Core.Entities.Players.Player.CreateForNewRun(ModelDb.Character<Ironclad>(), SaveManager.Instance.GenerateUnlockStateFromProgress(), 1uL), true,
                [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "MOUSEJOURNAL", GameMode.Standard, false);
            var player = run.Players[0];
            // The fixture is entirely in the precombat save, not injected into a
            // live combat. Replaying must derive every later state from inputs.
            await CardPileCmd.RemoveFromDeck(player.Deck.Cards.ToArray(), false);
            foreach (var model in new CardModel[] { ModelDb.Card<StrikeIronclad>(), ModelDb.Card<StrikeIronclad>(), ModelDb.Card<DefendIronclad>(), ModelDb.Card<DefendIronclad>(), ModelDb.Card<TrueGrit>() })
            {
                var card = run.CreateCard(model, player);
                if (card is TrueGrit) card.UpgradeInternal();
                await CardPileCmd.Add(card, PileType.Deck, skipVisuals: true);
            }
            await PotionCmd.TryToProcure<MegaCrit.Sts2.Core.Models.Potions.BlockPotion>(player);
            TimelineEngine.DiagnosticRoomLoader = async () => await RunManager.Instance.EnterRoomDebug(RoomType.Monster,
                model: ModelDb.Encounter<CultistsNormal>().ToMutable(), showTransition: false);
            await TimelineEngine.DiagnosticRoomLoader();
            await Until(() => TimelineEngine.Settled && TimelineEngine.Points.Count == 1, "initial checkpoint");
            await Screenshot("initial");
            await VerifyPotionUndo();
            string before = State();
            var enemies = NCombatRoom.Instance!.CreatureNodes.Where(n => n.Entity.IsEnemy).ToArray();
            var hp = enemies.Select(n => n.Entity.CurrentHp).ToArray();
            var strike = NPlayerHand.Instance!.ActiveHolders.First(h => h.CardModel is StrikeIronclad);
            await Click(Center(strike.Hitbox), MouseButton.Right);
            Check(MouseInput.PendingHolder == strike && enemies.Select(n => n.Entity.CurrentHp).SequenceEqual(hp), "right click waits for explicit enemy");
            await Click(Center(enemies[1].Hitbox), MouseButton.Right);
            await Until(() => TimelineEngine.Settled && TimelineEngine.Points.Count >= 2, "attack checkpoint");
            Check(enemies[0].Entity.CurrentHp == hp[0] && enemies[1].Entity.CurrentHp < hp[1], "only selected enemy damaged");
            string after = State();
            var originalRoom = NCombatRoom.Instance;
            Check(TimelineEngine.HandleUndoKey(), "direct snapshot undo accepted");
            Check(TimelineEngine.NavigationTask!.IsCompleted && NCombatRoom.Instance == originalRoom,
                "ordinary undo completes synchronously without room reload");
            Check(TimelineEngine.LastNavigationMilliseconds < 100, "ordinary undo model and UI restored under 100ms");
            Check(State() == before, "undo reconstructs exact initial HP energy cards powers and RNG");
            await Move(MouseButton.Xbutton2);
            Check(State() == after, "redo reproduces attack state");
            var grit = NPlayerHand.Instance!.ActiveHolders.First(h => h.CardModel is TrueGrit);
            await Click(Center(grit.Hitbox), MouseButton.Right);
            await Until(() => NPlayerHand.Instance!.IsInCardSelection && TimelineEngine.Points[^1].Choice, "selection checkpoint");
            await Screenshot("choice");
            await Move(MouseButton.Xbutton1);
            Check(State() == after && !NPlayerHand.Instance!.IsInCardSelection, "undo cancels pending selection and returns before card");
            await Move(MouseButton.Xbutton2);
            Check(NPlayerHand.Instance!.IsInCardSelection, "redo reopens pending selection");
            var hand = NPlayerHand.Instance;
            await Click(new Vector2(50, 450), MouseButton.Right);
            Check(hand.IsInCardSelection && hand._selectedCards.Count == 0, "empty right click cannot skip required selection");
            await Click(Center(hand.ActiveHolders.First().Hitbox), MouseButton.Right);
            Check(hand._selectedCards.Count == 1, "right click selects card");
            await Click(Center(hand._selectedHandCardContainer.Holders.First().Hitbox), MouseButton.Right);
            Check(hand._selectedCards.Count == 0, "right click deselects card");
            await Click(Center(hand.ActiveHolders.First().Hitbox), MouseButton.Right);
            await Click(new Vector2(50, 450), MouseButton.Right);
            await Until(() => TimelineEngine.Settled && !TimelineEngine.Points[^1].Choice, "selection resolves");
            string resolved = State();
            await Move(MouseButton.Xbutton1);
            Check(NPlayerHand.Instance!.IsInCardSelection, "undo resolved choice returns to selector");
            await Move(MouseButton.Xbutton1);
            Check(TimelineEngine.Cursor == 1 && State() == after && !NPlayerHand.Instance!.IsInCardSelection,
                "second consecutive undo crosses pending choice to preceding action");
            await Move(MouseButton.Xbutton2);
            Check(TimelineEngine.Cursor == 2 && NPlayerHand.Instance!.IsInCardSelection,
                "first consecutive redo reopens choice");
            await Move(MouseButton.Xbutton2);
            Check(State() == resolved, "redo replays recorded card choice");
            using (SpeedManager.BeginScope(SafeSpeedReason.CardPlayResolution))
                Check(Math.Abs(Engine.TimeScale - SettingsStore.Current.Speed.SpeedMultiplier) < .01, "acceleration survives choice navigation");
            await Screenshot("restored");
            int historyBeforeRestart = TimelineEngine.Points.Count;
            Check(TimelineEngine.HandleQuickRestartKey(), "restart available");
            Check(RestartPresentation.IsActive, "combat restart keeps previous frame for smooth reveal");
            await TimelineEngine.NavigationTask!;
            await Until(() => TimelineEngine.Points.Count == historyBeforeRestart + 1 && TimelineEngine.Settled, "restart settles with previous history retained");
            Check(State() == before, "restart reproduces room entry");

            await Screenshot("restart-combat-transition");
            await Until(() => !RestartPresentation.IsActive, "combat reveal finishes");
            await VerifyRestartSpeed();
            var resurrection = new CombatSnapshot();
            var random = RunManager.Instance.DebugOnlyGetState()!.Rng.CombatCardSelection;
            int expectedRandom = random.NextInt(100000);
            resurrection.Restore();
            Check(random.NextInt(100000) == expectedRandom, "snapshot rewinds future random results, not only RNG counters");
            resurrection.Restore();
            var resurrectionState = TimelineEngine.Fingerprint();
            var victim = NCombatRoom.Instance!.CreatureNodes.First(n => n.Entity.IsEnemy).Entity;
            await CreatureCmd.Kill(victim, true);
            await Frames(12);
            resurrection.Restore();
            Check(TimelineEngine.Fingerprint().SequenceEqual(resurrectionState)
                && NCombatRoom.Instance.GetCreatureNode(victim)?.IsInsideTree() == true,
                "snapshot restores a dead enemy and its visible target node");
            await Screenshot("resurrected");
            await VerifyGridChoice();
            await ExtraChecks();
            await VerifyRoomRestarts();
            SettingsPanel.Open(); await Screenshot("settings"); SettingsPanel.Close();
            Check(true, "settings opens with independent history controls");
            SettingsPanel.Open();
            var bindingButtons = (Dictionary<string, Button>)AccessTools.Field(typeof(SettingsPanel), "BindingButtons").GetValue(null)!;
            var dialog = (AcceptDialog)AccessTools.Field(typeof(SettingsPanel), "_dialog").GetValue(null)!;
            bindingButtons["undo"].EmitSignal(Button.SignalName.Pressed);
            dialog.PushInput(new InputEventKey { Keycode = Key.U, CtrlPressed = true, Pressed = true }, true);
            await Frames(); SettingsStore.Load();
            Check(SettingsStore.Current.Bindings["undo"] == new InputBinding { KeyCode = (long)Key.U, Ctrl = true }, "custom keyboard binding saves and reloads");
            bindingButtons["undo"].EmitSignal(Button.SignalName.Pressed);
            dialog.PushInput(new InputEventMouseButton { ButtonIndex = MouseButton.Xbutton1, Pressed = true }, true);
            await Frames(); SettingsPanel.Close();
            Check(SettingsStore.Current.Bindings["undo"].Mouse == (long)MouseButton.Xbutton1, "mouse binding can be reassigned");
            File.WriteAllText(Path.Combine(DirectoryPath, "results.json"), JsonSerializer.Serialize(new { status = "passed", checks = Results }));
        }
        catch (Exception e)
        {
            await Screenshot("failure"); Entry.Log.Error("VERIFY FAIL: " + e);
            File.WriteAllText(Path.Combine(DirectoryPath!, "results.json"), JsonSerializer.Serialize(new { status = "failed", checks = Results, error = e.ToString() }));
        }
        finally { await Frames(3); NGame.Instance!.GetTree().Quit(); }
    }
    private static async Task Move(MouseButton button)
    {
        await Click(new Vector2(50, 450), button);
        if (TimelineEngine.NavigationTask != null) await TimelineEngine.NavigationTask;
        Check(TimelineEngine.Status == "", "navigation completes without divergence");
        var ui = NCombatRoom.Instance!.Ui;
        Check(new NCombatCardPile[] { ui.DrawPile, ui.DiscardPile, ui.ExhaustPile }.All(p => p._currentCount == p._pile.Cards.Count),
            "visible pile counts match restored state");
        Check(NCombatRoom.Instance.CreatureNodes.All(n => n._stateDisplay._powerContainer._powerNodes.Select(p => p.Model)
            .ToHashSet().SetEquals(n.Entity.Powers.Where(p => p.IsVisible))), "visible power icons match restored state");
        await Frames(8);
    }
    private static async Task VerifyRestartSpeed()
    {
        var speed = SettingsStore.Current.Speed;
        var oldMode = speed.AccelerationMode;
        var oldMultiplier = speed.SpeedMultiplier;
        try
        {
            speed.SpeedMultiplier = 3.7f;
            foreach (var mode in new[] { STS2QuickAnimationMode.Utils.SpeedAccelerationMode.SafeState,
                STS2QuickAnimationMode.Utils.SpeedAccelerationMode.AlwaysOn, STS2QuickAnimationMode.Utils.SpeedAccelerationMode.Off })
            {
                speed.AccelerationMode = mode;
                SpeedManager.OnSettingsChanged();
                for (int i = 0; i < 2; i++)
                {
                    var room = NCombatRoom.Instance;
                    int historyCount = TimelineEngine.Points.Count;
                    Check(TimelineEngine.HandleQuickRestartKey(), $"restart {mode} #{i+1} accepted");
                    await TimelineEngine.NavigationTask!;
                    Check(NCombatRoom.Instance != room, $"restart {mode} #{i+1} rebuilds native combat room");
                    await Until(() => TimelineEngine.Settled && TimelineEngine.Points.Count == historyCount + 1, "native opening draw completes with restart checkpoint");
                    await Until(() => !RestartPresentation.IsActive, "restart reveal finishes at every speed");
                    using (SpeedManager.BeginScope(SafeSpeedReason.CardPlayResolution))
                    {
                        double expected = mode == STS2QuickAnimationMode.Utils.SpeedAccelerationMode.Off ? 1 : 3.7;
                        Check(Math.Abs(Engine.TimeScale - expected) < .01 && speed.SpeedMultiplier == 3.7f,
                            $"restart {mode} #{i+1} preserves configured speed");
                    }
                }
            }
        }
        finally { speed.AccelerationMode = oldMode; speed.SpeedMultiplier = oldMultiplier; SpeedManager.OnSettingsChanged(); }
    }
    private static async Task VerifyGridChoice()
    {
        var original = new CombatSnapshot();
        var run = RunManager.Instance.DebugOnlyGetState()!;
        var player = run.Players[0];
        var combat = CombatManager.Instance.DebugOnlyGetState()!;
        var headbutt = combat.CreateCard(ModelDb.Card<Headbutt>(), player);
        await CardPileCmd.Add(headbutt, PileType.Hand, skipVisuals: false);
        foreach (var model in new CardModel[] { ModelDb.Card<DefendIronclad>(), ModelDb.Card<StrikeIronclad>() })
            await CardPileCmd.Add(combat.CreateCard(model, player), PileType.Discard, skipVisuals: true);
        TimelineEngine.Capture(RunManager.Instance.CombatReplayWriter._replay!);
        await Until(() => TimelineEngine.Points.Count == 1 && TimelineEngine.Settled, "grid fixture checkpoint");
        var holder = NPlayerHand.Instance!.ActiveHolders.First(h => h.CardModel == headbutt);
        await Click(Center(holder.Hitbox), MouseButton.Right);
        await Click(Center(NCombatRoom.Instance!.CreatureNodes.First(n => n.Entity.IsEnemy).Hitbox), MouseButton.Right);
        await Until(() => TimelineEngine.ChoiceVisible && TimelineEngine.Points[^1].Choice, "discard grid choice");
        await Screenshot("grid-choice");
        await Move(MouseButton.Xbutton1);
        Check(!TimelineEngine.ChoiceVisible, "undo closes discard grid without leaving an action suspended");
        await Move(MouseButton.Xbutton2);
        Check(TimelineEngine.ChoiceVisible, "redo reopens discard grid from its immediate snapshot");
        await Move(MouseButton.Xbutton1);
        original.Restore();
        TimelineEngine.Capture(RunManager.Instance.CombatReplayWriter._replay!);
        await Until(() => TimelineEngine.Points.Count == 1 && TimelineEngine.Settled, "restore base fixture");
    }
    private static string State()
    {
        var state = CombatManager.Instance.DebugOnlyGetState()!;
        var p = RunManager.Instance.DebugOnlyGetState()!.Players[0];
        return JsonSerializer.Serialize(new {
            round = state.RoundNumber,
            creatures = NCombatRoom.Instance!.CreatureNodes.Select(n => new { n.Entity.CurrentHp, n.Entity.Block, powers = n.Entity.Powers.Select(x => new { id = x.Id.ToString(), x.Amount }) }),
            p.PlayerCombatState!.Energy,
            piles = new[] { PileType.Hand, PileType.Draw, PileType.Discard, PileType.Exhaust }.Select(t => new { type = t.ToString(), cards = p.PlayerCombatState.AllCards.Where(c => c.Pile.Type == t).Select(c => c.Id.ToString()) })
        });
    }

    private static async Task ExtraChecks()
    {
        var enemies = NCombatRoom.Instance!.CreatureNodes.Where(n => n.Entity.IsEnemy).ToArray();
        var hp = enemies.Select(n => n.Entity.CurrentHp).ToArray();
        var holder = NPlayerHand.Instance!.ActiveHolders.First(h => h.CardModel is StrikeIronclad);
        await DragCard(holder, Center(enemies[0].Hitbox));
        await Until(() => TimelineEngine.Settled && TimelineEngine.Points.Count >= 2, "drag action checkpoint");
        Check(enemies[0].Entity.CurrentHp < hp[0] && enemies[1].Entity.CurrentHp == hp[1], "right drag damages chosen enemy only");
        await Move(MouseButton.Xbutton1);
        var defend = NPlayerHand.Instance!.ActiveHolders.First(h => h.CardModel is DefendIronclad);
        await Click(Center(defend.Hitbox), MouseButton.Right);
        await Until(() => TimelineEngine.Settled && TimelineEngine.Cursor == 1, "branched action");
        Check(!TimelineEngine.HandleRedoKey(), "new action after undo discards redo branch");
        var blank = new Vector2(50, 450);
        int round = CombatManager.Instance.DebugOnlyGetState()!.RoundNumber;
        await Click(blank, MouseButton.Right);
        await Frames(15);
        Check(CombatManager.Instance.DebugOnlyGetState()!.RoundNumber == round, "single empty right click does not end turn");
        var doubleViewport = NGame.Instance!.GetViewport();
        ulong readyAt = Time.GetTicksMsec() + 400;
        await Until(() => Time.GetTicksMsec() >= readyAt, "double click reset");
        doubleViewport.WarpMouse(blank);
        doubleViewport.PushInput(new InputEventMouseMotion { Position = blank, GlobalPosition = blank }, true);
        await Frames(2);
        for (int i = 0; i < 2; i++)
        {
            doubleViewport.PushInput(new InputEventMouseButton { Position = blank, GlobalPosition = blank, ButtonIndex = MouseButton.Right, Pressed = true }, true);
            doubleViewport.PushInput(new InputEventMouseButton { Position = blank, GlobalPosition = blank, ButtonIndex = MouseButton.Right, Pressed = false }, true);
        }
        await Until(() => TimelineEngine.Settled && CombatManager.Instance.DebugOnlyGetState()!.RoundNumber > round && TimelineEngine.Points.Count >= 3, "end turn checkpoint");
        string next = State();
        int latestPoint = TimelineEngine.Cursor;
        for (int expected = latestPoint - 1; expected >= 0; expected--)
        {
            await Move(MouseButton.Xbutton1);
            Check(TimelineEngine.Cursor == expected && TimelineEngine.Fingerprint().SequenceEqual(TimelineEngine.Points[expected].State!),
                $"consecutive undo reaches exact checkpoint {expected}");
        }
        Check(!TimelineEngine.HandleUndoKey(), "undo stops at oldest retained checkpoint");
        for (int expected = 1; expected <= latestPoint; expected++)
        {
            await Move(MouseButton.Xbutton2);
            Check(TimelineEngine.Cursor == expected && TimelineEngine.Fingerprint().SequenceEqual(TimelineEngine.Points[expected].State!),
                $"consecutive redo reaches exact checkpoint {expected}");
        }
        Check(!TimelineEngine.HandleRedoKey(), "redo stops at latest checkpoint");
        Check(State() == next, "enemy turn and reshuffle replay deterministically");
        var viewport = NGame.Instance!.GetViewport();
        int sideRestartHistoryCount = TimelineEngine.Points.Count;
        foreach (var b in new[] { MouseButton.Xbutton1, MouseButton.Xbutton2 })
            viewport.PushInput(new InputEventMouseButton { Position = blank, GlobalPosition = blank, ButtonIndex = b, Pressed = true }, true);
        await Frames(2);
        foreach (var b in new[] { MouseButton.Xbutton1, MouseButton.Xbutton2 })
            viewport.PushInput(new InputEventMouseButton { Position = blank, GlobalPosition = blank, ButtonIndex = b, Pressed = false }, true);
        await TimelineEngine.NavigationTask!;
        await Until(() => TimelineEngine.Settled && TimelineEngine.Points.Count == sideRestartHistoryCount + 1, "side button restart preserves history");
        Check(CombatManager.Instance.DebugOnlyGetState()!.RoundNumber == 1, "side button chord restarts floor");
        await VerifyRapidUndo();
        await VerifyHistoryEdges();
        // Exercise the production map-loading route without the diagnostic room callback.
        TimelineEngine.DiagnosticRoomLoader = null;
        var run = RunManager.Instance.DebugOnlyGetState()!;
        var point = run.Map!.GetAllMapPoints().First(p => p.PointType == MegaCrit.Sts2.Core.Map.MapPointType.Monster);
        await RunManager.Instance.EnterMapCoord(point.coord);
        await Until(() => TimelineEngine.Settled && TimelineEngine.Points.Count == 1, "normal map combat");
        string mapStart = State();
        var card = NPlayerHand.Instance!.ActiveHolders.First(h => h.CardModel is DefendIronclad);
        await Click(Center(card.Hitbox), MouseButton.Right);
        await Until(() => TimelineEngine.Settled && TimelineEngine.Points.Count >= 2, "map combat action");
        await Move(MouseButton.Xbutton1);
        Check(State() == mapStart, "production map reload restores checkpoint without test callback");
        await Screenshot("map-restored");
        Check(TimelineEngine.Navigate(1), "history jump accepts recorded point");
        await TimelineEngine.NavigationTask!;
        Check(TimelineEngine.Status == "" && TimelineEngine.Cursor == 1, "history jump restores selected point");
        int mapRound = CombatManager.Instance.DebugOnlyGetState()!.RoundNumber;
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.E, PhysicalKeycode = Key.E, Unicode = 'e', Pressed = true });
        await Frames(4);
        Input.ParseInputEvent(new InputEventKey { Keycode = Key.E, PhysicalKeycode = Key.E, Unicode = 'e', Pressed = false });
        await Until(() => TimelineEngine.Settled && CombatManager.Instance.DebugOnlyGetState()!.RoundNumber > mapRound, "native keyboard E");
        Check(true, "original E key still ends turn");
        var endingEnemies = NCombatRoom.Instance!.CreatureNodes.Where(n => n.Entity.IsEnemy).Select(n => n.Entity).ToArray();
        var endingHp = endingEnemies.Select(e => e.CurrentHp).ToArray();
        foreach (var enemy in endingEnemies) enemy.SetCurrentHpInternal(0);
        Check(CombatManager.Instance.IsInProgress && CombatManager.Instance.IsEnding, "victory boundary still reports combat in progress");
        Check(!TimelineEngine.HandleUndoKey() && !TimelineEngine.HandleRedoKey() && !TimelineEngine.HandleQuickRestartKey(),
            "navigation rejected once combat victory begins");
        for (int i = 0; i < endingEnemies.Length; i++) endingEnemies[i].SetCurrentHpInternal(endingHp[i]);
        await CreatureCmd.Kill(endingEnemies, true);
        await CombatManager.Instance.CheckWinCondition();
        await Until(() => MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack.Instance?.Peek() is MegaCrit.Sts2.Core.Nodes.Screens.NRewardsScreen,
            "reward screen");
        var stack = MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack.Instance!;
        var rewards = (MegaCrit.Sts2.Core.Nodes.Screens.NRewardsScreen)stack.Peek()!;
        await Frames(20); await Click(blank, MouseButton.Right);
        Check(stack.Peek() == rewards && !rewards.IsComplete, "right click preserves unclaimed rewards");
        RunManager.Instance.RewardsSetSynchronizer.SkipLocalRewardsSet();
        stack.Remove(rewards);
        run = RunManager.Instance.DebugOnlyGetState()!;
        _ = new MegaCrit.Sts2.Core.Rewards.RewardsSet(run.Players[0]).EmptyForRoom(run.CurrentRoom!).Offer();
        await Until(() => stack.Peek() is MegaCrit.Sts2.Core.Nodes.Screens.NRewardsScreen { IsComplete: true } r && r._proceedButton.IsEnabled, "completed reward screen");
        var completedRewards = (MegaCrit.Sts2.Core.Nodes.Screens.NRewardsScreen)stack.Peek()!;
        await Click(blank, MouseButton.Xbutton1);
        await Click(blank, MouseButton.Xbutton2);
        Check(stack.Peek() == completedRewards && completedRewards._proceedButton.IsEnabled
            && completedRewards._proceedButton.IsVisibleInTree(), "undo and redo after victory preserve visible proceed button");
        await Screenshot("proceed-before");
        await Click(blank, MouseButton.Right);
        await Until(() => MegaCrit.Sts2.Core.Nodes.Screens.Map.NMapScreen.Instance?.IsOpen == true, "proceed opens map");
        Check(true, "right click proceeds after rewards complete");
        await Screenshot("proceed-after");
    }
}
[HarmonyPatch(typeof(NGame), nameof(NGame.LaunchMainMenu))]
internal static class VerificationStartupPatch
{
    static void Postfix(ref Task __result) { if (RuntimeVerification.DirectoryPath != null) __result = RuntimeVerification.AfterMenu(__result); }
}
[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SeenPopup))]
internal static class VerificationOnboardingPatch
{
    static bool Prefix(ref bool __result) { if (RuntimeVerification.DirectoryPath == null) return true; __result = true; return false; }
}
[HarmonyPatch(typeof(NGame), nameof(NGame.IsGameFocusedWindow))]
internal static class VerificationFocusPatch
{
    static bool Prefix(ref bool __result) { if (RuntimeVerification.DirectoryPath == null) return true; __result = true; return false; }
}















