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
    private readonly Win32OverlayWindow _window;
    private readonly bool _operatingSystemSupported;
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
    private MaskKey? _lastMask;
    private bool _initialized;
    private volatile bool _failed;
    private bool _failureLogged;
    private bool _disposed;

    public RevealEdgeOverlay(ILogger logger)
    {
        _logger = logger;
        _window = new Win32OverlayWindow();
        _operatingSystemSupported = OperatingSystem.IsWindowsVersionAtLeast(10, 0, 19041);
    }

    public bool IsAvailable => _operatingSystemSupported && !_failed;

    public NativeResult Prepare(RevealVisualState visual)
    {
        if (!_operatingSystemSupported)
        {
            return NativeResult.Failed(
                "CompositionBackdropBrush",
                0,
                "Real-time Reveal feathering requires Windows 10 version 2004 or later.");
        }

        if (_failed)
        {
            return NativeResult.Failed("CompositionBackdropBrush", 0, "The compositor is unavailable for this session.");
        }

        try
        {
            InitializeComposition();
            return PrepareCore(visual);
        }
        catch (Exception exception) when (IsCompositionFailure(exception))
        {
            Disable(exception.Message);
            return NativeResult.Failed("CompositionBackdropBrush", exception.HResult, exception.Message);
        }
    }

    public NativeResult Present()
    {
        if (!_initialized || _failed || _window.Handle == 0)
        {
            return NativeResult.Failed("SetWindowPos(RevealFeather)", 0, "The Reveal compositor is unavailable.");
        }

        return _window.ShowTopmostNoActivate();
    }

    void IRevealVisualHost.Hide() => HideVisual();

    public void HideVisual()
    {
        _window.Hide();
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        HideVisual();
        ReleaseVisualResources();
        if (_root is not null)
        {
            _root.Children.RemoveAll();
            _root.Dispose();
            _root = null;
        }

        _compositionTarget?.Dispose();
        _compositionTarget = null;
        _graphicsDevice?.Dispose();
        _graphicsDevice = null;
        if (_canvasDevice is not null)
        {
            _canvasDevice.DeviceLost -= OnCanvasDeviceLost;
            _canvasDevice.Dispose();
            _canvasDevice = null;
        }

        _compositor?.Dispose();
        _compositor = null;
        try
        {
            _dispatcherQueueController?.ShutdownQueueAsync();
        }
        catch (Exception exception)
        {
            _logger.Log(LogLevel.Warning, "Could not shut down the Reveal composition queue cleanly.", exception);
        }

        _dispatcherQueueController = null;
        _window.Dispose();
        GC.SuppressFinalize(this);
    }

    private void InitializeComposition()
    {
        if (_initialized)
        {
            return;
        }

        _dispatcherQueueController = EnsureDispatcherQueue();
        _compositor = new Compositor();
        _compositionTarget = CreateCompositionTarget(_compositor, _window.Handle);
        _root = _compositor.CreateContainerVisual();
        _compositionTarget.Root = _root;
        _canvasDevice = CanvasDevice.GetSharedDevice();
        _canvasDevice.DeviceLost += OnCanvasDeviceLost;
        _graphicsDevice = CanvasComposition.CreateCompositionGraphicsDevice(_compositor, _canvasDevice);
        _initialized = true;
    }

    private NativeResult PrepareCore(RevealVisualState visual)
    {
        if (_compositor is null || _root is null || _canvasDevice is null || _graphicsDevice is null)
        {
            return NativeResult.Failed("CompositionBackdropBrush", 0, "The compositor was not initialized.");
        }

        var blurPadding = (int)MathF.Ceiling(visual.BlurAmountPx * 3F);
        var hostBounds = GetHostBounds(visual, blurPadding);
        if (hostBounds.Width <= 0 || hostBounds.Height <= 0)
        {
            HideVisual();
            return NativeResult.Failed("CompositionBackdropBrush", 0, "The Reveal feather is outside the target window.");
        }

        var coreBounds = RevealGeometry.GetBounds(visual.CoreRegion);
        coreBounds.Offset(visual.WindowBounds.Location);
        var maskKey = new MaskKey(
            hostBounds.Size,
            new Point(coreBounds.Left - hostBounds.Left, coreBounds.Top - hostBounds.Top),
            visual.CoreRegion.DiameterPx,
            visual.CoreRegion.Shape,
            visual.CoreRegion.CornerRadius,
            visual.FeatherWidthPx,
            visual.BlurAmountPx);

        if (_lastMask != maskKey)
        {
            RebuildVisuals(visual, hostBounds, maskKey);
            _lastMask = maskKey;
        }

        var boundsResult = _window.SetBounds(hostBounds);
        if (!boundsResult.Success)
        {
            return boundsResult;
        }

        return NativeResult.Ok("CompositionBackdropBrush");
    }

    private void RebuildVisuals(RevealVisualState visual, Rectangle hostBounds, MaskKey maskKey)
    {
        if (_compositor is null || _root is null || _canvasDevice is null || _graphicsDevice is null)
        {
            throw new InvalidOperationException("The compositor was not initialized.");
        }

        ReleaseVisualResources();

        var pixels = CreateMaskPixels(visual, hostBounds);
        _maskSurface = CreateMaskSurface(pixels, hostBounds.Size);

        _maskSurfaceBrush = _compositor.CreateSurfaceBrush(_maskSurface);
        _maskSurfaceBrush.Stretch = CompositionStretch.None;

        var size = new Vector2(hostBounds.Width, hostBounds.Height);
        CreateBlurLayer(size, maskKey.BlurAmountPx);

        _mistBrush = _compositor.CreateColorBrush(WinColor.FromArgb(NeutralMistAlpha, 255, 255, 255));
        _mistMaskBrush = _compositor.CreateMaskBrush();
        _mistMaskBrush.Source = _mistBrush;
        _mistMaskBrush.Mask = _maskSurfaceBrush;
        _mistVisual = _compositor.CreateSpriteVisual();
        _mistVisual.Size = size;
        _mistVisual.Brush = _mistMaskBrush;

        _root.Size = size;
        _root.Children.InsertAtTop(_mistVisual);
        var regionResult = _window.SetRingRegion(
            RevealGeometry.Expand(visual.CoreRegion, visual.FeatherWidthPx),
            visual.CoreRegion,
            visual.WindowBounds,
            hostBounds);
        if (!regionResult.Success)
        {
            throw new ExternalException(regionResult.ErrorMessage ?? regionResult.Operation, regionResult.ErrorCode);
        }
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

    private static Rectangle GetHostBounds(RevealVisualState visual, int blurPadding)
    {
        var localBounds = RevealGeometry.GetBounds(
            RevealGeometry.Expand(visual.CoreRegion, visual.FeatherWidthPx + blurPadding));
        localBounds.Offset(visual.WindowBounds.Location);
        return Rectangle.Intersect(localBounds, visual.WindowBounds);
    }

    private static byte[] CreateMaskPixels(RevealVisualState visual, Rectangle hostBounds)
    {
        var byteCount = checked(hostBounds.Width * hostBounds.Height * 4);
        var pixels = new byte[byteCount];
        for (var y = 0; y < hostBounds.Height; y++)
        {
            var localY = hostBounds.Top + y + 0.5F - visual.WindowBounds.Top;
            for (var x = 0; x < hostBounds.Width; x++)
            {
                var localX = hostBounds.Left + x + 0.5F - visual.WindowBounds.Left;
                var distance = RevealGeometry.SignedDistanceFromBoundary(visual.CoreRegion, localX, localY);
                var opacity = RevealGeometry.GetFeatherOpacity(distance, visual.FeatherWidthPx);
                if (opacity <= 0F)
                {
                    continue;
                }

                var offset = ((y * hostBounds.Width) + x) * 4;
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
        Disable("The graphics device was lost.");
    }

    private void Disable(string reason)
    {
        _failed = true;
        if (!_failureLogged)
        {
            _logger.Log(LogLevel.Warning, $"Reveal feather disabled; using hard edge: {reason}");
            _failureLogged = true;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(HideVisual);
        }
        else
        {
            HideVisual();
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

    private sealed record MaskKey(
        Size HostSize,
        Point CoreOffset,
        int CoreDiameterPx,
        RevealShape Shape,
        int CornerRadiusPx,
        int FeatherWidthPx,
        float BlurAmountPx);

}
#pragma warning restore CA1416
