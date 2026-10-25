namespace Spotifly.App.Services;

/// <summary>
/// Service that manages debouncing and direction mapping for top-level mouse-wheel navigation
/// between Now Playing and Library views in the expanded Dynamic Island.
/// Prevents touchpad burst events from rapidly toggling views and ensures list scrolls are isolated.
/// </summary>
public interface IWheelNavigationService
{
    /// <summary>
    /// Gets or sets the debounce/cooldown threshold in milliseconds between top-level view switches.
    /// Default is 350ms.
    /// </summary>
    int DebounceMs { get; set; }

    /// <summary>
    /// Evaluates a mouse-wheel delta event for top-level navigation.
    /// Returns true and specifies the target state if a valid deliberate gesture occurred,
    /// or false if the event should be ignored (e.g. debounced/throttled or invalid delta).
    /// </summary>
    /// <param name="delta">WPF MouseWheel delta (positive = scroll up, negative = scroll down).</param>
    /// <param name="currentState">The current island state.</param>
    /// <param name="targetState">The target state to navigate to if successful.</param>
    /// <returns>True if the top-level view should switch; otherwise false.</returns>
    bool TryHandleTopLevelWheel(int delta, Models.IslandState currentState, out Models.IslandState targetState);

    /// <summary>
    /// Resets the internal cooldown timestamp so the next gesture can trigger immediately.
    /// </summary>
    void Reset();
}
