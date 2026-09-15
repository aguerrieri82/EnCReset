# EnC Reset

A small Visual Studio extension that resets stale Roslyn Edit & Continue active-statement tracking after debugging stops.

## Download and install

**[Download EnCReset.vsix](https://github.com/aguerrieri82/EnCReset/releases/latest/download/EnCReset.vsix)** — no source checkout or build required.

1. Download the VSIX using the link above.
2. Close Visual Studio, double-click the downloaded file, and select the Visual Studio instance where you want it installed.
3. Complete installation and restart Visual Studio.

Requires **Visual Studio 2026 (18.x), x64**, Community, Professional, or Enterprise, with .NET Framework 4.8.

For release notes and previous downloads, see [Releases](https://github.com/aguerrieri82/EnCReset/releases). Choose **EnCReset.vsix** under Assets; the source-code ZIP and TAR archives are not installers.

## Usage

- **Automatic:** 250 ms after Visual Studio returns to design mode, the extension checks for a remaining tracking session and calls Roslyn's internal `EndTracking()` if one exists.
- **Toggle automatic reset:** select **Debug → Automatically Reset Edit & Continue Tracking**. A check mark means automatic reset is enabled (the default). Your choice is saved across Visual Studio restarts. Turning it off also cancels any pending delayed reset.
- **Manual:** select **Debug → Reset Edit & Continue Tracking**.
- **Confirmation:** after `EndTracking()` returns successfully, **Output → Debug** displays this line in bold green:

  `[EnC Reset] Successfully reset stale Edit & Continue tracking.`

No success message is written when there is no session to reset or the required internal types or method cannot be found. The extension uses the existing Debug output pane.

## Compatibility

This is a targeted workaround, not a general repair for every Hot Reload or Edit & Continue problem. It accesses Roslyn internals through reflection, so Visual Studio updates may change compatibility. Roslyn types are resolved from assemblies already loaded by Visual Studio; the project does not reference Roslyn NuGet packages.

## Build from source

Install Visual Studio 2026 with the **Visual Studio extension development** workload and the .NET Framework 4.8 targeting pack. Open `EnCReset.slnx` and build, or run this from a Visual Studio Developer PowerShell:

```powershell
msbuild EnCReset.csproj /restore /t:Rebuild /p:Configuration=Release /p:DeployExtension=false /p:VsixDeployOnDebug=false
```

The installer is generated at `bin/Release/net48/EnCReset.vsix`.