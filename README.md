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
- [ ] **Phase 9**: Playlist Song List with Album Art
- [ ] **Phase 10**: Click a Song to Play It
- [ ] **Phase 11**: Mouse-Wheel Mode Switching and Navigation Polish
- [ ] **Phase 12**: Windows Startup, Settings Persistence and Recovery
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

