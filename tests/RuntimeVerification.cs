using System.Text.Json;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Test;
using STS2QuickAnimationMode.Utils;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Models.Cards;
using MegaCrit.Sts2.Core.Models.Encounters;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Cards.Holders;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using SpireMouse.Timeline;

namespace SpireMouse;

// Dormant in normal launches. Explicit developer verification uses a memory-only
// save store: no runs, achievements or progression are written to the user's files.
internal static partial class RuntimeVerification
{
    internal static string? DirectoryPath => System.Environment.GetEnvironmentVariable("SPIRE_MOUSE_VERIFY");
    private static bool _ran;
    internal static readonly List<string> Results = new();
    internal static void IsolateSavesIfRequested()
    {
        if (DirectoryPath == null) return;
        var save = new SaveManager(new MockGodotFileIo("user://spire-mouse-verification"), true);
        save.InitSettingsDataForTest();
        save.SettingsSave.ModSettings = SaveManager.Instance.SettingsSave.ModSettings;
        save.SettingsSave.SeenEaDisclaimer = true;
        save.SettingsSave.Fullscreen = false;
        save.SettingsSave.WindowSize = new Vector2I(1280, 900);
        SaveManager.MockInstanceForTesting(save);
        Directory.CreateDirectory(DirectoryPath);
    }
    // New singleplayer run with the standard three acts. v0.113 takes a prepared
    // player and a custom-game flag; the stable branch (v0.111) takes the character.
    internal static Task<MegaCrit.Sts2.Core.Runs.RunState> StartTestRun(MegaCrit.Sts2.Core.Models.CharacterModel character, string seed)
    {
        MegaCrit.Sts2.Core.Models.ActModel[] acts = [ModelDb.Act<Overgrowth>(), ModelDb.Act<Hive>(), ModelDb.Act<Glory>()];
#if STABLE_GAME
        return NGame.Instance!.StartNewSingleplayerRun(character, true, acts, [], seed, GameMode.Standard);
#else
        return NGame.Instance!.StartNewSingleplayerRun(MegaCrit.Sts2.Core.Entities.Players.Player.CreateForNewRun(character,
            SaveManager.Instance.GenerateUnlockStateFromProgress(), 1uL), true, acts, [], seed, GameMode.Standard, false);
#endif
    }
    internal static void Check(bool ok, string name)
    {
        if (!ok) throw new InvalidOperationException("Verification failed: " + name);
        Results.Add(name); Entry.Log.Info("VERIFY PASS: " + name);
    }
    internal static async Task Frames(int count = 3)
    {
        for (int i = 0; i < count; i++) await NGame.Instance!.ToSignal(NGame.Instance.GetTree(), SceneTree.SignalName.ProcessFrame);
    }
    internal static async Task Screenshot(string name)
    {
        await Frames(5);
        await NGame.Instance!.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        NGame.Instance.GetViewport().GetTexture().GetImage().SavePng(Path.Combine(DirectoryPath!, name + ".png"));
    }
    internal static async Task Until(Func<bool> condition, string label, int frames = 1200)
    {
        for (int i = 0; i < frames; i++) { if (condition()) return; await Frames(1); }
        await Screenshot("timeout");
        throw new TimeoutException(label);
    }
    private static async Task Click(Vector2 position, MouseButton button)
    {
        position.Y = Math.Min(position.Y, NGame.Instance!.GetViewport().GetVisibleRect().Size.Y - 70);
        Entry.Log.Info($"VERIFY CLICK logical={position} viewport={NGame.Instance!.GetViewport().GetVisibleRect()} transform={NGame.Instance.GetViewport().GetScreenTransform()}");
        NGame.Instance.GetViewport().WarpMouse(position);
        NGame.Instance.GetViewport().PushInput(new InputEventMouseMotion { Position = position, GlobalPosition = position }, true);
        await Frames(4);
        Entry.Log.Info($"VERIFY HOVER {NGame.Instance.GetViewport().GuiGetHoveredControl()?.GetPath()} mouse={NGame.Instance.GetViewport().GetMousePosition()}");
        NGame.Instance.GetViewport().PushInput(new InputEventMouseButton { Position = position, GlobalPosition = position, ButtonIndex = button, Pressed = true }, true);
        await Frames(2);
        NGame.Instance.GetViewport().PushInput(new InputEventMouseButton { Position = position, GlobalPosition = position, ButtonIndex = button, Pressed = false }, true);
        await Frames(4);
    }
    private static Vector2 Center(Control c) => c.GetGlobalTransform() * (c.Size / 2);
    private static async Task DragCard(NHandCardHolder holder, Vector2 destination)
    {
        var viewport = NGame.Instance!.GetViewport();
        var start = Center(holder.Hitbox);
        start.Y = Math.Min(start.Y, viewport.GetVisibleRect().Size.Y - 70);
        viewport.WarpMouse(start);
        viewport.PushInput(new InputEventMouseMotion { Position = start, GlobalPosition = start }, true);
        await Frames(4);
        viewport.PushInput(new InputEventMouseButton { Position = start, GlobalPosition = start, ButtonIndex = MouseButton.Right, Pressed = true }, true);
        await Frames(3);
        Check(MouseInput.PendingHolder == holder, "right button hold begins targeting");
        viewport.WarpMouse(destination);
        viewport.PushInput(new InputEventMouseMotion { Position = destination, GlobalPosition = destination, ButtonMask = MouseButtonMask.Right }, true);
        await Frames(6);
        viewport.PushInput(new InputEventMouseButton { Position = destination, GlobalPosition = destination, ButtonIndex = MouseButton.Right, Pressed = false }, true);
        await Frames(6);
    }
}
