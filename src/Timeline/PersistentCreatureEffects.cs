using System.Reflection;
using Godot;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Vfx.Forms;
using MegaCrit.Sts2.Core.Nodes.Vfx.Utilities;

namespace SpireMouse.Timeline;

// A model can retain a reference to a persistent presentation component. Rebind
// that reference after view replacement without rerunning power gameplay hooks.
internal sealed class PersistentCreatureEffects
{
    private sealed record Link(AbstractModel Model, FieldInfo Field, int Effect);
    private sealed record Ramp(string Path, float Value, bool Increasing);
    private sealed record Effect(Type Type, string Parent, Transform2D Transform, bool? Active, Ramp[] Ramps);
    private readonly List<Effect> _effects = new();
    private readonly List<Link> _links = new();
    private static readonly Dictionary<Type, FieldInfo[]> Fields = new();

    internal PersistentCreatureEffects(NCreature node)
    {
        var indices = new Dictionary<Node, int>();
        foreach (var model in Models(node.Entity))
            foreach (var field in ViewFields(model.GetType()).Where(f => typeof(Node).IsAssignableFrom(f.FieldType)))
            {
                if (field.GetValue(model) is not Node effect || !GodotObject.IsInstanceValid(effect))
                {
                    _links.Add(new(model, field, -1));
                    continue;
                }
                // The room and its background keep their own lifetime. Only
                // components owned by the retired creature need replacement.
                if (!node.IsAncestorOf(effect)) continue;
                if (!indices.TryGetValue(effect, out int index))
                {
                    index = _effects.Count;
                    indices.Add(effect, index);
                    var ramps = Descendants(effect).OfType<NValueRamp>()
                        .Select(r => new Ramp(effect.GetPathTo(r).ToString(), r._currentValue, r._isIncreasing)).ToArray();
                    _effects.Add(new(effect.GetType(), node.GetPathTo(effect.GetParent()).ToString(),
                        effect is Node2D spatial ? spatial.Transform : Transform2D.Identity,
                        effect is NFormVfx form ? form._isActive : null, ramps));
                }
                _links.Add(new(model, field, index));
            }
    }

    internal void Restore(Creature creature)
    {
        var owner = MegaCrit.Sts2.Core.Nodes.Rooms.NCombatRoom.Instance!.GetCreatureNode(creature)!;
        var rebuilt = new List<Node>();
        foreach (var saved in _effects)
        {
            var parent = owner.GetNode(saved.Parent);
            // Use native factories: they initialize particle resources, locale,
            // skeletal bindings and subscriptions that a raw scene clone misses.
            var create = saved.Type.GetMethods(BindingFlags.Public | BindingFlags.Static)
                .FirstOrDefault(m => m.Name == "Create" && typeof(Node).IsAssignableFrom(m.ReturnType)
                    && m.GetParameters() is var p && p.Length > 0
                    && (p[0].ParameterType == typeof(Creature) || p[0].ParameterType == typeof(Vector2))
                    && p.Skip(1).All(a => a.HasDefaultValue));
            if (create == null) throw new InvalidOperationException($"No supported native factory for {saved.Type.Name}.");
            var arguments = create.GetParameters().Select(p => p.DefaultValue).ToArray();
            arguments[0] = create.GetParameters()[0].ParameterType == typeof(Creature)
                ? creature : parent is Node2D spatial ? spatial.ToGlobal(saved.Transform.Origin) : Vector2.Zero;
            if (create.Invoke(null, arguments) is not Node effect)
                throw new InvalidOperationException($"Cannot reconstruct persistent creature effect {saved.Type.Name}.");
            if (effect.GetParent() == null) parent.AddChild(effect);
            if (effect is Node2D effect2D) effect2D.Transform = saved.Transform;
            if (effect is NFormVfx form && saved.Active is { } active) form.SetActive(active);
            // Active alone is insufficient: native forms can start visually on
            // before their active flag changes. Keep the common ramp state too.
            foreach (var ramp in saved.Ramps)
            {
                var current = effect.GetNode<NValueRamp>(ramp.Path);
                current.SetIncreasing(ramp.Increasing);
                current.ForceValue(ramp.Value);
            }
            rebuilt.Add(effect);
        }
        foreach (var link in _links) link.Field.SetValue(link.Model, link.Effect < 0 ? null : rebuilt[link.Effect]);
    }

    internal static void ReleaseTransientHandles(Creature creature)
    {
        foreach (var model in Models(creature))
            foreach (var field in ViewFields(model.GetType()).Where(f => typeof(Tween).IsAssignableFrom(f.FieldType)))
            {
                if (field.GetValue(model) is Tween tween && GodotObject.IsInstanceValid(tween) && tween.IsValid()) tween.Kill();
                field.SetValue(model, null);
            }
    }

    private static IEnumerable<AbstractModel> Models(Creature creature)
        => creature.Monster == null ? creature.Powers : creature.Powers.Cast<AbstractModel>().Prepend(creature.Monster);

    private static FieldInfo[] ViewFields(Type type)
    {
        if (Fields.TryGetValue(type, out var fields)) return fields;
        var result = new List<FieldInfo>();
        for (Type? cursor = type; cursor != null; cursor = cursor.BaseType)
            result.AddRange(cursor.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                .Where(f => typeof(Node).IsAssignableFrom(f.FieldType) || typeof(Tween).IsAssignableFrom(f.FieldType)));
        return Fields[type] = result.ToArray();
    }

    internal static IEnumerable<Node> Descendants(Node root)
    {
        foreach (var child in root.GetChildren())
        {
            yield return child;
            foreach (var descendant in Descendants(child)) yield return descendant;
        }
    }
}
