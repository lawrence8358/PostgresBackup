using System.Text;
using System.Windows;
using System.Windows.Threading;

namespace PostgresBackup.Wpf.Services;

/// <summary>
/// Keeps the complete operation output while limiting UI-bound text updates to one every 225 ms.
/// </summary>
public sealed class BufferedTerminalOutput
{
    private static readonly TimeSpan PublishInterval = TimeSpan.FromMilliseconds(225);

    private readonly object _gate = new();
    private readonly StringBuilder _content = new();
    private readonly Action<string> _publish;
    private readonly Dispatcher? _dispatcher = Application.Current?.Dispatcher;
    private DateTimeOffset _lastPublishedAt = DateTimeOffset.MinValue;
    private bool _hasUnpublishedContent;
    private bool _publishScheduled;
    private long _scheduleVersion;

    public BufferedTerminalOutput(Action<string> publish)
    {
        _publish = publish;
    }

    public string Content
    {
        get
        {
            lock (_gate)
            {
                return _content.ToString();
            }
        }
    }

    public void Append(string line)
    {
        TimeSpan delay;
        long scheduleVersion;

        lock (_gate)
        {
            _content.AppendLine(line);
            _hasUnpublishedContent = true;

            if (_publishScheduled)
            {
                return;
            }

            delay = _lastPublishedAt == DateTimeOffset.MinValue
                ? TimeSpan.Zero
                : PublishInterval - (DateTimeOffset.UtcNow - _lastPublishedAt);
            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }

            _publishScheduled = true;
            scheduleVersion = ++_scheduleVersion;
        }

        if (delay == TimeSpan.Zero)
        {
            SchedulePublish(scheduleVersion);
            return;
        }

        _ = PublishAfterDelayAsync(delay, scheduleVersion);
    }

    public async Task FlushAsync()
    {
        string? snapshot;

        lock (_gate)
        {
            if (!_hasUnpublishedContent)
            {
                return;
            }

            _publishScheduled = false;
            ++_scheduleVersion;
            snapshot = _content.ToString();
            _hasUnpublishedContent = false;
            _lastPublishedAt = DateTimeOffset.UtcNow;
        }

        await PublishOnDispatcherAsync(snapshot);
    }

    public void Clear()
    {
        lock (_gate)
        {
            _content.Clear();
            _hasUnpublishedContent = false;
            _publishScheduled = false;
            ++_scheduleVersion;
            _lastPublishedAt = DateTimeOffset.UtcNow;
        }

        _publish(string.Empty);
    }

    private async Task PublishAfterDelayAsync(TimeSpan delay, long scheduleVersion)
    {
        await Task.Delay(delay).ConfigureAwait(false);
        SchedulePublish(scheduleVersion);
    }

    private void SchedulePublish(long scheduleVersion)
    {
        if (_dispatcher is null)
        {
            PublishScheduled(scheduleVersion);
            return;
        }

        try
        {
            _ = _dispatcher.BeginInvoke(
                () => PublishScheduled(scheduleVersion),
                DispatcherPriority.Background);
        }
        catch (InvalidOperationException)
        {
            // The application is closing; there is no remaining UI to update.
        }
    }

    private async Task PublishOnDispatcherAsync(string snapshot)
    {
        if (_dispatcher is null || _dispatcher.CheckAccess())
        {
            _publish(snapshot);
            return;
        }

        await _dispatcher.InvokeAsync(
            () => _publish(snapshot),
            DispatcherPriority.Background).Task;
    }

    private void PublishScheduled(long scheduleVersion)
    {
        string? snapshot;

        lock (_gate)
        {
            if (!_publishScheduled || scheduleVersion != _scheduleVersion || !_hasUnpublishedContent)
            {
                return;
            }

            _publishScheduled = false;
            _hasUnpublishedContent = false;
            _lastPublishedAt = DateTimeOffset.UtcNow;
            snapshot = _content.ToString();
        }

        _publish(snapshot);
    }
}
