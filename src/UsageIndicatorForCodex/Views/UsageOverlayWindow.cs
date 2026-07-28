using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Shapes;
using UsageIndicatorForCodex.Core;
using UsageIndicatorForCodex.Interop;
using UsageIndicatorForCodex.Services;

namespace UsageIndicatorForCodex.Views;

internal readonly record struct OverlayPlacement(int X, int Y, int Width, int Height);

internal sealed class UsageOverlayWindow : Window
{
    private const double OverlayHeightDip = 30d;
    private const double IconSizeDip = 12d;
    private const double IconStrokeThicknessDip = 1.5d;
    private const double IconToTimestampGapDip = 4d;
    internal const string ResetIconGeometryData = "M 1.75,5.25 C 2.4,2.8 5.05,1.35 7.45,2.15 C 8.55,2.5 9.45,3.25 10.1,4.25 M 10.1,1.65 L 10.1,4.25 L 7.5,4.25 M 10.25,6.75 C 9.6,9.2 6.95,10.65 4.55,9.85 C 3.45,9.5 2.55,8.75 1.9,7.75 M 1.9,10.35 L 1.9,7.75 L 4.5,7.75";
    internal const string CreditIconGeometryData = "M 6,1.25 A 4.75,4.75 0 1 1 5.999,1.25 M 6,3.1 L 6,8.9 M 7.65,4.15 C 7.25,3.55 6.7,3.25 6,3.25 C 5.1,3.25 4.5,3.7 4.5,4.4 C 4.5,5.15 5.15,5.5 6,5.75 C 6.85,6 7.5,6.35 7.5,7.1 C 7.5,7.8 6.9,8.25 6,8.25 C 5.3,8.25 4.7,7.95 4.3,7.35";
    private readonly Border _container;
    private readonly Border _barFill;
    private readonly Border _barTrack;
    private readonly TextBlock _usageLabel;
    private readonly TextBlock _dateLabel;
    private readonly TextBlock _separator;
    private readonly Path _resetIcon;
    private readonly TextBlock _creditSeparator;
    private readonly Path _creditIcon;
    private readonly TextBlock _creditDateLabel;
    private readonly Border _usageToBarSpacer;
    private readonly Border _barToSeparatorSpacer;
    private readonly Border _separatorToResetIconSpacer;
    private readonly Border _resetIconToDateSpacer;
    private readonly Border _dateToCreditSeparatorSpacer;
    private readonly Border _creditSeparatorToIconSpacer;
    private readonly Border _creditIconToDateSpacer;
    private OverlayLayout _layout;
    private double _renderedWidthDip;
    private bool _clickable;

    public UsageOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        ResizeMode = ResizeMode.NoResize;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Height = OverlayHeightDip;
        MinHeight = OverlayHeightDip;
        MaxHeight = OverlayHeightDip;

        _usageLabel = NewTextBlock();
        _dateLabel = NewTextBlock();
        _separator = NewTextBlock("|");
        _resetIcon = NewTimestampIcon(ResetIconGeometryData);
        _creditSeparator = NewTextBlock("|");
        _creditIcon = NewTimestampIcon(CreditIconGeometryData);
        _creditDateLabel = NewTextBlock();
        _barFill = new Border { Height = 5, HorizontalAlignment = HorizontalAlignment.Left, CornerRadius = new CornerRadius(3) };
        _barTrack = new Border
        {
            Width = 140,
            Height = 5,
            Background = new SolidColorBrush(Color.FromRgb(82, 82, 91)),
            CornerRadius = new CornerRadius(3),
            Child = _barFill,
            VerticalAlignment = VerticalAlignment.Center
        };

        var content = new StackPanel { Orientation = Orientation.Horizontal, VerticalAlignment = VerticalAlignment.Center };
        content.Children.Add(_usageLabel);
        _usageToBarSpacer = Spacer(8);
        content.Children.Add(_usageToBarSpacer);
        content.Children.Add(_barTrack);
        _barToSeparatorSpacer = Spacer(8);
        content.Children.Add(_barToSeparatorSpacer);
        content.Children.Add(_separator);
        _separatorToResetIconSpacer = Spacer(8);
        content.Children.Add(_separatorToResetIconSpacer);
        content.Children.Add(_resetIcon);
        _resetIconToDateSpacer = Spacer(IconToTimestampGapDip);
        content.Children.Add(_resetIconToDateSpacer);
        content.Children.Add(_dateLabel);
        _dateToCreditSeparatorSpacer = Spacer(8);
        content.Children.Add(_dateToCreditSeparatorSpacer);
        content.Children.Add(_creditSeparator);
        _creditSeparatorToIconSpacer = Spacer(8);
        content.Children.Add(_creditSeparatorToIconSpacer);
        content.Children.Add(_creditIcon);
        _creditIconToDateSpacer = Spacer(IconToTimestampGapDip);
        content.Children.Add(_creditIconToDateSpacer);
        content.Children.Add(_creditDateLabel);

        _container = new Border
        {
            Background = new SolidColorBrush(Color.FromArgb(242, 28, 28, 30)),
            BorderBrush = new SolidColorBrush(Color.FromRgb(63, 63, 70)),
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(10, 5, 10, 5),
            Child = content
        };
        Content = _container;
        SourceInitialized += (_, _) => ApplyExtendedStyles();
        MouseLeftButtonUp += (_, _) =>
        {
            if (_clickable)
            {
                RetryRequested?.Invoke(this, EventArgs.Empty);
            }
        };
    }

    public event EventHandler? RetryRequested;

    internal bool IsResetIconVisible => _resetIcon.Visibility == Visibility.Visible;
    internal bool IsCreditIconVisible => _creditIcon.Visibility == Visibility.Visible;
    internal double ResetIconWidth => _resetIcon.Width;
    internal double ResetIconHeight => _resetIcon.Height;
    internal double ResetIconStrokeThickness => _resetIcon.StrokeThickness;
    internal double CreditIconWidth => _creditIcon.Width;
    internal double CreditIconHeight => _creditIcon.Height;
    internal double CreditIconStrokeThickness => _creditIcon.StrokeThickness;
    internal double TimestampIconGap => _resetIconToDateSpacer.Width;

    internal void SetOwner(nint owner)
    {
        var interop = new WindowInteropHelper(this);
        interop.EnsureHandle();
        interop.Owner = owner;
    }

    public OverlayLayout Render(
        IndicatorState state,
        UsageSnapshot? snapshot,
        bool creditExpiryEnabled,
        double availableWidth)
    {
        var isAvailable = state == IndicatorState.Available && snapshot is not null;
        var showCreditDetails = isAvailable
            && creditExpiryEnabled
            && snapshot!.CreditExpiresAt is { } creditExpiresAt
            && creditExpiresAt > DateTimeOffset.UtcNow;
        _clickable = state == IndicatorState.Unavailable;
        _dateLabel.Text = isAvailable ? IndicatorPresentation.FormatResetTime(snapshot!.ResetsAt) : "—";

        _creditDateLabel.Text = showCreditDetails
            ? IndicatorPresentation.FormatResetTime(snapshot!.CreditExpiresAt!.Value)
            : string.Empty;

        var percentage = isAvailable ? snapshot!.RemainingPercent : 0;
        _barFill.Width = _barTrack.Width * percentage / 100;
        _barFill.Background = ToneBrush(isAvailable ? IndicatorPresentation.GetTone(percentage) : IndicatorTone.Neutral);
        _barTrack.Background = state == IndicatorState.Loading
            ? PatternBrush(dotted: true)
            : state == IndicatorState.Unavailable
                ? PatternBrush(dotted: false)
                : new SolidColorBrush(Color.FromRgb(82, 82, 91));

        var candidates = new List<(OverlayLayout Layout, bool ShowCreditDetails)>();
        if (showCreditDetails)
        {
            candidates.Add((OverlayLayout.Full, true));
        }

        candidates.Add((OverlayLayout.Full, false));
        candidates.Add((OverlayLayout.Narrow, false));
        candidates.Add((OverlayLayout.Compact, false));
        foreach (var candidate in candidates)
        {
            ApplyLayout(state, snapshot, candidate.Layout, candidate.ShowCreditDetails);
            _container.InvalidateMeasure();
            _container.Measure(new Size(double.PositiveInfinity, OverlayHeightDip));
            _renderedWidthDip = Math.Ceiling(_container.DesiredSize.Width);
            Width = _renderedWidthDip;
            if (Width <= availableWidth)
            {
                ApplyExtendedStyles();
                return candidate.Layout;
            }
        }

        _layout = OverlayLayout.Hidden;
        _renderedWidthDip = 0;
        Width = 0;
        ApplyExtendedStyles();
        return OverlayLayout.Hidden;
    }

    private void ApplyLayout(
        IndicatorState state,
        UsageSnapshot? snapshot,
        OverlayLayout layout,
        bool showCreditDetails)
    {
        _layout = layout;
        _usageLabel.Text = IndicatorPresentation.FormatUsageLabel(state, snapshot, layout);
        var isAvailable = state == IndicatorState.Available && snapshot is not null;
        var showBar = layout is OverlayLayout.Full or OverlayLayout.Narrow;
        var showDetails = layout == OverlayLayout.Full;
        var showResetIcon = showDetails && isAvailable;
        _usageToBarSpacer.Visibility = showBar ? Visibility.Visible : Visibility.Collapsed;
        _barTrack.Visibility = showBar ? Visibility.Visible : Visibility.Collapsed;
        _barToSeparatorSpacer.Visibility = showDetails ? Visibility.Visible : Visibility.Collapsed;
        _separator.Visibility = showDetails ? Visibility.Visible : Visibility.Collapsed;
        _separatorToResetIconSpacer.Visibility = showDetails ? Visibility.Visible : Visibility.Collapsed;
        _resetIcon.Visibility = showResetIcon ? Visibility.Visible : Visibility.Collapsed;
        _resetIconToDateSpacer.Visibility = showResetIcon ? Visibility.Visible : Visibility.Collapsed;
        _dateLabel.Visibility = showDetails ? Visibility.Visible : Visibility.Collapsed;
        _dateToCreditSeparatorSpacer.Visibility = showCreditDetails ? Visibility.Visible : Visibility.Collapsed;
        _creditSeparator.Visibility = showCreditDetails ? Visibility.Visible : Visibility.Collapsed;
        _creditSeparatorToIconSpacer.Visibility = showCreditDetails ? Visibility.Visible : Visibility.Collapsed;
        _creditIcon.Visibility = showCreditDetails ? Visibility.Visible : Visibility.Collapsed;
        _creditIconToDateSpacer.Visibility = showCreditDetails ? Visibility.Visible : Visibility.Collapsed;
        _creditDateLabel.Visibility = showCreditDetails ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void Position(nint codexWindowHandle, NativeMethods.Rect rect, UserSettings settings)
    {
        if (PresentationSource.FromVisual(this) is not HwndSource source)
        {
            return;
        }

        UpdateLayout();
        var dpi = NativeMethods.GetDpiForWindow(codexWindowHandle);
        var placement = CalculatePlacement(rect, settings, _renderedWidthDip, dpi);
        NativeMethods.SetWindowPos(
            source.Handle,
            0,
            placement.X,
            placement.Y,
            placement.Width,
            placement.Height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow | NativeMethods.SwpNoOwnerZOrder);
    }

    internal static OverlayPlacement CalculatePlacement(
        NativeMethods.Rect rect,
        UserSettings settings,
        double renderedWidthDip,
        uint dpi)
    {
        var effectiveDpi = dpi == 0 ? 96u : dpi;
        var scale = effectiveDpi / 96d;
        var width = Math.Max(1, (int)Math.Ceiling(renderedWidthDip * scale));
        var height = Math.Max(1, (int)Math.Ceiling(OverlayHeightDip * scale));
        var x = rect.Left + (rect.Width - width) / 2 + (int)Math.Round(settings.HorizontalOffset * scale);
        var y = rect.Top + (int)Math.Round(settings.VerticalOffset * scale);
        return new OverlayPlacement(x, y, width, height);
    }

    private void ApplyExtendedStyles()
    {
        if (PresentationSource.FromVisual(this) is not HwndSource source)
        {
            return;
        }

        var style = NativeMethods.GetWindowLongPtr(source.Handle, NativeMethods.GwlExStyle);
        style |= NativeMethods.WsExToolWindow | NativeMethods.WsExNoActivate;
        style = _clickable ? style & ~NativeMethods.WsExTransparent : style | NativeMethods.WsExTransparent;
        NativeMethods.SetWindowLongPtr(source.Handle, NativeMethods.GwlExStyle, style);
    }

    private static Border Spacer(double width) => new() { Width = width };

    private static TextBlock NewTextBlock(string? text = null) => new()
    {
        Text = text ?? string.Empty,
        Foreground = new SolidColorBrush(Color.FromRgb(244, 244, 245)),
        FontFamily = new FontFamily("Segoe UI"),
        FontSize = 12,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static Path NewTimestampIcon(string geometryData) => new()
    {
        Data = Geometry.Parse(geometryData),
        Width = IconSizeDip,
        Height = IconSizeDip,
        Stretch = Stretch.None,
        Stroke = new SolidColorBrush(Color.FromRgb(244, 244, 245)),
        StrokeThickness = IconStrokeThicknessDip,
        StrokeStartLineCap = PenLineCap.Round,
        StrokeEndLineCap = PenLineCap.Round,
        StrokeLineJoin = PenLineJoin.Round,
        Fill = Brushes.Transparent,
        VerticalAlignment = VerticalAlignment.Center
    };

    private static System.Windows.Media.Brush ToneBrush(IndicatorTone tone) => tone switch
    {
        IndicatorTone.Green => new SolidColorBrush(Color.FromRgb(34, 197, 94)),
        IndicatorTone.Amber => new SolidColorBrush(Color.FromRgb(245, 158, 11)),
        IndicatorTone.Red => new SolidColorBrush(Color.FromRgb(239, 68, 68)),
        _ => new SolidColorBrush(Color.FromRgb(161, 161, 170))
    };

    private static System.Windows.Media.Brush PatternBrush(bool dotted)
    {
        var color = new SolidColorBrush(Color.FromRgb(161, 161, 170));
        var drawing = new DrawingGroup();
        if (dotted)
        {
            drawing.Children.Add(new GeometryDrawing(color, null, new EllipseGeometry(new Point(3, 2.5), 1, 1)));
        }
        else
        {
            drawing.Children.Add(new GeometryDrawing(null, new Pen(color, 1), new LineGeometry(new Point(0, 2.5), new Point(6, 2.5))));
        }

        return new DrawingBrush(drawing)
        {
            TileMode = TileMode.Tile,
            Viewport = new Rect(0, 0, 6, 5),
            ViewportUnits = BrushMappingMode.Absolute,
            Viewbox = new Rect(0, 0, 6, 5),
            ViewboxUnits = BrushMappingMode.Absolute
        };
    }
}
