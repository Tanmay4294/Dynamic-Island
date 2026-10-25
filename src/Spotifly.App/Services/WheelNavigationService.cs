using System.Diagnostics;
using Spotifly.App.Models;

namespace Spotifly.App.Services;

/// <summary>
/// Implements direction mapping and gesture debouncing for top-level mouse-wheel navigation.
/// </summary>
public class WheelNavigationService : IWheelNavigationService
{
    private readonly Stopwatch _stopwatch = Stopwatch.StartNew();
    private long _lastSwitchTimestampMs = -10000;
    private int _debounceMs = 350;

    public int DebounceMs
    {
        get => _debounceMs;
        set => _debounceMs = Math.Max(50, value);
    }

    public WheelNavigationService(int debounceMs = 350)
    {
        _debounceMs = debounceMs;
    }

    public bool TryHandleTopLevelWheel(int delta, IslandState currentState, out IslandState targetState)
    {
        targetState = currentState;

        // Zero delta is not an action
        if (delta == 0)
        {
            return false;
        }

        // Only handle top-level transitions between Expanded (Now Playing) and Library (Playlists)
        // If in PlaylistTracks or Compact or Hidden or SettingsOpen, top-level wheel switching is not triggered
        bool canSwitch = currentState == IslandState.Expanded || currentState == IslandState.Library;
        if (!canSwitch)
        {
            return false;
        }

        long now = _stopwatch.ElapsedMilliseconds;
        if (now - _lastSwitchTimestampMs < _debounceMs)
        {
            // Debounce / throttle rapid touchpad burst events
            return false;
        }

        // Direction mapping:
        // Delta > 0: Scroll Up
        // Delta < 0: Scroll Down
        // Rule: When expanded is showing Now Playing, scrolling up switches to Library.
        //       Scrolling down switches back to Now Playing (from Library).
        if (delta > 0 && currentState == IslandState.Expanded)
        {
            targetState = IslandState.Library;
            _lastSwitchTimestampMs = now;
            return true;
        }
        else if (delta < 0 && currentState == IslandState.Library)
        {
            targetState = IslandState.Expanded;
            _lastSwitchTimestampMs = now;
            return true;
        }

        return false;
    }

    public void Reset()
    {
        _lastSwitchTimestampMs = -10000;
    }
}
