using System.Reflection;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Runs;

namespace SpireMouse.Timeline;

// Preserve model identities so references held by the live turn loop remain valid.
// Only managed data is captured: never Godot objects, tasks, locks or delegates.
internal sealed class CombatSnapshot
{
    private readonly List<(object Target, FieldInfo Field, object? Value)> _fields = new();
    private readonly List<(Array Target, Array Values)> _arrays = new();
    private readonly HashSet<object> _visited = new(ReferenceEqualityComparer.Instance);
    private static readonly Dictionary<Type, FieldInfo[]> FieldCache = new();
    private readonly CreaturePresentation _presentation;

    internal CombatSnapshot()
    {
        var manager = RunManager.Instance;
        Visit(manager.DebugOnlyGetState());
        Visit(CombatManager.Instance.DebugOnlyGetState());
        Visit(CombatManager.Instance._turnState);
        Visit(CombatManager.Instance.History);
        Visit(manager.ActionQueueSet);
        Visit(manager.ActionQueueSynchronizer);
        Visit(manager.PlayerChoiceSynchronizer);
        Visit(manager.RewardsSetSynchronizer);
        _presentation = new CreaturePresentation(Visit);
    }

    private static bool Opaque(Type type)
    {
        string ns = type.Namespace ?? "";
        return type.IsPrimitive || type.IsEnum || type == typeof(string) || type == typeof(decimal)
            || type == typeof(Type) || typeof(MemberInfo).IsAssignableFrom(type)
            || typeof(Delegate).IsAssignableFrom(type) || typeof(GodotObject).IsAssignableFrom(type)
            || ns.StartsWith("System.Threading") || ns.StartsWith("System.Reflection")
            || ns.StartsWith("MegaCrit.Sts2.Core.Logging") || ns.StartsWith("MegaCrit.Sts2.Core.Multiplayer.Game")
            || ns.StartsWith("MegaCrit.Sts2.Core.Multiplayer.Transport");
    }

    private void Visit(object? value)
    {
        if (value == null) return;
        Type type = value.GetType();
        if (Opaque(type) || !_visited.Add(value)) return;
        if (value is MegaCrit.Sts2.Core.Models.AbstractModel model && !model.IsMutable) return;
        if (_visited.Count > 100000) throw new InvalidOperationException("Combat snapshot exceeded the object limit.");
        if (value is Array array)
        {
            _arrays.Add((array, (Array)array.Clone()));
            foreach (var item in array) Visit(item);
            return;
        }
        // Do not walk external services or reflection/runtime infrastructure.
        string ns = type.Namespace ?? "";
        if (type.Assembly != typeof(CombatManager).Assembly && !ns.StartsWith("System.Collections")
            && !ns.StartsWith("System.Random") && !type.IsValueType
            && value is not MegaCrit.Sts2.Core.Models.AbstractModel) return;
        if (!FieldCache.TryGetValue(type, out var fields))
        {
            var list = new List<FieldInfo>();
            for (Type? t = type; t != null; t = t.BaseType)
                list.AddRange(t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(f => !Opaque(f.FieldType)));
            // Scalars are data too; only runtime references must be omitted.
            for (Type? t = type; t != null; t = t.BaseType)
                list.AddRange(t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly)
                    .Where(f => f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType == typeof(string) || f.FieldType == typeof(decimal)));
            FieldCache[type] = fields = list.ToArray();
        }
        foreach (var field in fields)
        {
            var saved = field.GetValue(value);
            if (!type.IsValueType) _fields.Add((value, field, saved));
            Visit(saved);
        }
    }

    internal void Restore()
    {
        RestoreTransaction.Apply(this);
    }

    internal void RestoreInto(RestoreTransaction transaction)
    {
        // Exit old views while they still see their original models/subscriptions.
        CreaturePresentation.DetachCurrent();
        foreach (var entry in _fields) entry.Field.SetValue(entry.Target, entry.Value);
        foreach (var entry in _arrays) Array.Copy(entry.Values, entry.Target, entry.Values.Length);
        CombatManager.Instance._cardOrPotionEffectDepth.Clear();
        transaction.Hold();
        var state = CombatManager.Instance.DebugOnlyGetState()!;
        _presentation.Rebuild(state);
        CombatHud.Refresh(state);
        ArrowPresentation.Refresh(state);
        var changed = AccessTools.Field(typeof(CombatStateTracker), "CombatStateChanged").GetValue(CombatManager.Instance.StateTracker) as Action<CombatState>;
        changed?.Invoke(state);
    }
}