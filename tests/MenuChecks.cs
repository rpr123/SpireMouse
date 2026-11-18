using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.MainMenu;
using MegaCrit.Sts2.Core.Nodes.Screens.ModdingScreen;

namespace SpireMouse;

internal static partial class RuntimeVerification
{
    private static IEnumerable<Node> Descendants(Node node)
    {
        yield return node;
        foreach (var child in node.GetChildren()) foreach (var item in Descendants(child)) yield return item;
    }
    private static async Task VerifyModMenu()
    {
        await Until(() => ModMenuSettings.Attached, "Better Mod Menu integration attaches");
        var stack = Descendants(NGame.Instance!).OfType<NSubmenuStack>().First();
        stack.PushSubmenuType(typeof(NModdingScreen));
        await Frames(45);
        foreach (var tutorial in Descendants(NGame.Instance!.GetTree().Root).OfType<AcceptDialog>()
            .Where(d => d.Visible && d.Title.StartsWith("Better Mod Menu")))
        { tutorial.Hide(); }
        await Frames(10);
        var screen = Descendants(NGame.Instance!).OfType<NModdingScreen>().First(s => s.IsVisibleInTree());
        var rows = Descendants(screen).OfType<NModMenuRow>().ToArray();
        var ours = rows.First(r => r.Mod.manifest?.id == "SpireMouse");
        screen.OnRowSelected(ours); await Frames(10);
        var info = screen.GetNode<Control>("%ModInfoContainer");
        var button = (Button)info.FindChild("BetterModMenuConfigButton", true, false);
        Check(!button.Disabled && button.IsVisibleInTree(), "bottom Settings button is enabled and visible");
        Check(info.GetNodeOrNull("SpireMouseSettings") == null, "floating duplicate Settings button is absent");
        await Screenshot("menu-fixed");
        await Click(Center(button), MouseButton.Left);
        Check(SettingsPanel.IsOpen, "real mouse click on bottom Settings button opens Spire Mouse");
        await Screenshot("menu-settings-open");
        SettingsPanel.Close(); await Frames(5);
        var other = rows.First(r => r.Mod.manifest?.id != "SpireMouse");
        screen.OnRowSelected(other); await Frames(10);
        button.EmitSignal(Button.SignalName.Pressed);
        await Frames(5);
        Check(!SettingsPanel.IsOpen, "other mod Settings does not open Spire Mouse");
        screen.OnRowSelected(ours); await Frames(10);
        await Click(Center(button), MouseButton.Left);
        Check(SettingsPanel.IsOpen, "reselecting Spire Mouse keeps bottom Settings working");
        SettingsPanel.Close();
    }
}
