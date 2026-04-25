using System.Windows.Threading;

namespace DirHealth.Desktop.Core.Services;

public class ScanScheduler
{
    private readonly DispatcherTimer _timer = new();
    private Func<Task>? _scanAction;

    public bool IsEnabled    => _timer.IsEnabled;
    public int  IntervalHours { get; private set; }

    public ScanScheduler()
    {
        _timer.Tick += async (_, _) =>
        {
            if (_scanAction is not null)
                await _scanAction();
        };
    }

    public void Start(int intervalHours, Func<Task> scanAction)
    {
        IntervalHours   = intervalHours;
        _scanAction     = scanAction;
        _timer.Interval = TimeSpan.FromHours(intervalHours);
        _timer.Start();
    }

    public void Stop() => _timer.Stop();

    public void UpdateInterval(int intervalHours)
    {
        IntervalHours   = intervalHours;
        _timer.Interval = TimeSpan.FromHours(intervalHours);
    }
}
