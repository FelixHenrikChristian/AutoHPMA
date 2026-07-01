using System.Globalization;
using System.Runtime.InteropServices;
using AutoHPMA.Capture.Models;
using AutoHPMA.Capture.Native;
using AutoHPMA.Contracts.Services;
using AutoHPMA.Core.Contracts.Services;
using AutoHPMA.Core.Models;
using AutoHPMA.Models;
using OpenCvSharp;

namespace AutoHPMA.Tasks;

public sealed class AutomationTaskContext
{
    private const double BaseGameWidth = 1280d;

    public AutomationTaskContext(
        IAutomationRuntimeService runtime,
        IOverlayWindowService overlay,
        IWindowInteractionService windowInteraction,
        ITemplateMatchingService templateMatching,
        GameWindowTarget target,
        AutomationRuntimeOptions? runtimeOptions)
    {
        Runtime = runtime;
        Overlay = overlay;
        WindowInteraction = windowInteraction;
        TemplateMatching = templateMatching;
        Target = target;
        RuntimeOptions = runtimeOptions;

        RefreshGeometry();
    }

    public IAutomationRuntimeService Runtime { get; }

    public IOverlayWindowService Overlay { get; }

    public IWindowInteractionService WindowInteraction { get; }

    public ITemplateMatchingService TemplateMatching { get; }

    public GameWindowTarget Target { get; }

    public AutomationRuntimeOptions? RuntimeOptions { get; }

    public IntPtr DisplayHwnd => Target.DisplayWindow.Handle;

    public IntPtr GameHwnd => Target.GameWindow.Handle;

    public int OffsetX { get; private set; }

    public int OffsetY { get; private set; }

    public double CoordinateScale { get; private set; } = 1d;

    public void RefreshGeometry()
    {
        if (!NativeMethods.GetWindowRect(DisplayHwnd, out var displayRect))
        {
            throw new InvalidOperationException("Unable to read display window bounds.");
        }

        if (!NativeMethods.GetWindowRect(GameHwnd, out var gameRect))
        {
            throw new InvalidOperationException("Unable to read game window bounds.");
        }

        OffsetX = gameRect.Left - displayRect.Left;
        OffsetY = gameRect.Top - displayRect.Top;
        CoordinateScale = Math.Max(gameRect.Width / BaseGameWidth, 0.001d);
    }

    public Mat CaptureBgrMat(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();

        var frame = Runtime.TryGetFrame()
            ?? throw new InvalidOperationException("No captured frame is available. Start AutoHPMA first.");

        using var bgra = CreateBgraMat(frame);
        var bgr = new Mat();
        Cv2.CvtColor(bgra, bgr, ColorConversionCodes.BGRA2BGR);

        RefreshGeometry();
        if (Math.Abs(CoordinateScale - 1d) < 0.001d)
        {
            return bgr;
        }

        var resizedWidth = Math.Max(1, (int)Math.Round(bgr.Width / CoordinateScale));
        var resizedHeight = Math.Max(1, (int)Math.Round(bgr.Height / CoordinateScale));
        var resized = new Mat();
        Cv2.Resize(bgr, resized, new Size(resizedWidth, resizedHeight));
        bgr.Dispose();
        return resized;
    }

    public TemplateSearchResult SearchCurrentFrame(
        Mat template,
        TemplateSearchOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        using var source = CaptureBgrMat(cancellationToken);
        return TemplateMatching.Search(source, template, options);
    }

    public async Task ClickCanonicalAsync(
        int x,
        int y,
        CancellationToken cancellationToken = default)
    {
        RefreshGeometry();
        var targetX = (int)Math.Round((x * CoordinateScale) - OffsetX);
        var targetY = (int)Math.Round((y * CoordinateScale) - OffsetY);

        await WindowInteraction.ExecuteAsync(
            GameHwnd,
            new MouseActionOptions
            {
                ActionType = MouseActionType.Click,
                X = Math.Max(0, targetX),
                Y = Math.Max(0, targetY),
            },
            cancellationToken);
    }

    public Task ClickMatchCenterAsync(
        TemplateMatchRegion region,
        CancellationToken cancellationToken = default)
    {
        var centerX = region.X + (region.Width / 2);
        var centerY = region.Y + (region.Height / 2);
        return ClickCanonicalAsync(centerX, centerY, cancellationToken);
    }

    public async Task DragCanonicalAsync(
        int startX,
        int startY,
        int endX,
        int endY,
        int durationMilliseconds = 500,
        CancellationToken cancellationToken = default)
    {
        RefreshGeometry();

        await WindowInteraction.ExecuteAsync(
            GameHwnd,
            new MouseActionOptions
            {
                ActionType = MouseActionType.Drag,
                X = Math.Max(0, (int)Math.Round((startX * CoordinateScale) - OffsetX)),
                Y = Math.Max(0, (int)Math.Round((startY * CoordinateScale) - OffsetY)),
                EndX = Math.Max(0, (int)Math.Round((endX * CoordinateScale) - OffsetX)),
                EndY = Math.Max(0, (int)Math.Round((endY * CoordinateScale) - OffsetY)),
                DurationMilliseconds = durationMilliseconds,
            },
            cancellationToken);
    }

    public Task SendKeyAsync(int virtualKey, CancellationToken cancellationToken = default) =>
        WindowInteraction.SendKeyAsync(GameHwnd, virtualKey, cancellationToken);

    public Task SendEscapeAsync(CancellationToken cancellationToken = default) =>
        SendKeyAsync(0x1B, cancellationToken);

    public Task SendEnterAsync(CancellationToken cancellationToken = default) =>
        SendKeyAsync(0x0D, cancellationToken);

    public Task SendSpaceAsync(CancellationToken cancellationToken = default) =>
        SendKeyAsync(0x20, cancellationToken);

    public OverlayRegion ToOverlayRegion(
        TemplateMatchRegion region,
        string? name = null,
        string? statusText = null,
        OverlayRegionStatusKind statusKind = OverlayRegionStatusKind.Inline,
        OverlayRegionKind kind = OverlayRegionKind.Default)
    {
        RefreshGeometry();
        var scoreText = FormatMatchScore(region.Score);
        return new OverlayRegion(
            (int)Math.Round(region.X * CoordinateScale),
            (int)Math.Round(region.Y * CoordinateScale),
            Math.Max(1, (int)Math.Round(region.Width * CoordinateScale)),
            Math.Max(1, (int)Math.Round(region.Height * CoordinateScale)),
            name,
            statusText ?? scoreText,
            statusKind,
            kind == OverlayRegionKind.Default && scoreText is not null
                ? OverlayRegionKind.TemplateMatch
                : kind);
    }

    public IReadOnlyList<OverlayRegion> ToOverlayRegions(
        IEnumerable<TemplateMatchRegion> regions,
        string? name = null,
        OverlayRegionKind kind = OverlayRegionKind.Default)
    {
        return regions.Select(region => ToOverlayRegion(region, name, kind: kind)).ToArray();
    }

    private static string? FormatMatchScore(double? score)
    {
        if (!score.HasValue)
        {
            return null;
        }

        var value = score.Value;
        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            return null;
        }

        return (Math.Clamp(value, 0d, 1d) * 100d).ToString("0", CultureInfo.InvariantCulture) + "%";
    }

    private static Mat CreateBgraMat(CapturedFrame frame)
    {
        var mat = new Mat(frame.Height, frame.Width, MatType.CV_8UC4);
        var rowBytes = checked(frame.Width * 4);
        if (frame.Stride == rowBytes)
        {
            Marshal.Copy(frame.PixelsBgra8, 0, mat.Data, rowBytes * frame.Height);
            return mat;
        }

        for (var y = 0; y < frame.Height; y++)
        {
            Marshal.Copy(
                frame.PixelsBgra8,
                y * frame.Stride,
                IntPtr.Add(mat.Data, y * rowBytes),
                rowBytes);
        }

        return mat;
    }
}
