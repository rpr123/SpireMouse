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
using STS2QuickAnimationMode.Utils;

namespace SpireMouse;
internal static partial class RuntimeVerification
{
    private static async Task VerifyGardener()
    {
        SaveManager.Instance.SetFtuesEnabled(false);
        var manager = RunManager.Instance;
        var run = await StartTestRun(ModelDb.Character<Ironclad>(), "GARDENERUNDO");
        var player = run.Players[0];
        await CardPileCmd.RemoveFromDeck(player.Deck.Cards.ToArray(), false);
        foreach (var model in new CardModel[] { ModelDb.Card<HandOfGreed>(), ModelDb.Card<StrikeIronclad>(), ModelDb.Card<DefendIronclad>(), ModelDb.Card<DefendIronclad>(), ModelDb.Card<DefendIronclad>() })
            await CardPileCmd.Add(run.CreateCard(model, player), PileType.Deck, skipVisuals: true);
        await manager.EnterRoomDebug(RoomType.Elite, model: ModelDb.Encounter<PhantasmalGardenersElite>().ToMutable(), showTransition: false);
        await Until(() => TimelineEngine.Settled && TimelineEngine.Points.Count == 1, "gardeners ready");
        player.Creature.SetMaxHpInternal(10000);
        player.Creature.SetCurrentHpInternal(10000);
        for (int phase = 0; phase < 2; phase++)
        {
            if (phase == 1)
            {
                SettingsStore.Current.Speed.AccelerationMode = SpeedAccelerationMode.AlwaysOn;
                SpeedManager.OnSettingsChanged();
                for (int turn = 0; turn < 4; turn++)
                {
                    int round = CombatManager.Instance.DebugOnlyGetState()!.RoundNumber;
                    await EndStressTurn();
                    await Until(() => TimelineEngine.Settled && CombatManager.Instance.DebugOnlyGetState()!.RoundNumber > round, "gardener growth turn");
                }
                await Frames(30);
            }
            SettingsStore.Current.Speed.AccelerationMode = SpeedAccelerationMode.Off;
            SpeedManager.OnSettingsChanged();
            foreach (var slot in new[] { "first", "second" })
            {
                var room = NCombatRoom.Instance!;
                var target = room.CreatureNodes.First(n => n.Entity.Monster is PhantasmalGardener && n.Entity.SlotName == slot);
                var creature = target.Entity;
                creature.SetCurrentHpInternal(1);
                if (phase == 0 && slot == "first")
                {
                    target.AnimShake();
                    await Until(() => target._shakeTween?.IsRunning() == true && target.Visuals.Position.Length() > .1f, "native hit shake active");
                    Check(target.Visuals.Position.Length() > .1f, "capture during native hit shake");
                }
                TimelineEngine.Capture(manager.CombatReplayWriter._replay!);
                await Until(() => TimelineEngine.Settled && TimelineEngine.Points.Count == 1, "gardener death baseline");
                var scale = target.Visuals.Scale;
                float defaultScale = target.Visuals.DefaultScale;
                float hue = target.Visuals._hue;
                var model = TimelineEngine.Fingerprint();
                await Screenshot($"gardener-{phase}-{slot}-before");
                var card = NPlayerHand.Instance!.ActiveHolders.First(h => h.CardModel is HandOfGreed);
                await Click(Center(card.Hitbox), MouseButton.Right);
                await Click(Center(target.Hitbox), MouseButton.Right);
                await Until(() => TimelineEngine.Settled && TimelineEngine.Cursor == 1, "gardener killing action");
                Check(creature.IsDead, $"gardener {phase} {slot} killed by card");
                await Screenshot($"gardener-{phase}-{slot}-dead");
                for (int repeat = 0; repeat < 2; repeat++)
                {
                    Check(TimelineEngine.HandleUndoKey(), $"gardener {phase} {slot} undo {repeat}");
                    await TimelineEngine.NavigationTask!;
                    var restored = room.GetCreatureNode(creature)!;
                    Check(!creature.IsDead && TimelineEngine.Fingerprint().SequenceEqual(model), "gardener exact live model restored");
                    Entry.Log.Info($"GARDENER restored scale={restored.Visuals.Scale}/{restored.Visuals.DefaultScale} expected={scale}/{defaultScale} anim={AnimationName(restored)} oldValid={GodotObject.IsInstanceValid(target)}");
                    NGame.Instance.GetViewport().WarpMouse(new Vector2(30, 350));
                    await Screenshot($"gardener-{phase}-{slot}-undo-{repeat}");
                    await CheckIdleKeepsMoving(restored, "restored gardener");
                    await Frames(180);
                    await Screenshot($"gardener-{phase}-{slot}-later-{repeat}");
                    Check(restored.Visuals.Position.IsEqualApprox(Vector2.Zero), "restored gardener does not freeze at a hit-shake offset");
                    Check(restored.IsVisibleInTree() && restored.DeathAnimationTask == null, "gardener remains alive beyond old death animation");
                    Check(restored.Visuals.Scale.IsEqualApprox(scale) && Mathf.IsEqualApprox(restored.Visuals.DefaultScale, defaultScale), "gardener visual size restored after death");
                    Check(restored.Hitbox.GetGlobalRect().Size.DistanceTo(restored.Visuals.Bounds.GetGlobalRect().Size) < 1f, "gardener targeting bounds follow restored visual size");
                    Check(Mathf.IsEqualApprox(restored.Visuals._hue, hue), "gardener hue restored after death");
                    var material = restored.Visuals.SpineBody!.GetNormalMaterial();
                    Check(material is ShaderMaterial shader && Mathf.IsEqualApprox(shader.GetShaderParameter("h").AsSingle(), hue), "gardener rendered hue matches original");
                    if (repeat == 0)
                    {
                        Check(TimelineEngine.HandleRedoKey(), "gardener death redo");
                        await TimelineEngine.NavigationTask!;
                        await Frames(180);
                    }
                }
            }
        }
    }
}
