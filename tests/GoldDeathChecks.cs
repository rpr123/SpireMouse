using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireMouse.Timeline;
namespace SpireMouse;
internal static partial class RuntimeVerification
{
    private static async Task VerifyGoldAndDeath()
    {
        var manager=RunManager.Instance;
        var player=manager.DebugOnlyGetState()!.Players[0];
        var target=NCombatRoom.Instance!.CreatureNodes.First(n=>n.Entity.IsEnemy);
        target.Entity.SetCurrentHpInternal(10);
        TimelineEngine.Capture(manager.CombatReplayWriter._replay!);
        await Until(()=>TimelineEngine.Settled && TimelineEngine.Points.Count==1,"gold fixture checkpoint");
        var before=TimelineEngine.Fingerprint(); int initialGold=player.Gold;
        var card=NPlayerHand.Instance!.ActiveHolders.First(h=>h.CardModel is HandOfGreed);
        await Click(Center(card.Hitbox),MouseButton.Right);
        await Click(Center(target.Hitbox),MouseButton.Right);
        await Until(()=>TimelineEngine.Settled && TimelineEngine.Cursor==1,"gold killing blow checkpoint");
        Check(player.Gold>initialGold && target.Entity.IsDead,"kill generates gold while combat continues");
        var after=TimelineEngine.Fingerprint(); int earnedGold=player.Gold;
        Check(TimelineEngine.HandleUndoKey(),"gold killing blow undo accepted");await TimelineEngine.NavigationTask!;
        Check(TimelineEngine.Fingerprint().SequenceEqual(before) && player.Gold==initialGold && !target.Entity.IsDead,"gold and dead enemy model restored together");
        Check(NRun.Instance!.GlobalUi.TopBar.Gold._goldLabel.Text==player.Gold.ToString(),"gold undo updates top-bar amount immediately");
        await Frames(45);
        Check(NCombatRoom.Instance.GetCreatureNode(target.Entity)?.IsVisibleInTree()==true,"resurrected enemy remains visible after old death animation");
        await CheckIdleKeepsMoving(NCombatRoom.Instance.GetCreatureNode(target.Entity)!, "resurrected enemy");
        await CheckIdleKeepsMoving(NCombatRoom.Instance.GetCreatureNode(player.Creature)!, "player after resurrection undo");
        Check(TimelineEngine.HandleRedoKey(),"gold killing blow redo accepted");await TimelineEngine.NavigationTask!;
        Check(TimelineEngine.Fingerprint().SequenceEqual(after) && player.Gold==earnedGold,"gold killing blow redo reproduces model");
        Check(NRun.Instance!.GlobalUi.TopBar.Gold._goldLabel.Text==player.Gold.ToString(),"gold redo updates top-bar amount immediately");
        await Screenshot("gold-restored");
        await CreatureCmd.Kill(player.Creature,true);
        await CombatManager.Instance.CheckWinCondition();
        await Until(()=>CombatManager.Instance.IsOverOrEnding,"player defeat boundary");
        Check(!TimelineEngine.HandleUndoKey() && !TimelineEngine.HandleRedoKey(),"defeat refuses combat history navigation");
    }
}

