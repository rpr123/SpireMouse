using Godot;
using MegaCrit.Sts2.Core.Nodes;

namespace SpireMouse.Timeline;

// Keep the last rendered frame while rebuilding, then reveal the restored room.
// This is presentation only: combat snapshot restoration remains synchronous.
internal sealed class RestartPresentation
{
    private readonly CanvasLayer _layer;
    private readonly TextureRect _cover;
    private readonly ImageTexture _texture;
    private Tween? _fade;
    private static RestartPresentation? _current;
    internal static bool IsActive => _current != null;

    private RestartPresentation()
    {
        using var image = NGame.Instance!.GetViewport().GetTexture().GetImage();
        _texture = ImageTexture.CreateFromImage(image);
        _layer = new CanvasLayer { Layer = 1000, ProcessMode = Node.ProcessModeEnum.Always };
        _cover = new TextureRect { Texture = _texture, ExpandMode = TextureRect.ExpandModeEnum.IgnoreSize,
            StretchMode = TextureRect.StretchModeEnum.Scale, MouseFilter = Control.MouseFilterEnum.Stop };
        NGame.Instance.AddChild(_layer);
        _layer.AddChild(_cover);
        _cover.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
    }

    internal static RestartPresentation? Begin()
    {
        _current?.Close();
        try { return _current = new RestartPresentation(); }
        catch (Exception error) { Entry.Log.Warn($"Restart presentation unavailable: {error.Message}"); return null; }
    }

    internal void Reveal()
    {
        if (!GodotObject.IsInstanceValid(_layer)) { Close(); return; }
        _fade = _cover.CreateTween().SetIgnoreTimeScale(true);
        _fade.TweenProperty(_cover, "modulate:a", 0f, 0.16)
            .SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.InOut);
        _fade.TweenCallback(Callable.From(Close));
    }

    private void Close()
    {
        _fade?.Kill();
        if (GodotObject.IsInstanceValid(_layer))
        {
            _cover.Texture = null;
            _layer.GetParent()?.RemoveChild(_layer);
            _layer.QueueFree();
        }
        _texture.Dispose();
        if (ReferenceEquals(_current, this)) _current = null;
    }
}
