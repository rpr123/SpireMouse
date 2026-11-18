using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Animation;
using MegaCrit.Sts2.Core.Bindings.MegaSpine;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace SpireMouse.Timeline;

// Save durable animation states, not a half-finished hit/death frame. The game
// supplies transitions and names; no monster-specific animation table is needed.
internal sealed class CreatureAnimationSnapshot
{
    private sealed record Track(int Index, string Name, bool Loop, float Speed);
    private readonly AnimState _state;
    private readonly AnimState _anyState;
    private readonly List<Track> _tracks = new();
    private readonly float _speed;

    internal CreatureAnimationSnapshot(NCreature node, Action<object?> visit)
    {
        var animator = node._spineAnimator!;
        _state = StableState(animator._currentState);
        _anyState = animator._anyState;
        visit(_state);
        visit(_anyState);
        using var animation = node.Visuals.SpineBody!.GetAnimationState();
        _speed = animation.BoundObject.Call("get_time_scale").AsSingle();
        int count = animation.BoundObject.Call("get_num_tracks").AsInt32();
        for (int index = 0; index < count; index++)
        {
            var entry = animation.GetCurrent(index);
            Track? durable = null;
            try
            {
                for (int depth = 0; entry != null && depth < 32; depth++)
                {
                    if (entry.IsLoop() || (index > 0 && entry.IsComplete()))
                        durable = new(index, entry.GetAnimationName(), entry.IsLoop(), entry.BoundObject.Call("get_time_scale").AsSingle());
                    var next = entry.GetNext();
                    entry.Dispose();
                    entry = next;
                }
            }
            finally { entry?.Dispose(); }
            if (durable != null) _tracks.Add(durable);
        }
        if (!_tracks.Any(t => t.Index == 0) && _state.IsLooping)
            _tracks.Add(new(0, _state.Id, true, 1f));
    }

    private static AnimState StableState(AnimState state)
    {
        var seen = new HashSet<AnimState>();
        while (!state.IsLooping && seen.Add(state) && state.GetNextState() is { } next)
            state = next;
        return state;
    }

    internal void Restore(NCreature node)
    {
        if (node._spineAnimator is not { } animator) return;
        using var animation = node.Visuals.SpineBody!.GetAnimationState();
        animation.SetTimeScale(_speed);
        animator._currentState = _state;
        AccessTools.Field(typeof(CreatureAnimator), "_anyState").SetValue(animator, _anyState);
        using (animation.BoundObject.Call("clear_tracks")) { }
        foreach (var saved in _tracks)
        {
            animation.SetAnimation(saved.Name, saved.Loop, saved.Index);
            using var entry = animation.GetCurrent(saved.Index);
            if (entry == null) continue;
            entry.SetMixDuration(0f);
            entry.SetTimeScale(saved.Speed);
            entry.SetTrackTime(saved.Loop ? 0f : entry.GetAnimationEnd());
        }
        using var skeleton = node.Visuals.SpineBody!.GetSkeleton();
        if (skeleton != null)
        {
            skeleton.SetSlotsToSetupPose();
            animation.Apply(skeleton);
        }
        if (_state.BoundsContainer != null) node.UpdateBounds(_state.BoundsContainer);
    }
}