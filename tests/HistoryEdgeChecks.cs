using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireMouse.Timeline;
namespace SpireMouse;
internal static partial class RuntimeVerification
{
    private static async Task VerifyHistoryEdges()
    {
        var settings=SettingsStore.Current;
        int limit=settings.HistoryLimit;
        bool choices=settings.IncludeChoiceHistory;
        var binding=settings.Bindings["undo"];
        try
        {
            settings.HistoryLimit=3;
            var manager=RunManager.Instance;
            Check(TimelineEngine.HandleQuickRestartKey(), "history test reloads native room entry");
            await TimelineEngine.NavigationTask!;
            await Until(()=>TimelineEngine.Settled && TimelineEngine.Points[^1].Label=="전투 재시작","native history entry");
            string initial=State();
            // Extra survival HP belongs only to the stress fixture, not the entry save.
            manager.DebugOnlyGetState()!.Players[0].Creature.SetMaxHpInternal(10000);
            manager.DebugOnlyGetState()!.Players[0].Creature.SetCurrentHpInternal(10000);
            TimelineEngine.Capture(manager.CombatReplayWriter._replay!);
            await Until(()=>TimelineEngine.Settled && TimelineEngine.Points.Count==1,"limited history baseline");
            for(int i=0;i<6;i++)
            {
                int round=CombatManager.Instance.DebugOnlyGetState()!.RoundNumber;
                var previous=TimelineEngine.Points[^1];
                await EndStressTurn();
                await Until(()=>TimelineEngine.Settled && CombatManager.Instance.DebugOnlyGetState()!.RoundNumber>round
                    && !ReferenceEquals(TimelineEngine.Points[^1],previous),"history limit turn");
            }
            Check(TimelineEngine.Points.Count==3,"history capacity trims old checkpoints");
            var oldest=TimelineEngine.Points[0].State!;
            BurstUndo(20);await TimelineEngine.NavigationTask!;
            Check(TimelineEngine.Cursor==0 && TimelineEngine.Fingerprint().SequenceEqual(oldest),"undo burst stops at oldest retained snapshot");
            Check(!TimelineEngine.HandleUndoKey(),"trimmed history refuses out-of-range undo");
            int restartPrefixCount = TimelineEngine.Cursor + 1;
            Check(TimelineEngine.HandleQuickRestartKey(),"restart still has entry snapshot after history trim");
            await TimelineEngine.NavigationTask!;
            await Until(()=>!RestartPresentation.IsActive && TimelineEngine.Settled
                && TimelineEngine.Points.Count==Math.Min(settings.HistoryLimit, restartPrefixCount+1),
                "restart after undo retains past prefix and replaces redo branch");
            Check(State()==initial,"trimmed restart restores original entry exactly");
            // The remaining binding/choice assertions start a fresh fixture;
            // restart itself now deliberately preserves the preceding history.
            TimelineEngine.Capture(manager.CombatReplayWriter._replay!);
            await Until(()=>TimelineEngine.Settled && TimelineEngine.Points.Count==1,"binding fixture baseline");
            var defend=NPlayerHand.Instance!.ActiveHolders.First(h=>h.CardModel is DefendIronclad);
            await Click(Center(defend.Hitbox),MouseButton.Right);
            await Until(()=>TimelineEngine.Settled && TimelineEngine.Cursor==1,"modifier binding action");
            settings.Bindings["undo"]=new InputBinding{KeyCode=(long)Key.U,Ctrl=true};
            foreach(bool ctrl in new[]{false,true})
            {
                Input.ParseInputEvent(new InputEventKey{Keycode=Key.U,CtrlPressed=ctrl,Pressed=true});
                Input.ParseInputEvent(new InputEventKey{Keycode=Key.U,CtrlPressed=ctrl,Pressed=false});
                await Frames(10);
                if(TimelineEngine.NavigationTask!=null) await TimelineEngine.NavigationTask;
                Check(TimelineEngine.Cursor==(ctrl?0:1),ctrl?"Ctrl modifier binding performs undo":"unmodified key does not trigger modified undo");
            }
            settings.Bindings["undo"]=binding;
            settings.IncludeChoiceHistory=false;
            var grit=NPlayerHand.Instance!.ActiveHolders.First(h=>h.CardModel is TrueGrit);
            await Click(Center(grit.Hitbox),MouseButton.Right);
            await Until(()=>TimelineEngine.ChoiceVisible,"history-off selector");
            Check(TimelineEngine.Points.All(p=>!p.Choice),"choice-history-off does not create choice checkpoint");
            await Move(MouseButton.Xbutton1);
            Check(State()==initial && !TimelineEngine.ChoiceVisible,"undo cancels selector with choice history disabled");
            var player=manager.DebugOnlyGetState()!.Players[0];
            var potion=player.PotionSlots[0]!;
            manager.ActionQueueSynchronizer.RequestEnqueue(new MegaCrit.Sts2.Core.GameActions.DiscardPotionGameAction(player,0,true));
            await Until(()=>TimelineEngine.Settled && TimelineEngine.Cursor==1,"potion discard checkpoint");
            Check(player.PotionSlots[0]==null,"discard potion removes inventory");
            await Move(MouseButton.Xbutton1);
            Check(player.PotionSlots[0]==potion && NRun.Instance!.GlobalUi.TopBar.PotionContainer._holders[0].Potion?.Model==potion,"discard potion undo restores usable belt");
            await Move(MouseButton.Xbutton2);
            Check(player.PotionSlots[0]==null && !NRun.Instance!.GlobalUi.TopBar.PotionContainer._holders[0].HasPotion,"discard potion redo removes belt");
            await Move(MouseButton.Xbutton1);
        }
        finally{settings.HistoryLimit=limit;settings.IncludeChoiceHistory=choices;settings.Bindings["undo"]=binding;}
    }
}

