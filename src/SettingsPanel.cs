using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Settings;
using MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen;
using MegaCrit.Sts2.Core.Modding;
using STS2QuickAnimationMode.Utils;
using SpireMouse.Timeline;

namespace SpireMouse;

internal static class SettingsPanel
{
    private static AcceptDialog? _dialog;
    private static Label? _status;
    private static string? _capturing;
    private static readonly Dictionary<string, Button> BindingButtons = new();
    public static bool IsOpen => _dialog != null && GodotObject.IsInstanceValid(_dialog) && _dialog.Visible;

    public static void Open()
    {
        if (IsOpen) return;
        MouseInput.CancelTarget();
        _dialog = new AcceptDialog { Title = "Spire Mouse — 올인원 설정", Exclusive = true, Unresizable = false, MinSize = new Vector2I(620, 480) };
        _dialog.GetOkButton().Text = "저장하고 닫기";
        _dialog.Confirmed += Close;
        _dialog.Canceled += Close;
        _dialog.CloseRequested += Close;
        _dialog.WindowInput += e => { if (Capture(e)) _dialog?.SetInputAsHandled(); };
        _dialog.AddChild(new BindingCaptureNode());
        _dialog.AddThemeFontOverride("font", new SystemFont { FontNames = ["Malgun Gothic", "Segoe UI"] });
        var scroll = new ScrollContainer { HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        scroll.SetAnchorsAndOffsetsPreset(Control.LayoutPreset.FullRect);
        scroll.OffsetLeft = 16; scroll.OffsetTop = 12; scroll.OffsetRight = -16; scroll.OffsetBottom = -60;
        var box = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        box.AddThemeConstantOverride("separation", 10);
        scroll.AddChild(box); _dialog.AddChild(scroll);
        Heading(box, "마우스 / 키보드 단축키");
        AddLabel(box, "버튼을 누른 뒤 새 키/마우스 버튼을 입력하세요. Esc: 변경 취소 · 중복 할당 불가");
        BindingButtons.Clear();
        foreach (var (id, label) in new[] { ("quick", "카드 사용 / 대상 확정 / 카드 선택"), ("end_turn", "E키 동작 (확정 / 진행 / 턴 종료)"), ("undo", "되돌리기"), ("redo", "다시 실행"), ("restart", "층 다시 시작"), ("cancel", "조준 취소") })
        {
            var row = new HBoxContainer();
            row.AddChild(new Label { Text = label, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
            var key = new Button { Text = SettingsStore.Current.Bindings[id].ToString(), CustomMinimumSize = new Vector2(240, 36) };
            key.Pressed += () => { _capturing = id; _status!.Text = label + " — 새 입력을 눌러주세요"; };
            BindingButtons[id] = key; row.AddChild(key); box.AddChild(row);
        }
        var reset = new Button { Text = "기본 단축키로 복원" };
        reset.Pressed += () => { _capturing = null; SettingsStore.Current.Bindings = MouseSettings.Defaults(); RefreshBindings(); SettingsStore.Save(); };
        box.AddChild(reset);
        Toggle(box, "추가 E 단축키 사용 (가운데 클릭, 기본 꺼짐)", () => SettingsStore.Current.EnableExtraE, v => SettingsStore.Current.EnableExtraE = v);
        Toggle(box, "사이드 버튼 M4 + M5 함께 누르기: 층 재시작", () => SettingsStore.Current.SideButtonRestart, v => SettingsStore.Current.SideButtonRestart = v);
        _status = AddLabel(box, "선택 중 빈 곳 우클릭: 확정 · 평소 빈 곳 더블 우클릭: 턴 종료 · 조준 중 빈 곳 우클릭: 취소");
        Heading(box, "속도");
        var mode = new OptionButton();
        mode.AddItem("끄기", 0); mode.AddItem("자동 진행 중 가속", 1); mode.AddItem("항상 가속", 2);
        mode.Select((int)SettingsStore.Current.Speed.AccelerationMode);
        mode.ItemSelected += i => { SettingsStore.Current.Speed.AccelerationMode = (SpeedAccelerationMode)i; SpeedChanged(); };
        box.AddChild(mode);
        Slider(box, "게임 배속", () => SettingsStore.Current.Speed.SpeedMultiplier, v => SettingsStore.Current.Speed.SpeedMultiplier = v, .1, 10, .1);
        Toggle(box, "카드 애니메이션 가속", () => SettingsStore.Current.Speed.CardAnimationAccelerationEnabled, v => SettingsStore.Current.Speed.CardAnimationAccelerationEnabled = v);
        Slider(box, "카드 애니메이션 배속", () => SettingsStore.Current.Speed.CardAnimationMultiplier, v => SettingsStore.Current.Speed.CardAnimationMultiplier = v, .1, 10, .1);
        Toggle(box, "카드 효과 사이 대기 가속", () => SettingsStore.Current.Speed.CardResolutionAccelerationEnabled, v => SettingsStore.Current.Speed.CardResolutionAccelerationEnabled = v);
        Slider(box, "카드 효과 배속", () => SettingsStore.Current.Speed.CardResolutionMultiplier, v => SettingsStore.Current.Speed.CardResolutionMultiplier = v, .1, 10, .1);
        Toggle(box, "전투 연출 가속", () => SettingsStore.Current.Speed.CombatPresentationAccelerationEnabled, v => SettingsStore.Current.Speed.CombatPresentationAccelerationEnabled = v);
        Slider(box, "전투 연출 배속", () => SettingsStore.Current.Speed.CombatPresentationMultiplier, v => SettingsStore.Current.Speed.CombatPresentationMultiplier = v, .1, 10, .1);
        Heading(box, "자동 진행 가속 범위");
        var speed = SettingsStore.Current.Speed;
        Toggle(box, "드로우·셔플·버리기·소멸", () => speed.AccelerateCardPileSequences, v => speed.AccelerateCardPileSequences = v);
        Toggle(box, "카드 사용 효과 처리", () => speed.AccelerateCardPlayResolution, v => speed.AccelerateCardPlayResolution = v);
        Toggle(box, "턴 전환", () => speed.AccelerateTurnTransitions, v => speed.AccelerateTurnTransitions = v);
        Toggle(box, "적 행동", () => speed.AccelerateEnemyActions, v => speed.AccelerateEnemyActions = v);
        Toggle(box, "타임라인", () => speed.AccelerateTimelineAnimations, v => speed.AccelerateTimelineAnimations = v);
        Toggle(box, "결과 화면", () => speed.AccelerateGameOverSummary, v => speed.AccelerateGameOverSummary = v);
        Toggle(box, "로딩", () => speed.AccelerateLoadingScreens, v => speed.AccelerateLoadingScreens = v);
        Toggle(box, "점진적으로 가속", () => speed.ProgressiveAccelerationEnabled, v => speed.ProgressiveAccelerationEnabled = v);
        Slider(box, "가속 시작 대기 (초)", () => speed.TimeThreshold, v => speed.TimeThreshold = v, 0, 10, .05);
        Slider(box, "가속 전환 시간 (초)", () => speed.TransitionDuration, v => speed.TransitionDuration = v, 0, 20, .05);
        Heading(box, "되돌리기");
        var count = new SpinBox { MinValue = 0, MaxValue = 10000, AllowGreater = true, Step = 1, Value = SettingsStore.Current.HistoryLimit, Prefix = "기록 수 (0: 무제한) " };
        count.ValueChanged += v => SaveUndo((int)v, SettingsStore.Current.ShowHistory, SettingsStore.Current.IncludeChoiceHistory);
        box.AddChild(count);
        Toggle(box, "행동 기록 탭 표시", () => SettingsStore.Current.ShowHistory, v => SaveUndo(SettingsStore.Current.HistoryLimit, v, SettingsStore.Current.IncludeChoiceHistory));
        Toggle(box, "카드 선택 중간 단계도 기록", () => SettingsStore.Current.IncludeChoiceHistory, v => SaveUndo(SettingsStore.Current.HistoryLimit, SettingsStore.Current.ShowHistory, v));
        AddLabel(box, "Undo/Redo·층 재시작은 싱글플레이용입니다. 설정은 자동 저장됩니다.");
        NGame.Instance!.AddChild(_dialog);
        _dialog.PopupCentered(new Vector2I(820, 820));
    }
    public static bool Capture(InputEvent e)
    {
        if (!IsOpen || _capturing == null) return false;
        if (e is InputEventKey { Pressed: true, Keycode: Key.Escape }) { _capturing = null; _status!.Text = "변경 취소"; return true; }
        var binding = InputBinding.From(e);
        if (binding == null) return true;
        if (SettingsStore.Current.Bindings.Any(p => p.Key != _capturing && p.Value == binding)) { _status!.Text = "다른 기능이 사용하는 입력입니다. 다른 버튼을 눌러주세요."; return true; }
        SettingsStore.Current.Bindings[_capturing] = binding;
        _capturing = null; RefreshBindings();
        _status!.Text = SettingsStore.Save() ? "저장됨" : "설정 저장 실패 — 로그를 확인하세요";
        return true;
    }
    private static void RefreshBindings() { foreach (var (id, button) in BindingButtons) button.Text = SettingsStore.Current.Bindings[id].ToString(); }
    internal static void Close()
    {
        if (_capturing != null) { _capturing = null; _status!.Text = "변경 취소"; return; }
        SettingsStore.Save(); _dialog?.QueueFree(); _dialog = null;
    }
    private static void SpeedChanged() { SpeedManager.OnSettingsChanged(); SettingsStore.Save(); }
    private static void SaveUndo(int limit, bool history, bool choices)
    {
        SettingsStore.Current.HistoryLimit = limit; SettingsStore.Current.ShowHistory = history; SettingsStore.Current.IncludeChoiceHistory = choices; SettingsStore.Save();
    }
    private static Label AddLabel(VBoxContainer box, string text) { var label = new Label { Text = text, AutowrapMode = TextServer.AutowrapMode.WordSmart }; box.AddChild(label); return label; }
    private static void Heading(VBoxContainer box, string text) { var l = AddLabel(box, text); l.AddThemeFontSizeOverride("font_size", 24); }
    private static void Toggle(VBoxContainer box, string label, Func<bool> get, Action<bool> set)
    {
        var button = new CheckButton { Text = label, ButtonPressed = get() };
        button.Toggled += v => { set(v); SpeedChanged(); }; box.AddChild(button);
    }
    private static void Slider(VBoxContainer box, string label, Func<float> get, Action<float> set, double min, double max, double step)
    {
        var row = new HBoxContainer(); row.AddChild(new Label { Text = label, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill });
        var value = new SpinBox { MinValue = min, MaxValue = max, Step = step, Value = get(), CustomMinimumSize = new Vector2(180, 32) };
        value.ValueChanged += v => { set((float)v); SpeedChanged(); }; row.AddChild(value); box.AddChild(row);
    }
}

internal partial class BindingCaptureNode : Node
{
    public override void _Input(InputEvent inputEvent)
    {
        if (SettingsPanel.Capture(inputEvent)) GetViewport().SetInputAsHandled();
    }
}

[HarmonyPatch(typeof(NSettingsScreen), nameof(NSettingsScreen.OnSubmenuOpened))]
internal static class SettingsEntryPatch
{
    static void Postfix(NSettingsScreen __instance)
    {
        var content = __instance.GetNode<NSettingsPanel>("%GeneralSettings").Content;
        if (content.GetNodeOrNull("SpireMouseSettings") != null) return;
        var button = new Button { Name = "SpireMouseSettings", Text = "Spire Mouse — 마우스 · 단축키 · 배속 · 되돌리기 설정", CustomMinimumSize = new Vector2(0, 48) };
        button.Pressed += SettingsPanel.Open; content.AddChild(button); content.MoveChild(button, 0);
    }
}

[HarmonyPatch(typeof(NModInfoContainer), nameof(NModInfoContainer.Fill))]
internal static class ModSettingsEntryPatch
{
    static void Postfix(NModInfoContainer __instance, Mod mod)
    {
        __instance.GetNodeOrNull("SpireMouseSettings")?.QueueFree();
        if (mod.manifest?.id != "SpireMouse" || ModMenuSettings.Attached) return;
        var button = new Button { Name = "SpireMouseSettings", Text = "Spire Mouse 설정 열기", Position = new Vector2(0, 320), CustomMinimumSize = new Vector2(400, 48) };
        button.Pressed += SettingsPanel.Open; __instance.AddChild(button);
    }
}

