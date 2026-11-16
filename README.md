# Romeo Ops Launcher

A portable Windows x64 launcher for installing, verifying and playing the ROMEO stack for S.T.A.L.K.E.R. Anomaly, with GAMMA and optional xrRazom co-op.
<p align="center">
!<img src="https://github.com/itszuff/Romeo-Ops-Launcher/raw/main/docs/images/launcher-settings.png"/>
</p>

## Install or upgrade

Download [Romeo-Ops-Launcher.exe](https://github.com/itszuff/Romeo-Ops-Launcher/releases/latest/download/Romeo-Ops-Launcher.exe) into a folder where you want to keep the launcher. Open it, choose your game folder and archives in Settings, then open Install. The launcher requests administrator access for installation tasks. The game and mod archives are obtained separately.

Version 2.6.1 checks for updates from this repository. If you have 2.6.0 or earlier, download the new EXE once. It uses your existing RomeoOpsV2 settings. At startup the published launcher checks the latest stable GitHub release. Choose **Yes** to download, verify and restart, or **No** to keep playing. You can also select **Support → Check for updates**. The previous executable is saved beside the updated copy with a `.previous` suffix.

## Build on Windows

Install the .NET 9 SDK, then run these commands from the repository root. Close Mod Organizer and Anomaly before running the installer tests, which exercise the launcher's active-game checks.

```powershell
dotnet test tests/RomeoOps.Tests -c Release
dotnet run --project src/RomeoOps
```

## Publish a release

Set the three-part version in `src/RomeoOps/RomeoOps.csproj`, test the change, and push the release commit to the repository. Build the portable executable from that commit on Windows.

```powershell
dotnet publish src/RomeoOps -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true -p:EnableCompressionInSingleFile=true -o artifacts/win-x64
Copy-Item artifacts/win-x64/RomeoOps.exe artifacts/Romeo-Ops-Launcher.exe
```

Create a GitHub release at that commit with a tag matching the project version, such as `v2.6.1`. Attach `artifacts/Romeo-Ops-Launcher.exe` and publish it as the latest stable release. The exact filename, tag and GitHub-generated SHA-256 digest are used by the updater.

## License

The launcher source code is available under the [MIT License](LICENSE). Font licenses are included in [Fonts](src/RomeoOps/Fonts). Game and mod names, logos, artwork and third-party configuration content belong to their respective owners.
