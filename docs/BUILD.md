# Build from source

Use Windows, .NET SDK 9, and an installed copy of Slay the Spire 2. The project restores Godot.NET.Sdk 4.5.1 and references game assemblies from the installation. The beta.19 runtime checks used game v0.113.1 public-beta.

From PowerShell at the repository root, pass your game installation directory or set `STS2_INSTALL_DIR` and run `./build.ps1`.

```powershell
.\build.ps1 -GameDirectory 'D:\SteamLibrary\steamapps\common\Slay the Spire 2'
```

The script writes the production mod to `dist/SpireMouse/` with `Verify=false`. To install it manually, copy that folder to the game's `mods` directory and enable one copy of Spire Mouse in the mod menu.

```powershell
dotnet build SpireMouse.csproj -c Release -p:Verify=false '-p:Sts2InstallDir=D:\SteamLibrary\steamapps\common\Slay the Spire 2'
```

Game updates can change the private APIs this mod uses, and other mods can maintain state outside its combat history.

- [Developer verification](VERIFICATION.md)
- [Restart history](RESTART-HISTORY.md)
- [Restoration architecture](RESTORE-ARCHITECTURE.md)
- [Source provenance](PROVENANCE.md)
