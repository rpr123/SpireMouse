using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;
using STS2QuickAnimationMode.Utils;

namespace SpireMouse;

[ModInitializer(nameof(Initialize))]
public static class Entry
{
    internal static readonly MegaCrit.Sts2.Core.Logging.Logger Log = new("SpireMouse", LogType.Generic);
    public static void Initialize()
    {
        RuntimeVerification.IsolateSavesIfRequested();
        SettingsStore.Load();
        STS2QuickAnimationMode.Main.Initialize();
        new Harmony("SpireMouse.InputAndTimeline").PatchAll(typeof(Entry).Assembly);
        ((SceneTree)Engine.GetMainLoop()).Root.CallDeferred(Node.MethodName.AddChild, new Timeline.TimelineLoop());
        Log.Info("Spire Mouse initialized: independent input journal + mouse + speed.");
    }
}
