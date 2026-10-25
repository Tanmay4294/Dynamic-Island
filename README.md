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
- [ ] **Phase 2**: Transparent Overlay Window and Semicircle Shape
- [ ] **Phase 3**: Hover Trigger, Compact Menu and Expanded/Collapsed States
- [ ] **Phase 4**: Theme System and Settings Window
- [ ] **Phase 5**: Curved Semicircular Progress Control (Simulated Data First)
- [ ] **Phase 6**: Spotify Authentication and API Connectivity Test
- [ ] **Phase 7**: Spotify Service: Now Playing, Playback and Album Art
- [ ] **Phase 8**: Spotify Playlist List with Cover Art
- [ ] **Phase 9**: Playlist Song List with Album Art
- [ ] **Phase 10**: Click a Song to Play It
- [ ] **Phase 11**: Mouse-Wheel Mode Switching and Navigation Polish
- [ ] **Phase 12**: Windows Startup, Settings Persistence and Recovery
- [ ] **Phase 13**: Testing, Accessibility, Performance and Security
- [ ] **Phase 14**: Packaging and Final Handoff
