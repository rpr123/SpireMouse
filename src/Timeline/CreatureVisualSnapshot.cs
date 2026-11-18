using Godot;
using MegaCrit.Sts2.Core.Nodes.Combat;

namespace SpireMouse.Timeline;

// Appearance belongs to the live node, not the combat model. Recreating a dead
// creature must retain its original random hue/size and subsequent size changes.
internal sealed class CreatureVisualSnapshot
{
    private readonly Vector2 _position;
    private readonly Vector2 _visualPosition;
    private readonly Vector2 _visualScale;
    private readonly Vector2 _bodyScale;
    private readonly Vector2? _formScale;
    private readonly float _defaultScale;
    private readonly float _temporaryScale;
    private readonly float _hue;
    private readonly bool _interactionAllowed;
    private readonly Dictionary<string, Vector2> _authoredScales = new();

    internal CreatureVisualSnapshot(NCreature node)
    {
        _position = node.Position;
        // A hit shake is transient and always returns to zero in the native animation.
        _visualPosition = node._shakeTween?.IsRunning() == true ? Vector2.Zero : node.Visuals.Position;
        _visualScale = node.Visuals.Scale;
        _bodyScale = node.Body.Scale;
        _formScale = node.Visuals.FormVfxHolder?.Scale;
        _defaultScale = node.Visuals.DefaultScale;
        _temporaryScale = node._tempScale;
        _hue = node.Visuals._hue;
        // Flee and reattach switch targeting off on the view itself (game v0.113).
        _interactionAllowed = node._interactionAllowed;
        foreach (var child in PersistentCreatureEffects.Descendants(node.Visuals).OfType<Node2D>())
            if (child.Owner != null && (node.Visuals.FormVfxHolder == null || !node.Visuals.FormVfxHolder.IsAncestorOf(child)))
                _authoredScales[node.Visuals.GetPathTo(child).ToString()] = child.Scale;
    }

    internal void Restore(NCreature node)
    {
        // A tween from the discarded future must not overwrite the restored size.
        node._scaleTween?.Kill();
        node._shakeTween?.Kill();
        node.Position = _position;
        node._tempScale = _temporaryScale;
        node._interactionAllowed = _interactionAllowed;
        if (!Mathf.IsEqualApprox(node.Visuals._hue, _hue))
        {
            node.Visuals.SetScaleAndHue(_defaultScale, _hue);
            // The game's setter skips shader updates for zero hue.
            if (node.Visuals.SpineBody?.GetNormalMaterial() is ShaderMaterial material)
                material.SetShaderParameter("h", _hue);
        }
        node.Visuals.DefaultScale = _defaultScale;
        node.Visuals.Position = _visualPosition;
        node.Visuals.Scale = _visualScale;
        node.Body.Scale = _bodyScale;
        foreach (var (path, scale) in _authoredScales)
            if (node.Visuals.GetNodeOrNull<Node2D>(path) is { } child) child.Scale = scale;
        if (_formScale is { } formScale && node.Visuals.FormVfxHolder is { } form) form.Scale = formScale;
        node.UpdateBounds(node.Visuals);
        node.SetOrbManagerPosition();
    }
}
