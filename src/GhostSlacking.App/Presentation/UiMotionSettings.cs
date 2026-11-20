using System.Runtime.InteropServices;
using Avalonia;
using Avalonia.Threading;
using Windows.UI.ViewManagement;

namespace GhostSlacking.App;

internal sealed class UiMotionSettings : IDisposable
{
    private readonly UISettings? _system;
    private readonly Func<bool> _readEnabled;
    private readonly bool _subscribed;
    private bool _disposed;

    internal UiMotionSettings()
    {
        _readEnabled = () => _system?.AnimationsEnabled == true;
        try
        {
            _system = new UISettings();
            if (OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
            {
                _system.AnimationsEnabledChanged += OnSystemAnimationsChanged;
                _subscribed = true;
            }
        }
        catch (Exception exception) when (IsUnavailable(exception))
        {
            // The preference is also read before each interaction.
        }
    }

    internal UiMotionSettings(Func<bool> readEnabled) => _readEnabled = readEnabled;

    internal static UiMotionSettings? Current => (Application.Current as GhostSlackingApplication)?.MotionSettings;

    internal bool Enabled { get; private set; }
    internal event EventHandler? Changed;

    internal bool Refresh()
    {
        if (_disposed) return false;
        bool enabled;
        try { enabled = _readEnabled(); }
        catch (Exception exception) when (IsUnavailable(exception)) { enabled = false; }

        if (Enabled != enabled)
        {
            Enabled = enabled;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        return Enabled;
    }

    private void OnSystemAnimationsChanged(UISettings sender, object args) =>
        Dispatcher.UIThread.Post(() => Refresh(), DispatcherPriority.Input);

    private static bool IsUnavailable(Exception exception) =>
        exception is COMException or NotSupportedException or InvalidOperationException;

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        if (_subscribed && OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041))
        {
            _system!.AnimationsEnabledChanged -= OnSystemAnimationsChanged;
        }
    }
}
