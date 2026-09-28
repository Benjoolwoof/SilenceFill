# SilenceFill

![SilenceFill logo](assets/SilenceFill-logo.png)

SilenceFill plays your Spotify music in the quiet moments between audio from apps you choose. When a selected app makes sound, SilenceFill pauses Spotify. After that app stays quiet for a few seconds, SilenceFill resumes it.

## Download and use

Download [SilenceFill v1.0.1](https://github.com/Benjoolwoof/SilenceFill/releases/download/v1.0.1/SilenceFill-v1.0.1.exe) on Windows, then:

1. Open the Spotify desktop app and start playing music.
2. Open SilenceFill and check the apps whose sound should pause Spotify.
3. Choose how many quiet seconds must pass before music resumes (the default is four). Set 0 to resume on the first quiet audio check.

Click **Refresh open apps** if an app you just launched is missing. Closing the settings window keeps SilenceFill running in the system tray. Double-click its tray icon to reopen settings; right-click it to exit. Your app selections are saved between launches.

SilenceFill requires Windows 10 version 1809 or later and the .NET 9 Windows Desktop Runtime. It controls the Spotify desktop app. Selecting a browser applies to all tabs in that browser, and SilenceFill resumes Spotify only when it paused Spotify itself.

## Build from source

With the .NET 9 SDK installed:

```powershell
dotnet build src/SilenceFill.csproj -c Release
```

For a single executable:

```powershell
dotnet publish src/SilenceFill.csproj -c Release -r win-x64 --self-contained false -p:PublishSingleFile=true -o publish
```

The app uses Windows audio sessions to measure sound from the checked processes and Windows media controls to pause or play Spotify. It stores its settings locally in `%LOCALAPPDATA%\SilenceFill\settings.json`.

