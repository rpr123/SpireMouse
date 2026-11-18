using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Monsters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using SpireMouse.Timeline;

namespace SpireMouse;
internal static partial class RuntimeVerification
{
    private static string AnimationName(NCreature node, int track = 0)
    {
        using var animation = node.Visuals.SpineBody!.GetAnimationState();
        return animation.GetCurrentAnimationName(track) ?? "";
    }

    private static async Task VerifyAnimationUndo()
    {
        SaveManager.Instance.SetFtuesEnabled(false);
        var run = await NGame.Instance!.StartNewSingleplayerRun(MegaCrit.Sts2.Core.Entities.Players.Player.CreateForNewRun(ModelDb.Character<Ironclad>(), SaveManager.Instance.GenerateUnlockStateFromProgress(), 1uL), true,
            [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()], [], "ANIMATIONUNDO", GameMode.Standard, false);
        var player = run.Players[0];
        await CardPileCmd.RemoveFromDeck(player.Deck.Cards.ToArray(), false);
        foreach (var model in new CardModel[] { ModelDb.Card<Bludgeon>(), ModelDb.Card<StrikeIronclad>(), ModelDb.Card<DefendIronclad>(), ModelDb.Card<DefendIronclad>(), ModelDb.Card<DefendIronclad>() })
            await CardPileCmd.Add(run.CreateCard(model, player), PileType.Deck, skipVisuals: true);
        await RunManager.Instance.EnterRoomDebug(RoomType.Boss, model: ModelDb.Encounter<LagavulinMatriarchBoss>().ToMutable(), showTransition: false);
        await Until(() => TimelineEngine.Settled && TimelineEngine.Points.Count == 1, "sleeping monster ready");
        var node = NCombatRoom.Instance!.CreatureNodes.First(n => n.Entity.Monster is LagavulinMatriarch);
        var monster = (LagavulinMatriarch)node.Entity.Monster!;
        node.Entity.SetCurrentHpInternal(130);
        TimelineEngine.Capture(RunManager.Instance.CombatReplayWriter._replay!);
        await Until(() => TimelineEngine.Settled && TimelineEngine.Points.Count == 1, "sleep checkpoint");
        Check(AnimationName(node) == "sleep_loop", "baseline monster visibly sleeps");
        Check(AnimationName(node, 1) == "_tracks/eyes_closed_loop", "baseline shell eyes are closed");
        await Screenshot("animation-before");
        var card = NPlayerHand.Instance!.ActiveHolders.First(h => h.CardModel is Bludgeon);
        await Click(Center(card.Hitbox), MouseButton.Right);
        await Click(Center(node.Hitbox), MouseButton.Right);
        await Until(() => TimelineEngine.Settled && TimelineEngine.Cursor > 0 && monster.IsAwake && monster.IsShellAwake, "damage awakens body and shell");
        await Until(() => AnimationName(node) == "idle_loop" && AnimationName(node, 1) == "_tracks/eyes_open_loop", "awake animations settle");
        Check(monster.IsAwake && monster.IsShellAwake, "real card action awakens body and shell");
        await Screenshot("animation-awake");
        var room = NCombatRoom.Instance;
        for (int i = 0; i < 4; i++)
        {
            var timer = System.Diagnostics.Stopwatch.StartNew();
            Check(TimelineEngine.HandleUndoKey(), "animation undo accepted " + i);
            Check(TimelineEngine.NavigationTask!.IsCompleted && NCombatRoom.Instance == room, "animation undo remains synchronous " + i);
            Check(timer.ElapsedMilliseconds < 100, "animation undo under 100 ms " + i);
            node = room!.GetCreatureNode(monster.Creature)!;
            Check(!monster.IsAwake && !monster.IsShellAwake, "sleeping model restored " + i);
            Check(monster._sleepingVfx != null && GodotObject.IsInstanceValid(monster._sleepingVfx)
                && node.IsAncestorOf(monster._sleepingVfx), "native sleep effect reattached to the restored creature " + i);
            Check(AnimationName(node) == "sleep_loop" && AnimationName(node, 1) == "_tracks/eyes_closed_loop", "body and extra track restored immediately " + i + " actual=" + AnimationName(node) + "/" + AnimationName(node, 1));
            await Frames(25);
            Check(AnimationName(node) == "sleep_loop" && AnimationName(node, 1) == "_tracks/eyes_closed_loop", "future animation queue cannot return " + i);
            if (i == 0) await Screenshot("animation-undo");
            Check(TimelineEngine.HandleRedoKey(), "animation redo accepted " + i);
            await TimelineEngine.NavigationTask!;
            node = room!.GetCreatureNode(monster.Creature)!;
            await Until(() => AnimationName(node) == "idle_loop" && AnimationName(node, 1) == "_tracks/eyes_open_loop", "redo animation queue finishes");
            Check(monster.IsAwake && monster.IsShellAwake, "awake model and motion restored on redo " + i);
        }
        Check(TimelineEngine.HandleUndoKey(), "undo before replaying action");
        await TimelineEngine.NavigationTask!;
        node = room!.GetCreatureNode(monster.Creature)!;
        card = NPlayerHand.Instance!.ActiveHolders.First(h => h.CardModel is Bludgeon);
        await Click(Center(card.Hitbox), MouseButton.Right);
        await Click(Center(node.Hitbox), MouseButton.Right);
        await Until(() => TimelineEngine.Settled && monster.IsAwake && monster.IsShellAwake, "restored trigger graph works on new action");
        await Until(() => AnimationName(node) == "idle_loop" && AnimationName(node, 1) == "_tracks/eyes_open_loop", "replayed body and shell wake settle");
        Check(AnimationName(node, 1) == "_tracks/eyes_open_loop", "new action after undo still animates correctly");
        await Screenshot("animation-replayed");
        await VerifyCrabFacing();
    }

    private static float AnimationTime(NCreature node)
    {
        using var animation = node.Visuals.SpineBody!.GetAnimationState();
        using var entry = animation.GetCurrent(0);
        return entry?.GetTrackTime() ?? -1f;
    }

    private static async Task CheckIdleKeepsMoving(NCreature node, string label)
    {
        if (!node.HasSpineAnimation)
        {
            // Another installed mod can replace a creature's visuals with a non-Spine
            // scene; there is no animation track to sample. Recorded, never counted as a pass.
            Entry.Log.Info($"VERIFY SKIP: {label} idle animation (no Spine body: {node.Visuals.SceneFilePath})");
            return;
        }
        await Until(() => AnimationName(node) == "idle_loop", label + " idle begins");
        float before = AnimationTime(node);
        await Frames(15);
        Check(AnimationTime(node) > before, label + " idle animation keeps advancing");
    }

    private static async Task VerifyCrabFacing()
    {
        var manager = RunManager.Instance;
        var run = manager.DebugOnlyGetState()!;
        var player = run.Players[0];
        await CardPileCmd.RemoveFromDeck(player.Deck.Cards.ToArray(), false);
        foreach (var model in new CardModel[] { ModelDb.Card<StrikeIronclad>(), ModelDb.Card<StrikeIronclad>(), ModelDb.Card<StrikeIronclad>(), ModelDb.Card<DefendIronclad>(), ModelDb.Card<DefendIronclad>() })
            await CardPileCmd.Add(run.CreateCard(model, player), PileType.Deck, skipVisuals: true);
        await RelicCmd.Obtain(ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.PaelsLegion>().ToMutable(), player);
        await manager.EnterRoomDebug(RoomType.Boss, model: ModelDb.Encounter<KaiserCrabBoss>().ToMutable(), showTransition: false);
        await Until(() => TimelineEngine.Settled && TimelineEngine.Points.Count == 1, "crab encounter ready");
        var node = NCombatRoom.Instance!.GetCreatureNode(player.Creature)!;
        var pet = NCombatRoom.Instance.CreatureNodes.First(n => n.Entity.PetOwner == player);
        var surrounded = player.Creature.GetPower<MegaCrit.Sts2.Core.Models.Powers.SurroundedPower>()!;
        Check(surrounded != null && node.Body.Scale.X > 0, "crab starts facing right");
        await CheckIdleKeepsMoving(node, "crab initial");
        var left = NCombatRoom.Instance.CreatureNodes.First(n => n.Entity.HasPower<MegaCrit.Sts2.Core.Models.Powers.BackAttackLeftPower>());
        var right = NCombatRoom.Instance.CreatureNodes.First(n => n.Entity.HasPower<MegaCrit.Sts2.Core.Models.Powers.BackAttackRightPower>());
        foreach (var target in new[] { left, right })
        {
            int cursor = TimelineEngine.Cursor;
            var card = NPlayerHand.Instance!.ActiveHolders.First(h => h.CardModel is StrikeIronclad);
            await Click(Center(card.Hitbox), MouseButton.Right);
            await Click(Center(target.Hitbox), MouseButton.Right);
            await Until(() => TimelineEngine.Settled && TimelineEngine.Cursor > cursor, "crab targeted card settles");
            Check(Math.Sign(node.Body.Scale.X) == (target == left ? -1 : 1), "card turns character toward selected arm");
        }
        for (int repeat = 0; repeat < 3; repeat++)
        {
            Check(TimelineEngine.Navigate(1), "crab restore left accepted " + repeat);
            await TimelineEngine.NavigationTask!;
            node = NCombatRoom.Instance!.GetCreatureNode(player.Creature)!;
            pet = NCombatRoom.Instance.CreatureNodes.First(n => n.Entity.PetOwner == player);
            Check(node.Body.Scale.X < 0 && surrounded!.Facing == MegaCrit.Sts2.Core.Models.Powers.SurroundedPower.Direction.Left, "crab visual and model both face left " + repeat);
            Check(pet.Body.Scale.X < 0, "crab companion faces left after undo " + repeat);
            await CheckIdleKeepsMoving(node, "crab left undo " + repeat);
            if (repeat == 0) await Screenshot("crab-undo-left");
            Check(TimelineEngine.Navigate(0), "crab restore opening accepted " + repeat);
            await TimelineEngine.NavigationTask!;
            node = NCombatRoom.Instance!.GetCreatureNode(player.Creature)!;
            pet = NCombatRoom.Instance.CreatureNodes.First(n => n.Entity.PetOwner == player);
            Check(node.Body.Scale.X > 0 && surrounded!.Facing == MegaCrit.Sts2.Core.Models.Powers.SurroundedPower.Direction.Right, "crab opening faces right " + repeat);
            Check(pet.Body.Scale.X > 0, "crab companion faces right after undo " + repeat);
            await CheckIdleKeepsMoving(node, "crab opening undo " + repeat);
            Check(TimelineEngine.Navigate(2), "crab redo accepted " + repeat);
            await TimelineEngine.NavigationTask!;
            node = NCombatRoom.Instance!.GetCreatureNode(player.Creature)!;
            pet = NCombatRoom.Instance.CreatureNodes.First(n => n.Entity.PetOwner == player);
            Check(node.Body.Scale.X > 0, "crab redo faces right " + repeat);
            await CheckIdleKeepsMoving(node, "crab redo " + repeat);
        }
        await Screenshot("crab-redo-right");
    }
}
