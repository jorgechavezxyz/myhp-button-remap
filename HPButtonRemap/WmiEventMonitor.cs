using System.Management;
using System.Diagnostics;

namespace HPButtonRemap;

/// <summary>
/// Monitors HP WMI events and triggers actions
/// </summary>
public class WmiEventMonitor : IDisposable
{
    private readonly Action<ButtonAction> _execute;
    private readonly Action<WmiEventMonitor>? _stoppedUnexpectedly;
    private volatile bool _active;
    private volatile bool _stopping;
    private ManagementEventWatcher? _watcher;
    private volatile ButtonAction? _action;
    private bool _disposed;

    public WmiEventMonitor(Action<ButtonAction> execute, Action<WmiEventMonitor>? stoppedUnexpectedly = null)
    {
        _execute = execute;
        _stoppedUnexpectedly = stoppedUnexpectedly;
    }

    /// <summary>
    /// Start monitoring for configured button events
    /// </summary>
    public void StartMonitoring(Config config)
    {
        Debug.WriteLine("Starting HP WMI Event Monitor...");
        StopMonitoring();
        _stopping = false;

        try
        {
            RegisterEventHandler(config);
            Debug.WriteLine($"Monitoring EventID={config.EffectiveEventID}, EventData={config.EffectiveEventData}");
        }
        catch
        {
            StopMonitoring();
            throw;
        }
    }

    internal void Activate() => _active = true;

    internal void UpdateAction(ButtonAction? action) => _action = action;

    /// <summary>
    /// Register a WMI event handler for configured button event
    /// </summary>
    private void RegisterEventHandler(Config config)
    {
        // Build WQL query to filter HP button events
        string query = $"SELECT * FROM hpqBEvnt WHERE EventID = {config.EffectiveEventID}";

        if (config.EffectiveEventData != 0)
        {
            query += $" AND EventData = {config.EffectiveEventData}";
        }

        var scope = new ManagementScope(@"root\wmi");
        var eventQuery = new WqlEventQuery(query);
        _watcher = new ManagementEventWatcher(scope, eventQuery);
        _action = config.Action;

        // Set up event handler
        _watcher.EventArrived += (sender, e) =>
        {
            OnEventArrived();
        };

        // The watcher also stops when the WMI service or HP's event provider restarts.
        // Report that so the owner can start a new subscription.
        _watcher.Stopped += (sender, e) =>
        {
            if (!_stopping)
            {
                Debug.WriteLine($"WMI watcher stopped unexpectedly: {e.Status}");
                _stoppedUnexpectedly?.Invoke(this);
            }
        };

        // Start watching
        _watcher.Start();
    }

    /// <summary>
    /// Handle WMI event arrival
    /// </summary>
    private void OnEventArrived()
    {
        try
        {
            if (_active && _action is { } action)
                _execute(action);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Event handler error: {ex.Message}");
        }
    }

    /// <summary>
    /// Stop monitoring and cleanup
    /// </summary>
    public void StopMonitoring()
    {
        _active = false;
        _stopping = true;
        Debug.WriteLine("Stopping HP WMI Event Monitor...");

        if (_watcher == null)
        {
            return;
        }

        try
        {
            _watcher.Stop();
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to stop watcher: {ex.Message}");
        }
        finally
        {
            _watcher.Dispose();
        }

        _watcher = null;
        _action = null;
        Debug.WriteLine("Monitoring stopped");
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            StopMonitoring();
            _disposed = true;
        }
    }
}
