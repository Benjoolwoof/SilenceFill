# SilenceFill 2.2.2

![SilenceFill logo](assets/SilenceFill-logo.png)

SilenceFill pauses Spotify when a selected Windows app produces audio, then resumes it after the selected apps stay quiet. Analytics shows **measured Spotify output**, not inferred genre or mood.

## Use

1. Download and run [SilenceFill v2.2.2 for Windows x64](https://github.com/Benjoolwoof/SilenceFill/releases/download/v2.2.2/SilenceFill-v2.2.2.exe) on Windows 10 version 1809 or newer, or Windows 11. The executable includes the .NET runtime.
2. Play music in Spotify and select the apps that should interrupt it.
3. Set the quiet delay and choose Sensitive, Balanced, or Relaxed detection.
4. Close the window to leave SilenceFill in the tray. The tray menu can open the app, pause automation, change appearance, and exit.

SilenceFill opens maximized on launch and whenever it is reopened from the tray. The first launch shows a short setup dialog. A second v2 process brings the existing window forward and maximizes it. If an older SilenceFill process is running, v2 displays a warning and does not issue playback commands until that copy exits.

## Analytics

The compass and history use normalized Windows audio peak measurements. Its default axes use everyday terms for volume and how much it changes. Axis labels and quadrants can be customized, but automatic placement cannot infer composition style, genre, or mood. Spotify's Windows media session has not reliably supplied genre data in testing. The Saint-Saëns recording is therefore shown as sound activity, not labeled “Soft & steady.”

Drag the current-song dot to save a per-song offset. The trail keeps moving relative to that correction. Choose a time period and dot update interval in Analytics. History can be exported as CSV or deleted. Chart labels and corrections are separate from listening history.

## Data and privacy

Files are stored in `%LOCALAPPDATA%\SilenceFill`. Existing settings, chart labels, history, and song corrections are read in their older formats and written in versioned formats as they change. Each write uses a temporary file and a backup. Diagnostics logs recent decisions in memory and can be exported; audio is never recorded.

## Build and test

The project targets .NET 9 and Windows 10 version 1809 or later. The published executable includes the runtime. .NET 9 remains supported through November 10, 2026; moving to .NET 10 LTS should be done after its SDK and Windows packaging are available in the build environment. Only .NET 9 SDK was available for this release.

From `src`:

```powershell
dotnet publish SilenceFill.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -p:IncludeNativeLibrariesForSelfExtract=true
```

Run the tests from `tests`:

```powershell
dotnet run --project SilenceFill.Tests.csproj
```

The `preview` project renders interface screenshots using isolated test data. Set `SILENCEFILL_DATA_DIR` to use another local data folder during development.

## Limits

Windows media controls do not reveal who pressed pause while Spotify is already paused, so SilenceFill cannot distinguish a second manual pause during its own pause. It avoids resuming when playback starts manually, when Spotify restarts or the session changes, or when audio monitoring fails. Spotify's Windows session may also be unavailable during startup or device transitions; SilenceFill waits rather than guessing. The executable is unsigned.
