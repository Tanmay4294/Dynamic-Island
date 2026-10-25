# Spotifly (Dynamic-Island)

Spotifly is a standalone Windows desktop application that displays a small, hover-activated semicircular "Dynamic Island" near the top-centre of the screen, providing Spotify controls and library browsing.

## Architecture & Technology Stack
- **Target Framework**: .NET 8.0 LTS (`net8.0-windows`)
- **Presentation**: C# / WPF + Windows Desktop APIs
- **Design Pattern**: MVVM (Model-View-ViewModel)
- **Lifecycle & Execution**: Background service with single-instance named Mutex guard and explicit tray/settings exit
- **Logging**: Rotating thread-safe structured file logging with automated credential/token scrubbing
- **Tests**: xUnit test suite (`Spotifly.Tests`)

## Build & Run Commands
Build the entire solution:
```powershell
dotnet build Spotifly.sln
```

Run test suite:
```powershell
dotnet test Spotifly.sln
```

Run the application:
```powershell
dotnet run --project src/Spotifly.App
```

## Implementation Phases
- [x] **Phase 0**: Repository and Environment Audit (Resolved .NET 8 LTS SDK environment and repository setup)
- [x] **Phase 1**: Minimal WPF Shell and Single-Instance Behaviour
- [x] **Phase 2**: Transparent Overlay Window and Semicircle Shape
- [x] **Phase 3**: Hover Trigger, Compact Menu and Expanded/Collapsed States
- [x] **Phase 4**: Theme System and Settings Window
- [x] **Phase 5**: Curved Semicircular Progress Control (Simulated Data First)
- [x] **Phase 6**: Spotify Authentication and API Connectivity Test
- [x] **Phase 7**: Spotify Service: Now Playing, Playback and Album Art
- [x] **Phase 8**: Spotify Playlist List with Cover Art
- [x] **Phase 9**: Playlist Song List with Album Art
- [x] **Phase 10**: Click a Song to Play It

## Phase 10: Song Playback & UX Behavior
- **Trigger**: Deliberate click on any track in the expanded playlist song list.
- **Player API**: Sends `PUT https://api.spotify.com/v1/me/player/play` with the track's Spotify URI and parent playlist context URI. Never streams or downloads audio within Spotifly.
- **Duplicate Prevention**: Immediate visual pending state on the clicked track row, temporary interaction lock, and automatic UI state restoration upon completion, error, or cancellation.
- **Error & Edge Case Handling**:
  - `401 Unauthorized`: Silent token refresh and one automatic retry; prompts reconnection if refresh fails.
  - `403 Forbidden`: Displays clear notification indicating Premium requirement or permission restriction.
  - `404 Not Found` / No Active Device: Notifies user to launch or activate playback on a Spotify device.
  - `429 Too Many Requests`: Honors `Retry-After` header and prevents spamming.
- **UX Decision (Playlist View Retention)**: Spotifly deliberately keeps the user in the playlist song list upon starting playback rather than auto-switching back to Now Playing. This allows users to browse, explore, and queue subsequent tracks uninterrupted.
- **Prerequisites for Manual Verification**: An active Spotify Premium subscription and an active Spotify player device (e.g., Spotify desktop or mobile app running with an active session).
- [x] **Phase 11**: Mouse-Wheel Mode Switching and Navigation Polish

## Phase 11: Mouse-Wheel Navigation & List Isolation
- **Top-Level Navigation**:
  - While viewing expanded **Now Playing**, scrolling up (`delta > 0`) switches to the **Playlists Library**.
  - While viewing the **Playlists Library**, scrolling down (`delta < 0`) switches back to **Now Playing**.
  - Direction mapping and debouncing/throttling (350ms window) prevent rapid touchpad gesture bursts from jittering or toggling views repeatedly.
- **List Scroll Isolation**:
  - Scrolling inside the playlist list (`PlaylistsScrollViewer`) or track list (`TracksScrollViewer`) handles scrolling directly on the list and stops event propagation (`e.Handled = true`), preventing accidental view flips.
  - Normal list scrolling is never throttled or debounced.
- **Navigation Controls & Polish**:
  - Retained the song list **Back** button to return to Playlists with scroll position preserved.
  - Preserved the compact middle **Spotifly** control to reliably open the expanded Now Playing view without altering compact menu structure.
  - Added subtle, theme-aligned visual hint badge (`NavigationHintBadge`) indicating the current view and available wheel direction without cluttering the island.
  - Ensured no autoplay occurs upon hover, focus, or selection.
- [x] **Phase 12**: Windows Startup, Settings Persistence and Recovery

## Phase 12: Startup, Recovery & Lifecycle Hardening
- **Windows Startup**:
  - Unpackaged, non-elevated per-user registration via `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`.
  - Idempotent configuration: repeated enable/disable calls never produce duplicate registry values or launch loops.
  - Windows startup toggle in Settings actively queries and mirrors the underlying Windows registry configuration.
- **Settings Persistence & Resilient Recovery**:
  - Stores all preferences (`theme`, `activationDelayMs`, `hideDelayMs`, `horizontalOffset`, `verticalOffset`, `launchAtSignIn`) in `%LocalAppData%\Spotifly\settings.json`.
  - Writes updates atomically via `.tmp` swap to prevent partial writes during sudden power-off.
  - Validates and clamps all numeric ranges upon load; if JSON is malformed or corrupted, logs the issue, restores safe defaults without crashing, and saves a repaired file.
- **Offline & Disconnected State**:
  - Spotifly UI, controls, animations, and settings operate gracefully offline without Spotify connection.
  - When disconnected or without internet, displays non-blocking status messages ("Spotify is idle", "Spotify not connected", "Network connection to Spotify failed"). Never crashes or hangs UI threads.
- **Single-Instance & Clean Exit**:
  - Protected by named system `Mutex` and `EventWaitHandle`; duplicate launches signal the existing instance to pop up a friendly tray balloon notification, then immediately terminate cleanly.
  - Explicit **Exit** actions in Settings and system tray execute full cooperative shutdown: cancels background tokens, flushes settings, closes overlay, and disposes mutex and HTTP clients cleanly.
  - The compact **X** button continues to dismiss/hide the island as designed and does not terminate the app.
- [ ] **Phase 13**: Testing, Accessibility, Performance and Security
- [ ] **Phase 14**: Packaging and Final Handoff

## Spotify Developer Dashboard Configuration (Phase 6)
To enable real Spotify authentication with Spotifly:
1. Open the [Spotify Developer Dashboard](https://developer.spotify.com/dashboard) and select your application: `Spotifly Windows dynamic island`.
2. Go to **Settings** (`Basic Information` / `Redirect URIs`).
3. Add the exact loopback redirect URI:
   ```text
   http://127.0.0.1:43821/callback
   ```
   *(Note: Spotify requires explicit `127.0.0.1`; `localhost` is strictly disallowed by Spotify).*
4. In **User Management** (while in Development Mode):
   - Add your Spotify account email to the **Users Allowlist**.
   - Development Mode allows up to 5 authorized users, and the app creator must have an active Spotify Premium account.
5. Save the changes in the dashboard.
6. In Spotifly Settings, click **Connect Spotify** (or **Reconnect**). Your default browser will open to Spotify's authorization page. Click **Agree**; Spotify will redirect to the local loopback server, exchange the PKCE code for tokens with playback and playlist scopes (`user-read-playback-state user-modify-playback-state playlist-read-private playlist-read-collaborative`), encrypt them via Windows DPAPI, and immediately verify connectivity.


> [!IMPORTANT]
> **Mandatory Policy Checkpoint before Phase 7 / Live Playback Binding**:
> 1. In accordance with the **Spotify Developer Policy** and Spotify Developer Terms of Service, developers must strictly adhere to commercial and content display guidelines.
> 2. Specific attention must be paid to restrictions against synchronizing sound recordings or Spotify content with external visual media, creating audio visualizations without explicit authorization, or modifying/deriving visual representations of Spotify-streamed audio.
> 3. Until real-time playback position synchronization is thoroughly verified and vetted against current Spotify Developer terms, the progress arc remains decoupled from live Spotify position polling and seeking, operating in simulated mode.
> 4. If developer authorization or policy compliance is unclear prior to Phase 7, the issue must be explicitly flagged for user review and confirmed before connecting live playback position APIs.

