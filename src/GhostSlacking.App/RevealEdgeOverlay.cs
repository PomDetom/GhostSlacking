using System.Numerics;
using System.Runtime.InteropServices;
using System.Drawing;
using Avalonia.Threading;
using GhostSlacking.Core;
using GhostSlacking.Platform;
using Microsoft.Graphics.Canvas;
using Microsoft.Graphics.Canvas.Effects;
using Microsoft.Graphics.Canvas.UI.Composition;
using WinRT;
using Windows.Graphics.DirectX;
using Windows.System;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinColor = Windows.UI.Color;

// Every Windows Composition call in this file is gated by the Windows 10
// 2004 capability check in Prepare. The host object itself must remain safe to
// construct on older Windows versions so the application can use hard-edge
// Reveal as its fallback.
#pragma warning disable CA1416

namespace GhostSlacking.App;

internal sealed class RevealEdgeOverlay : IRevealVisualHost, IDisposable
{
    private const byte NeutralMistAlpha = 10;

    private readonly ILogger _logger;
    private readonly bool _operatingSystemSupported;
    private readonly RevealRecoveryBackoff _recoveryBackoff = new();
    private Win32OverlayWindow? _window;
    private DispatcherQueueController? _dispatcherQueueController;
    private Compositor? _compositor;
    private DesktopWindowTarget? _compositionTarget;
    private ContainerVisual? _root;
    private CanvasDevice? _canvasDevice;
    private CompositionGraphicsDevice? _graphicsDevice;
    private SpriteVisual? _blurVisual;
    private SpriteVisual? _mistVisual;
    private CompositionEffectFactory? _effectFactory;
    private CompositionBackdropBrush? _backdropBrush;
    private CompositionEffectBrush? _effectBrush;
    private CompositionSurfaceBrush? _maskSurfaceBrush;
    private CompositionMaskBrush? _blurMaskBrush;
    private CompositionColorBrush? _mistBrush;
    private CompositionMaskBrush? _mistMaskBrush;
    private CompositionDrawingSurface? _maskSurface;
    private RevealMaskKey? _lastMask;
    private RevealVisualState? _lastRequestedVisual;
    private bool _initialized;
    private int _recovering;
    private int _recoveryPosted;
    private IDisposable? _recoveryRegistration;
    private bool _disposed;

    public RevealEdgeOverlay(ILogger logger)
    {
        _logger = logger;
        _operatingSystemSupported = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);
    }

    public bool IsAvailable =>
        _operatingSystemSupported &&
        !_disposed &&
        Volatile.Read(ref _recovering) == 0;

    public NativeResult Prepare(RevealVisualState visual)
    {
        if (!_operatingSystemSupported)
        {
            return NativeResult.Failed(
                "CompositionBackdropBrush",
                0,
                "Real-time Reveal feathering requires Windows 10 version 2004 or later.");
        }

        if (_disposed)
        {
            return NativeResult.Failed("CompositionBackdropBrush", 0, "The Reveal compositor was disposed.");
        }

        if (Volatile.Read(ref _recovering) != 0)
        {
            return NativeResult.Failed("CompositionBackdropBrush", 0, "The Reveal compositor is recovering.");
        }

        _lastRequestedVisual = visual;
        try
        {
            EnsureWindowForTarget(visual.TargetHwnd);
            InitializeComposition();
            var prepared = PrepareCore(visual);
            if (!prepared.Success)
            {
                BeginRecovery(prepared.ErrorMessage ?? prepared.Operation);
            }

            return prepared;
        }
        catch (Exception exception) when (IsCompositionFailure(exception))
        {
            if (_canvasDevice?.IsDeviceLost(exception.HResult) == true)
            {
                try
                {
                    _canvasDevice.RaiseDeviceLost();
                }
                catch (Exception raiseException) when (IsCompositionFailure(raiseException))
                {
                    BeginRecovery(raiseException.Message);
                }
            }

            BeginRecovery(exception.Message);
            return NativeResult.Failed("CompositionBackdropBrush", exception.HResult, exception.Message);
        }
    }

    public NativeResult Present()
    {
        var window = _window;
        if (!_initialized || Volatile.Read(ref _recovering) != 0 || window is null || window.Handle == 0)
        {
            return NativeResult.Failed("SetWindowPos(RevealFeather)", 0, "The Reveal compositor is unavailable.");
        }

        var presented = window.ShowTopmostNoActivate();
        if (!presented.Success)
        {
            BeginRecovery(presented.ErrorMessage ?? presented.Operation);
        }

        return presented;
    }

    void IRevealVisualHost.Hide() => HideVisual();

    public void HideVisual()
    {
        _window?.Hide();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _recoveryRegistration?.Dispose();
        _recoveryRegistration = null;
        HideVisual();
        ReleaseCompositionResources();
        try
        {
            _dispatcherQueueController?.ShutdownQueueAsync();
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "Could not shut down the Reveal composition queue cleanly.", exception);
        }

        _dispatcherQueueController = null;
        _window?.Dispose();
        _window = null;
        GC.SuppressFinalize(this);
    }

    private void EnsureWindowForTarget(nint targetHwnd)
    {
        if (_window is { Handle: not 0 } window && window.OwnerHwnd == targetHwnd)
        {
            return;
        }

        HideVisual();
        ReleaseCompositionResources();
        _window?.Dispose();
        _window = new Win32OverlayWindow(targetHwnd);
        _logger.Log(LogLevel.Debug, $"RevealFeatherHostCreated owner={targetHwnd}");
    }

    private void InitializeComposition()
    {
        if (_initialized)
        {
            return;
        }

        if (_window?.Handle is not nint windowHandle || windowHandle == 0)
        {
            throw new InvalidOperationException("The Reveal overlay window was not initialized.");
        }

        _dispatcherQueueController ??= EnsureDispatcherQueue();
        _compositor ??= new Compositor();
        _compositionTarget ??= CreateCompositionTarget(_compositor, windowHandle);
        _root ??= _compositor.CreateContainerVisual();
        _compositionTarget.Root = _root;
        InitializeDeviceResources();
        _initialized = true;
    }

    private void InitializeDeviceResources()
    {
        if (_compositor is null)
        {
            throw new InvalidOperationException("The compositor was not initialized.");
        }

        if (_canvasDevice is null)
        {
            _canvasDevice = CanvasDevice.GetSharedDevice();
            _canvasDevice.DeviceLost += OnCanvasDeviceLost;
        }

        if (_graphicsDevice is null)
        {
            _graphicsDevice = CanvasComposition.CreateCompositionGraphicsDevice(_compositor, _canvasDevice);
            _graphicsDevice.RenderingDeviceReplaced += OnRenderingDeviceReplaced;
        }
    }

    private NativeResult PrepareCore(RevealVisualState visual)
    {
        if (_compositor is null || _root is null || _canvasDevice is null || _graphicsDevice is null)
        {
            return NativeResult.Failed("CompositionBackdropBrush", 0, "The compositor was not initialized.");
        }

        var layout = RevealOverlayLayout.Create(visual);
        if (layout.HostBounds.Width <= 0 || layout.HostBounds.Height <= 0)
        {
            HideVisual();
            return NativeResult.Failed("CompositionBackdropBrush", 0, "The Reveal feather is outside the target window.");
        }

        if (_window is null)
        {
            return NativeResult.Failed("CompositionBackdropBrush", 0, "The Reveal overlay window was not initialized.");
        }

        var boundsResult = _window.SetBounds(layout.HostBounds);
        if (!boundsResult.Success)
        {
            return boundsResult;
        }

        _root.Size = new Vector2(layout.HostBounds.Width, layout.HostBounds.Height);
        if (_lastMask != layout.MaskKey)
        {
            RebuildVisuals(visual, layout);
            _lastMask = layout.MaskKey;
            _logger.Log(
                LogLevel.Debug,
                $"RevealMaskRebuilt size={layout.SurfaceSize.Width}x{layout.SurfaceSize.Height} " +
                $"shape={layout.MaskKey.Shape} feather={layout.MaskKey.FeatherWidthPx} blur={layout.MaskKey.BlurAmountPx:F2}");
        }

        var visualOffset = new Vector3(layout.VisualOffset.X, layout.VisualOffset.Y, 0F);
        if (_blurVisual is not null)
        {
            _blurVisual.Offset = visualOffset;
        }

        if (_mistVisual is not null)
        {
            _mistVisual.Offset = visualOffset;
        }

        var regionResult = _window.SetRingRegion(layout.RingOuterRegion, layout.RingInnerRegion);
        if (!regionResult.Success)
        {
            return regionResult;
        }

        return NativeResult.Ok("CompositionBackdropBrush");
    }

    private void RebuildVisuals(RevealVisualState visual, RevealOverlayLayout layout)
    {
        if (_compositor is null || _root is null || _canvasDevice is null || _graphicsDevice is null)
        {
            throw new InvalidOperationException("The compositor was not initialized.");
        }

        ReleaseVisualResources();

        var pixels = CreateMaskPixels(layout.TemplateCoreRegion, visual.FeatherWidthPx, layout.SurfaceSize);
        _maskSurface = CreateMaskSurface(pixels, layout.SurfaceSize);

        _maskSurfaceBrush = _compositor.CreateSurfaceBrush(_maskSurface);
        _maskSurfaceBrush.Stretch = CompositionStretch.None;

        var size = new Vector2(layout.SurfaceSize.Width, layout.SurfaceSize.Height);
        CreateBlurLayer(size, layout.MaskKey.BlurAmountPx);

        _mistBrush = _compositor.CreateColorBrush(WinColor.FromArgb(NeutralMistAlpha, 255, 255, 255));
        _mistMaskBrush = _compositor.CreateMaskBrush();
        _mistMaskBrush.Source = _mistBrush;
        _mistMaskBrush.Mask = _maskSurfaceBrush;
        _mistVisual = _compositor.CreateSpriteVisual();
        _mistVisual.Size = size;
        _mistVisual.Brush = _mistMaskBrush;

        _root.Children.InsertAtTop(_mistVisual);
    }

    private CompositionDrawingSurface CreateMaskSurface(byte[] pixels, Size size)
    {
        if (_canvasDevice is null || _graphicsDevice is null)
        {
            throw new InvalidOperationException("The graphics device was not initialized.");
        }

        var surface = _graphicsDevice.CreateDrawingSurface(
            new Windows.Foundation.Size(size.Width, size.Height),
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            DirectXAlphaMode.Premultiplied);
        using var bitmap = CanvasBitmap.CreateFromBytes(
            _canvasDevice,
            pixels,
            size.Width,
            size.Height,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            96F);
        using var drawingSession = CanvasComposition.CreateDrawingSession(surface);
        drawingSession.Clear(WinColor.FromArgb(0, 0, 0, 0));
        drawingSession.DrawImage(bitmap);
        return surface;
    }

    private void CreateBlurLayer(Vector2 size, float blurAmount)
    {
        if (_compositor is null || _maskSurfaceBrush is null)
        {
            throw new InvalidOperationException("The compositor was not initialized.");
        }

        var blur = new GaussianBlurEffect
        {
            Name = "Blur",
            BlurAmount = Math.Max(0.5F, blurAmount),
            BorderMode = EffectBorderMode.Hard,
            Optimization = EffectOptimization.Balanced,
            Source = new CompositionEffectSourceParameter("Backdrop")
        };
        var saturation = new SaturationEffect
        {
            Name = "Saturation",
            Saturation = 0.88F,
            Source = blur
        };
        _effectFactory = _compositor.CreateEffectFactory(saturation);
        _effectBrush = _effectFactory.CreateBrush();
        _backdropBrush = _compositor.CreateBackdropBrush();
        _effectBrush.SetSourceParameter("Backdrop", _backdropBrush);

        _blurMaskBrush = _compositor.CreateMaskBrush();
        _blurMaskBrush.Source = _effectBrush;
        _blurMaskBrush.Mask = _maskSurfaceBrush;

        _blurVisual = _compositor.CreateSpriteVisual();
        _blurVisual.Size = size;
        _blurVisual.Brush = _blurMaskBrush;
        _root!.Children.InsertAtTop(_blurVisual);
    }

    private void ReleaseVisualResources()
    {
        _root?.Children.RemoveAll();
        _mistVisual?.Dispose();
        _mistVisual = null;
        _blurVisual?.Dispose();
        _blurVisual = null;
        _mistMaskBrush?.Dispose();
        _mistMaskBrush = null;
        _mistBrush?.Dispose();
        _mistBrush = null;
        _blurMaskBrush?.Dispose();
        _blurMaskBrush = null;
        _maskSurfaceBrush?.Dispose();
        _maskSurfaceBrush = null;
        _effectBrush?.Dispose();
        _effectBrush = null;
        _backdropBrush?.Dispose();
        _backdropBrush = null;
        _effectFactory?.Dispose();
        _effectFactory = null;
        _maskSurface?.Dispose();
        _maskSurface = null;
        _lastMask = null;
    }

    private void ReleaseDeviceResources()
    {
        _initialized = false;
        ReleaseVisualResources();
        if (_graphicsDevice is not null)
        {
            _graphicsDevice.RenderingDeviceReplaced -= OnRenderingDeviceReplaced;
            _graphicsDevice.Dispose();
            _graphicsDevice = null;
        }

        if (_canvasDevice is not null)
        {
            _canvasDevice.DeviceLost -= OnCanvasDeviceLost;
            // GetSharedDevice returns a process-wide object. Releasing our
            // reference is sufficient; disposing it would affect other users.
            _canvasDevice = null;
        }
    }

    private void ReleaseCompositionResources()
    {
        ReleaseDeviceResources();
        if (_root is not null)
        {
            _root.Children.RemoveAll();
            _root.Dispose();
            _root = null;
        }

        _compositionTarget?.Dispose();
        _compositionTarget = null;
        _compositor?.Dispose();
        _compositor = null;
    }

    private static byte[] CreateMaskPixels(CircleRegion templateCore, int featherWidthPx, Size surfaceSize)
    {
        var byteCount = checked(surfaceSize.Width * surfaceSize.Height * 4);
        var pixels = new byte[byteCount];
        for (var y = 0; y < surfaceSize.Height; y++)
        {
            var localY = y + 0.5F;
            for (var x = 0; x < surfaceSize.Width; x++)
            {
                var localX = x + 0.5F;
                var distance = RevealGeometry.SignedDistanceFromBoundary(templateCore, localX, localY);
                var opacity = RevealGeometry.GetFeatherOpacity(distance, featherWidthPx);
                if (opacity <= 0F)
                {
                    continue;
                }

                var offset = ((y * surfaceSize.Width) + x) * 4;
                SetMaskPixel(pixels, offset, opacity);
            }
        }

        return pixels;
    }

    private static void SetMaskPixel(byte[] pixels, int offset, float value)
    {
        var alpha = ToByte(value);
        pixels[offset] = alpha;
        pixels[offset + 1] = alpha;
        pixels[offset + 2] = alpha;
        pixels[offset + 3] = alpha;
    }

    private static byte ToByte(float value) =>
        (byte)Math.Clamp((int)MathF.Round(value * byte.MaxValue), 0, byte.MaxValue);

    private void OnCanvasDeviceLost(CanvasDevice sender, object args)
    {
        BeginRecovery("The graphics device was lost.");
    }

    private void OnRenderingDeviceReplaced(
        CompositionGraphicsDevice sender,
        RenderingDeviceReplacedEventArgs args)
    {
        BeginRecovery("The composition rendering device was replaced.");
    }

    private void BeginRecovery(string reason)
    {
        if (_disposed)
        {
            return;
        }

        var firstFailure = Interlocked.Exchange(ref _recovering, 1) == 0;
        if (firstFailure)
        {
            _logger.Log(LogLevel.Warning, $"Reveal feather recovery started; using hard edge: {reason}");
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(HideVisual);
        }
        else
        {
            HideVisual();
        }

        ScheduleRecovery();
    }

    private void ScheduleRecovery()
    {
        if (Interlocked.Exchange(ref _recoveryPosted, 1) != 0)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            if (_disposed || Volatile.Read(ref _recovering) == 0)
            {
                Interlocked.Exchange(ref _recoveryPosted, 0);
                return;
            }

            var delay = _recoveryBackoff.NextDelay();
            _recoveryRegistration?.Dispose();
            _recoveryRegistration = DispatcherTimer.RunOnce(() =>
            {
                Interlocked.Exchange(ref _recoveryPosted, 0);
                TryRecover();
            }, delay, DispatcherPriority.Input);
        });
    }

    private void TryRecover()
    {
        if (_disposed || Volatile.Read(ref _recovering) == 0)
        {
            return;
        }

        HideVisual();
        try
        {
            if (_lastRequestedVisual is RevealVisualState visual)
            {
                ReleaseCompositionResources();
                EnsureWindowForTarget(visual.TargetHwnd);
                InitializeComposition();
                var prepared = PrepareCore(visual);
                if (!prepared.Success)
                {
                    ScheduleRecovery();
                    return;
                }
            }
            else
            {
                ScheduleRecovery();
                return;
            }

            _recoveryBackoff.Reset();
            _recoveryRegistration = null;
            Volatile.Write(ref _recovering, 0);
            _logger.Log(LogLevel.Info, "Reveal feather graphics resources recovered.");
        }
        catch (Exception exception) when (IsCompositionFailure(exception))
        {
            ScheduleRecovery();
        }
    }

    private static bool IsCompositionFailure(Exception exception) =>
        exception is ExternalException or ArgumentException or InvalidOperationException or NotSupportedException;

    private static DispatcherQueueController EnsureDispatcherQueue()
    {
        var options = new DispatcherQueueOptions
        {
            Size = Marshal.SizeOf<DispatcherQueueOptions>(),
            ThreadType = DispatcherQueueThreadType.Current,
            ApartmentType = DispatcherQueueApartmentType.Sta
        };
        var result = CreateDispatcherQueueController(options, out var rawController);
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }

        if (rawController == 0)
        {
            throw new ExternalException("Could not create the Composition dispatcher queue.");
        }

        try
        {
            return DispatcherQueueController.FromAbi(rawController);
        }
        finally
        {
            Marshal.Release(rawController);
        }
    }

    private static DesktopWindowTarget CreateCompositionTarget(Compositor compositor, nint hwnd)
    {
        if (!ComWrappersSupport.TryUnwrapObject(compositor, out var compositorReference))
        {
            throw new ExternalException("Could not access the native Composition object.");
        }

        var interfaceId = new Guid("29E691FA-4567-4DCA-B319-D0F207EB6807");
        using var interopReference = compositorReference.As(interfaceId);
        var vtable = Marshal.ReadIntPtr(interopReference.ThisPtr);
        var method = Marshal.ReadIntPtr(vtable, 3 * IntPtr.Size);
        var createTarget = Marshal.GetDelegateForFunctionPointer<CreateDesktopWindowTargetDelegate>(method);
        var result = createTarget(interopReference.ThisPtr, hwnd, 1, out var rawTarget);
        if (result < 0)
        {
            Marshal.ThrowExceptionForHR(result);
        }

        if (rawTarget == 0)
        {
            throw new ExternalException("Could not create the Reveal composition target.");
        }

        try
        {
            return DesktopWindowTarget.FromAbi(rawTarget);
        }
        finally
        {
            Marshal.Release(rawTarget);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int Size;
        public DispatcherQueueThreadType ThreadType;
        public DispatcherQueueApartmentType ApartmentType;
    }

    private enum DispatcherQueueThreadType
    {
        Current = 2
    }

    private enum DispatcherQueueApartmentType
    {
        Sta = 2
    }


    [UnmanagedFunctionPointer(CallingConvention.StdCall)]
    private delegate int CreateDesktopWindowTargetDelegate(
        nint compositor,
        nint hwndTarget,
        int isTopmost,
        out nint result);

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(
        DispatcherQueueOptions options,
        out nint dispatcherQueueController);

}
#pragma warning restore CA1416
