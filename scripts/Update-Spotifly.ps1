# Spotifly Installation & Update Script
# ------------------------------------------------------------
# This script builds, tests, publishes, and installs Spotifly to a permanent location.
# It also updates shortcuts and the Windows startup registry entry.
# ------------------------------------------------------------
param(
    [switch]$SkipBuild   # Skip build & test steps (useful if you only want to copy files)
)

function Write-Log {
    param([string]$Message)
    Write-Host $Message
}

# 1. Build & Test (optional)
if (-not $SkipBuild) {
    Write-Log "Building Release configuration..."
    dotnet build -c Release
    if ($LASTEXITCODE -ne 0) { Throw "Build failed. Aborting update." }

    Write-Log "Running test suite..."
    dotnet test -c Release --no-build
    if ($LASTEXITCODE -ne 0) { Throw "Tests failed. Aborting update." }
}

# 2. Publish to a staging folder
$staging = Join-Path $env:TEMP ("Spotifly_Staging_" + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $staging -Force | Out-Null
Write-Log "Publishing to $staging ..."
# Framework‑dependent publish (requires .NET runtime on target machine)
# Use '--self-contained true' for a self‑contained build.

dotnet publish src/Spotifly.App/Spotifly.App.csproj -c Release -o $staging --self-contained false
if ($LASTEXITCODE -ne 0) { Throw "Publish failed." }

# 3. Permanent install directory
$installDir = Join-Path $env:LOCALAPPDATA "Programs\Spotifly"
New-Item -ItemType Directory -Path $installDir -Force | Out-Null

# 4. Copy binaries (overwrite existing files)
Write-Log "Copying files to $installDir ..."
Copy-Item -Path (Join-Path $staging '*') -Destination $installDir -Recurse -Force

# Verify main executable exists
$exePath = Join-Path $installDir "Spotifly.App.exe"
if (-not (Test-Path $exePath)) { Throw "Executable not found after copy: $exePath" }

# 5. Create / update shortcuts (Start Menu & Desktop)
$wsh = New-Object -ComObject WScript.Shell
# Start-Menu shortcut. The file name is the label Windows Search displays.
$startMenuDirectory = Join-Path $env:APPDATA "Microsoft\Windows\Start Menu\Programs"
$startMenu = Join-Path $startMenuDirectory "Dynamic Island.lnk"
$shortcut = $wsh.CreateShortcut($startMenu)
$shortcut.TargetPath = $exePath
$shortcut.WorkingDirectory = $installDir
$shortcut.IconLocation = $exePath
$shortcut.Description = "Dynamic Island Spotify controls"
$shortcut.Save()

# Remove stale shortcuts so Windows Search has one canonical entry, then create/update
# the desktop shortcut with the same permanent target.
Remove-Item (Join-Path $startMenuDirectory "Spotifly.lnk") -Force -ErrorAction SilentlyContinue
$desktop = Join-Path $env:USERPROFILE "Desktop\Dynamic Island.lnk"
$ds = $wsh.CreateShortcut($desktop)
$ds.TargetPath = $exePath
$ds.WorkingDirectory = $installDir
$ds.IconLocation = $exePath
$ds.Description = "Dynamic Island Spotify controls"
$ds.Save()
Remove-Item (Join-Path $env:USERPROFILE "Desktop\Spotifly.lnk") -Force -ErrorAction SilentlyContinue

# 6. Update Windows startup registry entry (per‑user)
$runKey = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Run'
$startupCommand = '"{0}"' -f $exePath
Set-ItemProperty -Path $runKey -Name 'Spotifly' -Value $startupCommand -Force
Write-Log "Startup registry entry updated to $exePath"

# 7. Clean up staging folder
Remove-Item $staging -Recurse -Force -ErrorAction SilentlyContinue
Write-Log "Staging folder removed. Update complete."
