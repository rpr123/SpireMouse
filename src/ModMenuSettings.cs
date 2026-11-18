using Godot;
using HarmonyLib;
using System.Reflection.Emit;
using MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen;

namespace SpireMouse;

// Optional integration: Better Mod Menu discovers only library-owned settings.
// Connect its existing button to our standalone settings without adding a library
// dependency or changing any other mod's settings provider.
internal static class ModMenuSettings
{
    private const string Selected = "spire_mouse_selected";
    private const string Connected = "spire_mouse_connected";
    internal static bool Attached { get; private set; }
    private static ulong _nextAttempt;

    internal static void TryAttach()
    {
        if (Attached || Time.GetTicksMsec() < _nextAttempt) return;
        _nextAttempt = Time.GetTicksMsec() + 1000;
        var type = AccessTools.TypeByName("BetterModMenu.Patches.ModdingScreenInfoPanelOps");
        var method = type == null ? null : AccessTools.DeclaredMethod(type, "Refresh");
        if (method == null) return;
        new Harmony("SpireMouse.BetterModMenu").Patch(method,
            postfix: new HarmonyMethod(typeof(ModMenuSettings), nameof(AfterRefresh)),
            transpiler: new HarmonyMethod(typeof(ModMenuSettings), nameof(FixLegacyButtonPaths)));
        Attached = true;
        Entry.Log.Info("Connected standalone settings to Better Mod Menu's detail button.");
    }

    private static IEnumerable<CodeInstruction> FixLegacyButtonPaths(IEnumerable<CodeInstruction> instructions)
    {
        // BMM 1.1.1 creates the buttons inside DetailActionRow but Refresh still
        // looks directly under the action root. Only fix those two stale paths;
        // versions already using the correct paths are left untouched.
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Ldstr && instruction.operand is string name
                && name is "BetterModMenuConfigButton" or "BetterModMenuAnnotationButton")
                instruction.operand = "BetterModMenuDetailActionRow/" + name;
            yield return instruction;
        }
    }

    private static void AfterRefresh(NModdingScreen screen, object session)
    {
        string? id = AccessTools.Property(session.GetType(), "SelectedModId")?.GetValue(session) as string;
        var info = screen.GetNodeOrNull<Control>("%ModInfoContainer");
        if (info == null) return;
        info.GetNodeOrNull("SpireMouseSettings")?.QueueFree();
        var button = info.FindChild("BetterModMenuConfigButton", true, false) as Button;
        if (button == null) return;
        bool ours = string.Equals(id, "SpireMouse", StringComparison.OrdinalIgnoreCase);
        button.SetMeta(Selected, ours);
        if (!button.HasMeta(Connected))
        {
            button.Pressed += () => { if (button.GetMeta(Selected, false).AsBool()) SettingsPanel.Open(); };
            button.SetMeta(Connected, true);
        }
        if (!ours) return;
        var style = AccessTools.TypeByName("BetterModMenu.Patches.ModdingScreenVanillaStyle");
        AccessTools.Method(style, "ApplyDetailActionAvailability")?.Invoke(null, [button, true]);
        button.Disabled = false;
        button.TooltipText = "Spire Mouse — 마우스 · 단축키 · 배속 · 되돌리기 설정";
    }
}
