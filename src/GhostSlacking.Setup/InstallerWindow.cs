using System.IO;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Automation.Peers;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace GhostSlacking.Setup;

internal sealed record InstallerSelection(
    string Directory, bool StartMenuShortcut, bool DesktopShortcut, bool LaunchAfterInstall)
{
    public InstallerSelection ValidateDirectory()
    {
        if (!Path.IsPathFullyQualified(Directory))
            throw new ArgumentException("请选择本机绝对安装路径。");
        var path = Path.GetFullPath(Directory);
        if (path.StartsWith(@"\\", StringComparison.Ordinal))
            throw new ArgumentException("请选择本机绝对安装路径。");
        return this with { Directory = path };
    }
}

internal partial class InstallerWindow : Window
{
    private enum IconAction { None, Start, Finish, Close }
    private IconAction _iconAction;

    public event EventHandler? StartRequested;
    public event EventHandler? FinishRequested;
    public event EventHandler? CancelRequested;
    public nint Handle => new WindowInteropHelper(this).Handle;

    public InstallerWindow(string directory)
    {
        InitializeComponent();
        InstallDirectory.Text = directory;
        SetProgress(0, "正在检查安装状态");
    }

    public void ConfigureReady(string action, bool freshInstall, bool manual, bool uninstall)
    {
        Instruction.Text = $"点击图标开始{action}";
        FreshInstallOptions.Visibility = freshInstall ? Visibility.Visible : Visibility.Collapsed;
        LaunchOption.Visibility = uninstall ? Visibility.Collapsed : Visibility.Visible;
        OptionsArea.Visibility = manual ? Visibility.Visible : Visibility.Collapsed;
        _iconAction = IconAction.Start;
        IconButton.IsEnabled = true;
        SetProgress(0, $"准备{action}，点击图标或按 Enter 开始");
        if (manual) IconButton.Focus();
    }

    public InstallerSelection ReadSelection() => new(
        InstallDirectory.Text, StartMenuOption.IsChecked == true,
        DesktopOption.IsChecked == true, LaunchOption.IsChecked == true);

    public void BeginInstall()
    {
        _iconAction = IconAction.None;
        IconButton.IsEnabled = false;
        HideOptions();
    }

    private void HideOptions()
    {
        // Changing height must not move the icon after the user clicks it.
        var top = Top;
        var left = Left;
        OptionsArea.Visibility = Visibility.Collapsed;
        UpdateLayout();
        if (double.IsFinite(top)) Top = top;
        if (double.IsFinite(left)) Left = left;
    }

    public void SetProgress(int percent, string status)
    {
        // The canvas has padding above the head: clip to the actual body bounds.
        var bounds = GhostBody.Data.Bounds;
        var height = bounds.Height * Math.Clamp(percent, 0, 100) / 100d;
        FillClip.Rect = new Rect(bounds.Left, bounds.Bottom - height, bounds.Width, height);
        AutomationProperties.SetName(IconButton, $"GhostSlacking：{status}");
        AutomationProperties.SetHelpText(IconButton, $"进度 {percent}%");
        UIElementAutomationPeer.FromElement(IconButton)?.RaiseAutomationEvent(AutomationEvents.LiveRegionChanged);
    }

    public void EnableCompletion(bool succeeded)
    {
        HideOptions();
        _iconAction = succeeded ? IconAction.Finish : IconAction.Close;
        IconButton.IsEnabled = true;
        IconButton.Focus();
    }

    public bool ShowFailure(string message, bool offerRuntimeDownload = false) =>
        MessageBox.Show(this,
            offerRuntimeDownload ? message + "\n\n是否打开微软官方下载页面？" : message,
            "GhostSlacking 安装", offerRuntimeDownload ? MessageBoxButton.YesNo : MessageBoxButton.OK,
            MessageBoxImage.Warning) == MessageBoxResult.Yes;

    public void CloseAfterRender() => Dispatcher.BeginInvoke(DispatcherPriority.ContextIdle, new Action(Close));

    private void OnIconClick(object sender, RoutedEventArgs args)
    {
        switch (_iconAction)
        {
            case IconAction.Start: StartRequested?.Invoke(this, EventArgs.Empty); break;
            case IconAction.Finish:
                _iconAction = IconAction.None;
                IconButton.IsEnabled = false;
                FinishRequested?.Invoke(this, EventArgs.Empty);
                break;
            case IconAction.Close: Close(); break;
        }
    }

    private void OnPreviewKeyDown(object sender, KeyEventArgs args)
    {
        if (args.Key == Key.Escape)
        {
            args.Handled = true;
            CancelRequested?.Invoke(this, EventArgs.Empty);
        }
        else if (args.Key == Key.Enter && args.OriginalSource == IconButton)
        {
            args.Handled = true;
            OnIconClick(IconButton, new RoutedEventArgs());
        }
        // Space uses Button's standard keyboard activation and accessibility peer.
    }

    internal RenderTargetBitmap RenderPreview(double dpi = 96)
    {
        Show();
        UpdateLayout();
        var bitmap = new RenderTargetBitmap(
            (int)Math.Ceiling(ActualWidth * dpi / 96), (int)Math.Ceiling(ActualHeight * dpi / 96),
            dpi, dpi, PixelFormats.Pbgra32);
        bitmap.Render(this);
        return bitmap;
    }

    public void SavePreview(string path, double dpi = 96)
    {
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(RenderPreview(dpi)));
        using var stream = File.Create(path);
        encoder.Save(stream);
        Close();
    }
}
