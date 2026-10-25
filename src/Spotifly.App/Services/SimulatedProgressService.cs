using System.Windows.Threading;

namespace Spotifly.App.Services;

public interface ISimulatedProgressService
{
    double Progress { get; }
    bool IsAutoSimulating { get; set; }
    event EventHandler<double>? ProgressChanged;
    void SetProgress(double value);
    void NextTrack();
    void Reset();
}

/// <summary>
/// Developer service to simulate playback progress from 0.0 to 1.0.
/// Avoids polling real Spotify position, avoids seeking, and complies strictly
/// with Phase 5 requirements by running in simulated-only mode.
/// </summary>
public sealed class SimulatedProgressService : ISimulatedProgressService
{
    private readonly DispatcherTimer _simulationTimer;
    private double _progress;
    private bool _isAutoSimulating = true;
    private readonly ILoggerService? _logger;

    public double Progress
    {
        get => _progress;
        private set
        {
            var clamped = Math.Clamp(value, 0.0, 1.0);
            if (Math.Abs(_progress - clamped) > 0.0001)
            {
                _progress = clamped;
                ProgressChanged?.Invoke(this, _progress);
            }
        }
    }

    public bool IsAutoSimulating
    {
        get => _isAutoSimulating;
        set
        {
            if (_isAutoSimulating != value)
            {
                _isAutoSimulating = value;
                if (_isAutoSimulating)
                {
                    _simulationTimer.Start();
                }
                else
                {
                    _simulationTimer.Stop();
                }
            }
        }
    }

    public event EventHandler<double>? ProgressChanged;

    public SimulatedProgressService(ILoggerService? logger = null, int stepIntervalMs = 250)
    {
        _logger = logger;
        _simulationTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(stepIntervalMs)
        };
        _simulationTimer.Tick += OnSimulationTick;
        _simulationTimer.Start();
    }

    public void SetProgress(double value)
    {
        Progress = value;
    }

    public void NextTrack()
    {
        // Resets smoothly to 0.0 when a simulated track changes
        _logger?.LogInformation("Simulated track change: progress reset to 0.0.");
        Progress = 0.0;
    }

    public void Reset()
    {
        Progress = 0.0;
    }

    private void OnSimulationTick(object? sender, EventArgs e)
    {
        if (!_isAutoSimulating) return;

        // Progress advances incrementally (e.g. ~0.005 per 250ms = full track ~50 seconds)
        double next = _progress + 0.005;
        if (next > 1.0)
        {
            NextTrack();
        }
        else
        {
            Progress = next;
        }
    }
}
