# Dynamic Island for Spotify (Spotifly)

Spotifly is a Windows desktop companion for Spotify. It places a borderless Dynamic Island at the top of the screen, expands on hover, and provides Now Playing information, playback controls, playlist browsing, album artwork, and a curved playback progress indicator. It controls Spotify through the Spotify Web API and never downloads or plays audio locally.

## Features

- Compact top-of-screen island with a persistent progress arc and hover expansion.
- Now Playing artwork, title, artist, device status, live progress, play/pause, next, and previous controls.
- Playlist carousel with real Spotify covers, names, looping fan navigation, and clickable selection.
- Song carousel inside a selected playlist with artwork and playback actions.
- Minimal, Modern, Glassmorphism, and Album Cover appearance modes.
- Album Cover mode extracts a vivid color from artwork and applies it to the island accent, progress arc, halo, controls, and borders.
- Settings window, system tray controls, startup launch, single-instance enforcement, and safe hide versus exit behavior.
- DPAPI-protected tokens and sanitized rotating logs.

## Technology

- C# and WPF on .NET 8 (`net8.0-windows`)
- Windows Forms integration for the notification-area tray icon
- MVVM for Settings and preference editing, with code-behind for overlay animation and hit testing
- Spotify OAuth Authorization Code with PKCE
- xUnit tests in `tests/Spotifly.Tests`
- No client secret is embedded in the desktop application

## Project layout

| Path | Responsibility |
| --- | --- |
| `src/Spotifly.App/App.xaml.cs` | Startup, dependency wiring, single-instance and shutdown lifecycle |
| `src/Spotifly.App/Views/IslandView.xaml` | Island visual tree, controls, bindings, and carousel templates |
| `src/Spotifly.App/Views/IslandView.xaml.cs` | Overlay states, hover behavior, transitions, playback UI, and navigation |
| `src/Spotifly.App/Views/SettingsWindow.xaml` | Settings window and appearance cards |
| `src/Spotifly.App/ViewModels/SettingsViewModel.cs` | Settings, themes, Spotify account actions, and startup preference |
| `src/Spotifly.App/Controls/FanCarouselPanel.cs` | Responsive arc layout, rotation, scale, depth, and animated carousel offset |
| `src/Spotifly.App/Controls/SemicircleProgressControl.cs` | Track arc, progress arc, and glow drawing |
| `src/Spotifly.App/Controls/RemoteArtworkImage.cs` | Asynchronous playlist artwork loading and caching |
| `src/Spotifly.App/Services/SpotifyAuthService.cs` | PKCE authorization, callback listener, refresh, and secure token persistence |
| `src/Spotifly.App/Services/SpotifyApiService.cs` | Spotify HTTP API operations |
| `src/Spotifly.App/Services/SpotifyService.cs` | Playback polling, playlists, tracks, device state, and commands |
| `src/Spotifly.App/Services/IThemeService.cs` | Runtime theme switching and album palette application |
| `src/Spotifly.App/Themes/*.xaml` | Minimal, Modern, Glassmorphism, Album Cover, and shared resources |
| `src/Spotifly.App/Models` | Playback, playlist, track, settings, and state models |

## How the Dynamic Island works

`IslandView` is a transparent, topmost, borderless WPF window. `IslandBackgroundPath` draws the curved island geometry. `IslandContentContainer` clips content to the same geometry so artwork and controls stay inside the dome. The progress control is a click-through overlay rendered above content, keeping the arc visible without intercepting buttons.

The state machine includes `Hidden`, `Compact`, `Expanded`, `Library`, `PlaylistTracks`, and `SettingsOpen`. Cursor polling and activation/hide timers manage transitions. Moving away from an expanded island collapses to `Compact`, keeping the island available. Explicit X dismissal uses `Hidden`; Settings Exit and tray Exit terminate the process.

`FanCarouselPanel` measures cards against the available island size and arranges them around the center. Center cards are upright and larger; edge cards rotate, scale, and move along the fan. Its animated dependency-property offset provides smooth movement. Playlist and song panels use separate panel instances.

`SemicircleProgressControl` draws the track arc and normalized progress arc. Live Spotify progress uses `progress_ms`, `duration_ms`, and the last snapshot timestamp while playing, then updates on a UI timer. The arc and Album Cover halo share the same palette.

## Appearance modes

- **Minimal**: neutral dark island with a clean white arc.
- **Modern**: Spotify green accents and neon green glow.
- **Glassmorphism**: translucent surfaces and soft ambient contrast.
- **Album Cover**: samples saturated artwork pixels, selects a readable accent, and applies it to `IslandAccentBrush`, progress brushes, `IslandShadowEffect`, borders, hover states, and card tints. Green is used until artwork is available.

Theme dictionaries are hot-swapped by `ThemeService`, so changing appearance does not recreate the island or interrupt playback.

## Spotify authentication and API

Register this exact redirect URI in the Spotify Developer Dashboard:

```text
http://127.0.0.1:43821/callback
```

The PKCE flow creates a high-entropy verifier, opens the default browser, receives the loopback callback, exchanges the code without a client secret, encrypts access and refresh tokens with Windows DPAPI, and starts playback polling. Requested scopes cover profile information, playback state and controls, private playlists, and collaborative playlists. Reconnect after changing scopes.

Playback control requires an active Spotify device and generally a Premium account. Spotifly sends remote-control commands and does not stream audio locally. HTTP 401, 403, 429, no-device, idle, paused, and network failure states are mapped to user-facing status text with sanitized diagnostics.

## Settings, startup, and storage

Open Settings with the island gear, expanded header gear, or tray menu. Preferences are stored at `%LocalAppData%\\Spotifly\\settings.json`; secure tokens are stored separately in DPAPI-protected storage. The startup toggle uses `HKCU\\Software\\Microsoft\\Windows\\CurrentVersion\\Run` without administrator access.

The updater creates the canonical Start-menu entry **Dynamic Island**, targeting:

```text
%LocalAppData%\\Programs\\Spotifly\\Spotifly.App.exe
```

User settings and tokens remain in place during updates. Logs are written to `%LocalAppData%\\Spotifly\\Logs\\spotifly.log` with rotation and redaction of bearer values, tokens, authorization codes, and sensitive parameters.

## Build, test, run, and update

From `C:\\dynamic_island`:

```powershell
$env:DOTNET_ROOT = 'C:\\Users\\Tanmay\\AppData\\Local\\Microsoft\\dotnet'
$env:PATH = "$env:DOTNET_ROOT;$env:PATH"

dotnet restore Spotifly.sln
dotnet build Spotifly.sln -c Release
dotnet test Spotifly.sln -c Release --no-build
dotnet run --project src/Spotifly.App -c Release
```

Update the permanent installation with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\\scripts\\Update-Spotifly.ps1
```

The script publishes Release output, updates `%LocalAppData%\\Programs\\Spotifly`, refreshes the Dynamic Island shortcuts and startup registration, and preserves user data.

For a self-contained package:

```powershell
dotnet publish src/Spotifly.App/Spotifly.App.csproj -c Release -r win-x64 --self-contained true -o publish/self-contained
```

## Testing checklist

Automated tests cover PKCE and token handling, playback response mapping, progress normalization, playlist pagination, missing artwork, carousel arrangement, navigation, settings recovery, startup registration, and single-instance behavior.

Manual checks should cover:

1. Connect and disconnect Spotify through Settings.
2. Start playback on an active device and confirm artwork, title, controls, and progress.
3. Pause, resume, skip, and change tracks.
4. Open playlists, scroll the fan carousel, select a playlist, and play a song.
5. Select each appearance mode, including Album Cover with different artwork colors.
6. Hover in and out repeatedly and open Settings from the gear.
7. Restart Windows and confirm the installed Dynamic Island entry launches the current build.

## Privacy and Spotify notice

Spotifly has no telemetry, analytics, third-party trackers, or remote script execution. Do not commit credentials, tokens, settings, logs, or build output. Spotify names, logos, artwork, and API services remain the property of Spotify and their respective owners. Use of the Spotify Web API must follow the current Spotify Developer Terms and policies.
