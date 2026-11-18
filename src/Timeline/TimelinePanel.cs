using Godot;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Rooms;

namespace SpireMouse.Timeline;

internal static class TimelinePanel
{
    private static VBoxContainer? _box;
    private static VBoxContainer? _rows;
    private static Label? _status;
    private static string _revision = "";
    private static bool _expanded;
    internal static void Refresh()
    {
        bool visible = SettingsStore.Current.ShowHistory && NCombatRoom.Instance != null && TimelineEngine.Singleplayer;
        if (_box == null || !GodotObject.IsInstanceValid(_box))
        {
            if (!visible) return;
            _box = new VBoxContainer { Name = "SpireMouseHistory", Position = new Vector2(16, 190), CustomMinimumSize = new Vector2(230, 0) };
            _box.AddThemeFontOverride("font", new SystemFont { FontNames = ["Malgun Gothic", "Segoe UI"] });
            var toggle = new Button { Text = "행동 기록 ▾" };
            toggle.Pressed += () => { _expanded = !_expanded; _revision = ""; };
            _box.AddChild(toggle);
            _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
            _box.AddChild(_status);
            var scroll = new ScrollContainer { CustomMinimumSize = new Vector2(230, 260) };
            _rows = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
            scroll.AddChild(_rows); _box.AddChild(scroll); NGame.Instance!.AddChild(_box);
        }
        _box.Visible = visible;
        if (!visible) return;
        _status!.Text = TimelineEngine.Status;
        _rows!.GetParent<Control>().Visible = _expanded;
        string revision = $"{TimelineEngine.Points.Count}:{TimelineEngine.Cursor}:{TimelineEngine.IsNavigationInProgress}:{_expanded}";
        if (revision == _revision) return;
        _revision = revision;
        foreach (var child in _rows.GetChildren()) { _rows.RemoveChild(child); child.QueueFree(); }
        for (int i = TimelineEngine.Points.Count - 1; i >= 0; i--)
        {
            int index = i;
            var button = new Button { Text = $"{(i == TimelineEngine.Cursor ? "●" : "○")} {i + 1}. {TimelineEngine.Points[i].Label}",
                Disabled = TimelineEngine.IsNavigationInProgress || i == TimelineEngine.Cursor };
            button.Pressed += () => TimelineEngine.Navigate(index);
            _rows.AddChild(button);
        }
    }
}
