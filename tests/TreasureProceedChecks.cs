using Godot;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Acts;
using MegaCrit.Sts2.Core.Models.Characters;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

namespace SpireMouse;
internal static partial class RuntimeVerification
{
    private static async Task VerifyTreasureProceed()
    {
        SaveManager.Instance.SetFtuesEnabled(false);
        var run = await StartTestRun(ModelDb.Character<Ironclad>(), "MOUSETREASURE");
        var coord = run.Map!.GetAllMapPoints().First(p => p.PointType == MapPointType.Treasure).coord;
        await RunManager.Instance.EnterMapCoord(coord);
        await Until(() => !NGame.Instance.Transition.InTransition && NRun.Instance?.TreasureRoom != null, "treasure room entered");
        var room = NRun.Instance!.TreasureRoom!;
        var blank = new Vector2(50, 450);
        await Click(blank, MouseButton.Right);
        Check(room._chestButton.IsEnabled && !NMapScreen.Instance!.IsOpen, "blank right click does not skip unopened chest");
        await Click(Center(room._chestButton), MouseButton.Left);
        await Until(() => room._isRelicCollectionOpen && room.ProceedButton.IsEnabled && room.ProceedButton.IsSkip,
            "unclaimed treasure native skip button enabled");
        await Screenshot("treasure-unclaimed");
        await Click(blank, MouseButton.Right);
        await Click(Center(room.ProceedButton), MouseButton.Right);
        Check(room._isRelicCollectionOpen && !NMapScreen.Instance!.IsOpen, "right click does not skip unclaimed relic even when skip enabled");
        int relics = run.Players[0].Relics.Count;
#if STABLE_GAME
        await Click(Center(room._relicCollection._holdersInUse[0]), MouseButton.Left);
#else
        await Click(Center(room._relicCollection.HoldersInUse[0]), MouseButton.Left);
#endif
        await Until(() => !room._isRelicCollectionOpen && room.ProceedButton.IsEnabled && !room.ProceedButton.IsSkip,
            "treasure claimed and proceed ready");
        await Frames(40);
        Check(run.Players[0].Relics.Count > relics, "native relic click awards treasure");
        await Screenshot("treasure-claimed");
        await Click(Center(NRun.Instance.GlobalUi.TopBar.Gold), MouseButton.Right);
        Check(!NMapScreen.Instance!.IsOpen, "top bar right click does not proceed from treasure");
        SettingsPanel.Open();
        await Click(blank, MouseButton.Right);
        Check(!NMapScreen.Instance.IsOpen, "settings blocks treasure shortcut");
        SettingsPanel.Close();
        await Click(blank, MouseButton.Right);
        await Until(() => NMapScreen.Instance.IsOpen, "claimed treasure blank right click opens map");
        Check(NMapScreen.Instance.IsOpen, "claimed treasure proceeds with blank right click");
        await Screenshot("treasure-proceeded");
    }
}
