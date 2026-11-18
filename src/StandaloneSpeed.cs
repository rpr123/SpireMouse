using System.Reflection;
using HarmonyLib;
using STS2QuickAnimationMode.Utils;

// Adapter for the MIT-licensed speed patch descriptions. No RitsuLib dependency.
namespace SpireMouse.SpeedPatching
{
    public interface IPatchMethod { }
    public record ModPatchTarget(Type Type, string Name, Type[]? Arguments = null, bool Optional = false);
}

namespace STS2QuickAnimationMode
{
    public static class Main
    {
        public static MegaCrit.Sts2.Core.Logging.Logger Logger => SpireMouse.Entry.Log;
        public static bool IsModActive { get; private set; }
        public static void Initialize()
        {
            var harmony = new Harmony("SpireMouse.Speed");
            int applied = 0;
            foreach (var type in typeof(Main).Assembly.GetTypes().Where(t => !t.IsInterface && typeof(SpireMouse.SpeedPatching.IPatchMethod).IsAssignableFrom(t)))
            {
                var targets = (SpireMouse.SpeedPatching.ModPatchTarget[])type.GetMethod("GetTargets")!.Invoke(null, null)!;
                foreach (var target in targets)
                {
                    var method = AccessTools.DeclaredMethod(target.Type, target.Name, target.Arguments);
                    if (method == null)
                    {
                        if (target.Optional) { Logger.Warn($"Optional speed target unavailable: {target.Type.Name}.{target.Name}"); continue; }
                        throw new MissingMethodException(target.Type.FullName, target.Name);
                    }
                    HarmonyMethod? Patch(string name) => type.GetMethod(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static) is { } m ? new(m) : null;
                    harmony.Patch(method, Patch("Prefix"), Patch("Postfix"), Patch("Transpiler"), Patch("Finalizer"));
                    applied++;
                }
            }
            IsModActive = true;
            SpeedManager.Initialize();
            Logger.Info($"Standalone speed: {applied} method patches applied.");
        }
    }
}

namespace STS2QuickAnimationMode.Data
{
    public static class ModDataStore
    {
        public const string SettingsKey = "settings";
        public static T Get<T>(string key) where T : class, new() => (T)(object)SpireMouse.SettingsStore.Current.Speed;
        public static void Modify<T>(string key, Action<T> modifier) where T : class, new() => modifier(Get<T>(key));
        public static void Save(string key) => SpireMouse.SettingsStore.Save();
    }
}
