using Godot;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Orbs;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace SpireMouse.Timeline;

// A view owns animation tasks, tweens, hitboxes and subscriptions. Retiring that
// owner before model mutation is safer than repairing each transient field.
internal sealed class CreaturePresentation
{
    private sealed record Saved(CreatureVisualSnapshot Visual, CreatureAnimationSnapshot? Animation, Creature? BlockOwner, PersistentCreatureEffects Effects);
    private readonly Dictionary<Creature, Saved> _saved = new();

    internal CreaturePresentation(Action<object?> visit)
    {
        foreach (var node in NCombatRoom.Instance!.CreatureNodes)
            _saved[node.Entity] = new(new(node), node._spineAnimator == null ? null : new(node, visit), node._stateDisplay._blockTrackingCreature, new(node));
    }

    internal static void DetachCurrent()
    {
        var room = NCombatRoom.Instance!;
        foreach (var node in room.CreatureNodes.Concat(room.RemovingCreatureNodes).Distinct().ToArray())
        {
            room._creatureNodes.Remove(node);
            room._removingCreatureNodes.Remove(node);
            if (!GodotObject.IsInstanceValid(node)) continue;
            PersistentCreatureEffects.ReleaseTransientHandles(node.Entity);
            node.HideHoverTips();
            node.Hide();
            // _ExitTree cancels native death work and unsubscribes against the
            // original powers. Never mutate those powers before this boundary.
            node.GetParent()?.RemoveChild(node);
            node.QueueFreeSafely();
        }
    }

    internal void Rebuild(CombatState state)
    {
        var room = NCombatRoom.Instance!;
        // A pet's native placement needs its owner's new view to exist first.
        foreach (var creature in state.Creatures.OrderBy(c => c.PetOwner != null))
        {
            room.AddCreature(creature);
            var node = room.GetCreatureNode(creature)!;
            if (_saved.TryGetValue(creature, out var saved))
            {
                saved.Visual.Restore(node);
                saved.Animation?.Restore(node);
                saved.Effects.Restore(creature);
                if (saved.BlockOwner != null) node.TrackBlockStatus(saved.BlockOwner);
            }
            // Suppress only the new view's entry fade. There are no old health,
            // hover, death or intent tweens attached to this view to enumerate.
            node._stateDisplay._showHideTween?.Kill();
            node._stateDisplay.Position = node._stateDisplay._originalPosition;
            node._stateDisplay.Modulate = Colors.White;
            CreatureHealthLifecycle.Refresh(node);
            if (node.OrbManager is { } orbs && creature.Player?.PlayerCombatState?.OrbQueue is { } queue)
            {
                // Native initialization allocates empty slots. Populate from the
                // restored queue without replaying channel/evoke gameplay hooks.
                orbs._curTween?.Kill();
                foreach (var old in orbs._orbs.ToArray())
                {
                    old.GetParent()?.RemoveChild(old);
                    old.QueueFreeSafely();
                }
                orbs._orbs.Clear();
                for (int i = 0; i < queue.Capacity; i++)
                {
                    var icon = NOrb.Create(orbs.IsLocal, queue.Orbs.ElementAtOrDefault(i));
                    orbs._orbContainer.AddChild(icon);
                    orbs._orbs.Add(icon);
                }
                orbs.TweenLayout();
                orbs._curTween?.CustomStep(1);
                orbs.UpdateControllerNavigation();
            }
            if (creature.IsEnemy)
            {
                node.UpdateIntent(state.Players.Select(p => p.Creature)).GetAwaiter().GetResult();
                node.IntentContainer.Modulate = Colors.White;
            }
        }
    }
}
