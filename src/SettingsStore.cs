using System.Text.Json;
using Godot;
using STS2QuickAnimationMode.Utils;

namespace SpireMouse;

public sealed record InputBinding
{
    public long Mouse { get; set; }
    public long KeyCode { get; set; }
    public bool Ctrl { get; set; }
    public bool Alt { get; set; }
    public bool Shift { get; set; }
    public static InputBinding Button(MouseButton button) => new() { Mouse = (long)button };
    public static InputBinding Keyboard(Key key) => new() { KeyCode = (long)key };
    public static InputBinding? From(InputEvent e) => e switch
    {
        InputEventMouseButton m when m.Pressed && m.ButtonIndex != MouseButton.None => new() { Mouse = (long)m.ButtonIndex, Ctrl = m.CtrlPressed, Alt = m.AltPressed, Shift = m.ShiftPressed },
        InputEventKey k when k.Pressed && !k.Echo && k.Keycode is not (Key.Shift or Key.Ctrl or Key.Alt or Key.Meta or Key.None) => new() { KeyCode = (long)k.Keycode, Ctrl = k.CtrlPressed, Alt = k.AltPressed, Shift = k.ShiftPressed },
        _ => null
    };
    public bool Matches(InputEvent e) => From(e) is { } b && this == b;
    public override string ToString()
    {
        var name = Mouse != 0 ? (MouseButton)Mouse switch
        {
            MouseButton.Left => "마우스 왼쪽", MouseButton.Right => "마우스 오른쪽",
            MouseButton.Middle => "가운데 버튼", MouseButton.Xbutton1 => "사이드 뒤로 (M4)",
            MouseButton.Xbutton2 => "사이드 앞으로 (M5)", _ => $"마우스 {(MouseButton)Mouse}"
        } : OS.GetKeycodeString((Key)KeyCode);
        return (Ctrl ? "Ctrl + " : "") + (Alt ? "Alt + " : "") + (Shift ? "Shift + " : "") + name;
    }
}

public sealed class MouseSettings
{
    public int HistoryLimit { get; set; } = 100;
    public bool ShowHistory { get; set; } = false;
    public bool IncludeChoiceHistory { get; set; } = true;
    public bool EnableExtraE { get; set; }
    public bool SideButtonRestart { get; set; } = true;
    public Dictionary<string, InputBinding> Bindings { get; set; } = Defaults();
    public SpeedSettings Speed { get; set; } = new() { SpeedMultiplier = 3f };
    public static Dictionary<string, InputBinding> Defaults() => new()
    {
        ["quick"] = InputBinding.Button(MouseButton.Right),
        ["end_turn"] = InputBinding.Button(MouseButton.Middle),
        ["undo"] = InputBinding.Button(MouseButton.Xbutton1),
        ["redo"] = InputBinding.Button(MouseButton.Xbutton2),
        ["restart"] = InputBinding.Keyboard(Key.F5),
        ["cancel"] = InputBinding.Keyboard(Key.Escape)
    };
}

public static class SettingsStore
{
    public static MouseSettings Current { get; private set; } = new();
    public static string PathName => Path.Combine(RuntimeVerification.DirectoryPath ?? OS.GetUserDataDir(), "mod_configs", "SpireMouse.json");
    public static void Load()
    {
        try
        {
            if (File.Exists(PathName)) Current = JsonSerializer.Deserialize<MouseSettings>(File.ReadAllText(PathName)) ?? new();
            Current.Bindings ??= MouseSettings.Defaults();
            foreach (var item in MouseSettings.Defaults()) Current.Bindings.TryAdd(item.Key, item.Value);
            Current.Speed ??= new();
        }
        catch (Exception e) { Entry.Log.Warn($"Settings load failed: {e.Message}"); Current = new(); }
    }
    public static bool Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(PathName)!);
            var temp = PathName + ".tmp";
            File.WriteAllText(temp, JsonSerializer.Serialize(Current, new JsonSerializerOptions { WriteIndented = true }));
            File.Move(temp, PathName, true);
            return true;
        }
        catch (Exception e) { Entry.Log.Error($"Settings save failed: {e}"); return false; }
    }
    public static bool Is(string action, InputEvent e) => Current.Bindings.TryGetValue(action, out var binding) && binding.Matches(e);
}
