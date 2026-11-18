using System.Text.Json;
using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Models.Potions;
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
    private static async Task VerifyCharacterMatrix(string name)
    {
        CharacterModel character;
        CardModel[] cards;
        switch (name)
        {
            case "Defect": character=ModelDb.Character<Defect>(); cards=[ModelDb.Card<Zap>(), ModelDb.Card<Dualcast>(), ModelDb.Card<StrikeDefect>(),ModelDb.Card<Capacitor>(),ModelDb.Card<DefendDefect>()]; break;
            case "Silent": character=ModelDb.Character<Silent>(); cards=[ModelDb.Card<Survivor>(),ModelDb.Card<Prepared>(),ModelDb.Card<Acrobatics>(),ModelDb.Card<StrikeSilent>(),ModelDb.Card<DefendSilent>()]; break;
            case "Necrobinder": character=ModelDb.Character<Necrobinder>(); cards=[ModelDb.Card<Bodyguard>(),ModelDb.Card<Unleash>(),ModelDb.Card<StrikeNecrobinder>(),ModelDb.Card<DefendNecrobinder>(),ModelDb.Card<DefendNecrobinder>()]; break;
            case "Fool": character=ModelDb.Character<Fool>(); cards=[ModelDb.Card<FoolishShot>(),ModelDb.Card<Quiver>(),ModelDb.Card<StrikeFool>(),ModelDb.Card<DefendFool>(),ModelDb.Card<DefendFool>()]; break;
            case "Regent": character=ModelDb.Character<Regent>(); cards=[ModelDb.Card<Venerate>(),ModelDb.Card<SummonForth>(),ModelDb.Card<FallingStar>(),ModelDb.Card<StrikeRegent>(),ModelDb.Card<DefendRegent>()]; break;
            default: character=ModelDb.Character<Ironclad>(); cards=[ModelDb.Card<Offering>(),ModelDb.Card<BurningPact>(),ModelDb.Card<TrueGrit>(),ModelDb.Card<StrikeIronclad>(),ModelDb.Card<DefendIronclad>()]; break;
        }
        if(name=="Gold") cards=[ModelDb.Card<HandOfGreed>(),ModelDb.Card<StrikeIronclad>(),ModelDb.Card<DefendIronclad>(),ModelDb.Card<DefendIronclad>(),ModelDb.Card<TrueGrit>()];
        if(name.StartsWith("Form"))
        {
            (CharacterModel Character, CardModel Card) formSetup = name switch {
                "FormEcho" => (ModelDb.Character<Defect>(), ModelDb.Card<EchoForm>()),
                "FormSerpent" => (ModelDb.Character<Silent>(), ModelDb.Card<SerpentForm>()),
                "FormReaper" => (ModelDb.Character<Necrobinder>(), ModelDb.Card<ReaperForm>()),
                "FormVoid" => (ModelDb.Character<Regent>(), ModelDb.Card<VoidForm>()),
                _ => (ModelDb.Character<Ironclad>(), (CardModel)ModelDb.Card<DemonForm>())
            };
            character = formSetup.Character;
            cards=[formSetup.Card, ModelDb.Card<StrikeIronclad>(),ModelDb.Card<DefendIronclad>(),ModelDb.Card<DefendIronclad>(),ModelDb.Card<TrueGrit>()];
        }
        SaveManager.Instance.SetFtuesEnabled(false);
        var run=await NGame.Instance!.StartNewSingleplayerRun(MegaCrit.Sts2.Core.Entities.Players.Player.CreateForNewRun(character, name=="Blitz" ? MegaCrit.Sts2.Core.Unlocks.UnlockState.all : SaveManager.Instance.GenerateUnlockStateFromProgress(), 1uL), true,[ModelDb.Act<Overgrowth>(),ModelDb.Act<Hive>(),ModelDb.Act<Glory>()],[],"MATRIX"+name,name switch { "Dread"=>GameMode.Dread, "Blitz"=>GameMode.Blitz, _=>GameMode.Standard }, false, name=="Dread" ? 10 : 0);
        var player=run.Players[0];
        await CardPileCmd.RemoveFromDeck(player.Deck.Cards.ToArray(),false);
        foreach(var model in cards)
        {
            var card=run.CreateCard(model,player);
            if(card is TrueGrit) card.UpgradeInternal();
            await CardPileCmd.Add(card,PileType.Deck,skipVisuals:true);
        }
        if (name == "Pael") await RelicCmd.Obtain(ModelDb.Relic<MegaCrit.Sts2.Core.Models.Relics.PaelsLegion>().ToMutable(), player);
        if (name == "Byrdpip") await new MegaCrit.Sts2.Core.Entities.RestSite.HatchRestSiteOption(player).OnSelect();
        await PotionCmd.TryToProcure<FirePotion>(player);
        await PotionCmd.TryToProcure<StrengthPotion>(player);
        if(name=="Choices") await PotionCmd.TryToProcure<AttackPotion>(player);
        EncounterModel encounter=name switch { "Boss"=>ModelDb.Encounter<QueenBoss>(), "Vantom"=>ModelDb.Encounter<VantomBoss>(), "Elite"=>ModelDb.Encounter<BygoneEffigyElite>(), _=>ModelDb.Encounter<CultistsNormal>() };
        TimelineEngine.DiagnosticRoomLoader = async () => await RunManager.Instance.EnterRoomDebug(name=="Boss"?RoomType.Boss:name=="Elite"?RoomType.Elite:RoomType.Monster,model:encounter.ToMutable(),showTransition:false);
        await TimelineEngine.DiagnosticRoomLoader();
        await Until(()=>TimelineEngine.Settled && TimelineEngine.Points.Count==1,"matrix initial checkpoint");
        string initial=State();
        if (name is "Pael" or "Byrdpip") await VerifyMatrixViews(name+" initial");
        if(name is "Boss" or "Elite") { await VerifyRapidUndo(); await Screenshot("stress-"+name); return; }
        if(name=="Choices") { await VerifyGeneratedPotionChoice(); return; }
        if(name=="Gold") { await VerifyGoldAndDeath(); return; }
        if(name=="Lifecycle") { await VerifyRestoreLifecycle(); return; }
        if(name.StartsWith("Form")) { await VerifyPersistentForms(cards[0].GetType()); return; }
        if(name=="Vantom") { await VerifyAuthoredScale(); return; }
        foreach(var model in cards.DistinctBy(c=>c.Id))
        {
            var holder=NPlayerHand.Instance!.ActiveHolders.First(h=>h.CardModel.Id==model.Id);
            await Click(Center(holder.Hitbox),MouseButton.Right);
            if(MouseInput.PendingHolder!=null)
                await Click(Center(NCombatRoom.Instance!.CreatureNodes.First(n=>n.Entity.IsEnemy).Hitbox),MouseButton.Right);
            await Until(()=>TimelineEngine.ChoiceVisible || (TimelineEngine.Settled && TimelineEngine.Cursor>0),"matrix card played "+model.Id);
            if(TimelineEngine.ChoiceVisible)
            {
                await Until(()=>NPlayerHand.Instance!.IsInCardSelection,"matrix hand choice");
                await Click(Center(NPlayerHand.Instance!.ActiveHolders.First().Hitbox),MouseButton.Right);
                await Click(new Vector2(50,450),MouseButton.Right);
                await Until(()=>TimelineEngine.Settled && !TimelineEngine.ChoiceVisible && TimelineEngine.Cursor>0 && !TimelineEngine.Points[TimelineEngine.Cursor].Choice,"matrix choice resolves");
            }
            string after=State(); int latest=TimelineEngine.Cursor;
            Check(TimelineEngine.Navigate(0),name+" "+model.Id+" undo accepted");
            await TimelineEngine.NavigationTask!;
            Check(TimelineEngine.Status=="" && State()==initial,name+" "+model.Id+" exact initial model restored");
            await VerifyMatrixViews(name+" "+model.Id+" undo");
            Check(TimelineEngine.Navigate(latest),name+" "+model.Id+" redo accepted");
            await TimelineEngine.NavigationTask!;
            Check(TimelineEngine.Status=="" && State()==after,name+" "+model.Id+" exact redo model restored");
            await VerifyMatrixViews(name+" "+model.Id+" redo");
            TimelineEngine.Navigate(0); await TimelineEngine.NavigationTask!;
        }
        for(int slot=0;slot<2;slot++)
        {
            var potion=player.PotionSlots[slot]!;
            potion.EnqueueManualUse(slot==0 ? NCombatRoom.Instance!.CreatureNodes.First(n=>n.Entity.IsEnemy).Entity : player.Creature);
            await Until(()=>TimelineEngine.Settled && TimelineEngine.Cursor>0,"matrix potion action");
            string after=State(); int latest=TimelineEngine.Cursor;
            TimelineEngine.Navigate(0);await TimelineEngine.NavigationTask!;
            Check(State()==initial,name+" potion "+slot+" undo fingerprint");
            await VerifyMatrixViews(name+" potion undo");
            TimelineEngine.Navigate(latest);await TimelineEngine.NavigationTask!;
            Check(State()==after,name+" potion "+slot+" redo fingerprint");
            await VerifyMatrixViews(name+" potion redo");
            TimelineEngine.Navigate(0);await TimelineEngine.NavigationTask!;
        }
        for(int i=0;i<3;i++)
        {
            int round=CombatManager.Instance.DebugOnlyGetState()!.RoundNumber;
            await EndStressTurn();
            await Until(()=>TimelineEngine.Settled && CombatManager.Instance.DebugOnlyGetState()!.RoundNumber>round,"matrix turn");
            await Move(MouseButton.Xbutton1);
            await VerifyMatrixViews(name+" turn undo");
            await Move(MouseButton.Xbutton2);
            await VerifyMatrixViews(name+" turn redo");
        }
        int restartHistoryCount = TimelineEngine.Points.Count;
        Check(TimelineEngine.HandleQuickRestartKey(),name+" restart");await TimelineEngine.NavigationTask!;
        await Until(()=>TimelineEngine.Settled && TimelineEngine.Points.Count==restartHistoryCount+1,"matrix native restart ready with history retained");
        Check(State()==initial,name+" restart exact initial state");
        await Until(()=>!RestartPresentation.IsActive,"matrix reveal");
        await VerifyMatrixViews(name+" restart");
        await Screenshot("matrix-"+name);
    }
    private static async Task VerifyMatrixViews(string label)
    {
        await Frames(12);
        var player=RunManager.Instance.DebugOnlyGetState()!.Players[0];
        var room=NCombatRoom.Instance!;
        Check(room.Ui.EndTurnButton.CanTurnBeEnded && room.Ui.EndTurnButton.Position.DistanceTo(room.Ui.EndTurnButton.ShowPos)<1,label+" turn button");
        if (label.StartsWith("Pael") || label.StartsWith("Byrdpip"))
        {
            var pet = room.CreatureNodes.Single(n => n.Entity.Monster is MegaCrit.Sts2.Core.Models.Monsters.PaelsLegion or MegaCrit.Sts2.Core.Models.Monsters.Byrdpip);
            Check(!pet._stateDisplay.IsVisibleInTree(), label+" companion health stays hidden");
            Check(!pet.IsInteractable && pet.Hitbox.MouseFilter == Control.MouseFilterEnum.Ignore, label+" companion stays untargetable");
            Check(room.CreatureNodes.Where(n => n.Entity.IsEnemy || n.Entity.IsPlayer).All(n => n._stateDisplay.IsVisibleInTree()), label+" ordinary health stays visible");
        }
        var belt=NRun.Instance!.GlobalUi.TopBar.PotionContainer;
        Check(belt._holders.Take(player.MaxPotionCount).Select(h=>h.Potion?.Model).SequenceEqual(player.PotionSlots),label+" potion belt");
        Check(belt._holders.Take(player.MaxPotionCount).All(h=>new[]{h.FocusNeighborLeft,h.FocusNeighborRight,h.FocusNeighborBottom}.All(p=>p.IsEmpty || h.GetNodeOrNull(p) is Control { } c && c.IsInsideTree() && !c.IsQueuedForDeletion())),label+" potion focus neighbours resolve to live controls");
        Check(NRun.Instance!.GlobalUi.TopBar.Hp._hpLabel.Text == $"{player.Creature.CurrentHp}/{player.Creature.MaxHp}",label+" top-bar HP");
        var orb=room.GetCreatureNode(player.Creature)?.OrbManager;
        if(orb!=null) Check(orb._orbs.Count==player.PlayerCombatState!.OrbQueue.Capacity,label+" orb slot count");
        if(orb!=null) Check(orb._orbs.Where(o=>o.Model!=null).Select(o=>o.Model).SequenceEqual(player.PlayerCombatState!.OrbQueue.Orbs),label+" visible orb models");
        if(player.Character.ShouldAlwaysShowStarCounter) Check(room.Ui._starCounter._displayedStarCount==player.PlayerCombatState!.Stars,label+" visible star count");
        if(player.Osty!=null)
        {
            Check(room.GetCreatureNode(player.Osty)!=null,label+" Osty visible");
            Check(!player.Osty.IsAlive || HealthActuallyVisible(room.GetCreatureNode(player.Osty)!),label+" live Osty health remains opaque and visible");
        }
    }
}










