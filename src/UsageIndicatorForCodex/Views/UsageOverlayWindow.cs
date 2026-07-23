using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using UsageIndicatorForCodex.Core;
using UsageIndicatorForCodex.Interop;
using UsageIndicatorForCodex.Services;

namespace UsageIndicatorForCodex.Views;

internal sealed class UsageOverlayWindow : Window
{
    private readonly Border _container;
    private readonly Border _barFill;
    private readonly Border _barTrack;
    private readonly TextBlock _usageLabel;
    private readonly TextBlock _dateLabel;
    private readonly TextBlock _separator;
    private readonly Border _usageToBarSpacer;
    private readonly Border _barToSeparatorSpacer;
    private readonly Border _separatorToDateSpacer;
    private OverlayLayout _layout;
    private bool _clickable;

    public UsageOverlayWindow()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        ShowActivated = false;
        Focusable = false;
        Height = 30;

        _usageLabel = NewTextBlock();
        _dateLabel = NewTextBlock();
        _separator = NewTextBlock("|");
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
        _separatorToDateSpacer = Spacer(8);
        content.Children.Add(_separatorToDateSpacer);
        content.Children.Add(_dateLabel);

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

    internal void SetOwner(nint owner)
    {
        var interop = new WindowInteropHelper(this);
        interop.EnsureHandle();
        interop.Owner = owner;
    }

    public OverlayLayout Render(IndicatorState state, UsageSnapshot? snapshot, double availableWidth)
    {
        var isAvailable = state == IndicatorState.Available && snapshot is not null;
        _clickable = state == IndicatorState.Unavailable;
        _dateLabel.Text = isAvailable ? IndicatorPresentation.FormatResetTime(snapshot!.ResetsAt) : "—";

        var percentage = isAvailable ? snapshot!.RemainingPercent : 0;
        _barFill.Width = _barTrack.Width * percentage / 100;
        _barFill.Background = ToneBrush(isAvailable ? IndicatorPresentation.GetTone(percentage) : IndicatorTone.Neutral);
        _barTrack.Background = state == IndicatorState.Loading
            ? PatternBrush(dotted: true)
            : state == IndicatorState.Unavailable
                ? PatternBrush(dotted: false)
                : new SolidColorBrush(Color.FromRgb(82, 82, 91));

        foreach (var layout in new[] { OverlayLayout.Full, OverlayLayout.Narrow, OverlayLayout.Compact })
        {
            ApplyLayout(state, snapshot, layout);
            _container.InvalidateMeasure();
            _container.Measure(new Size(double.PositiveInfinity, Height));
            Width = Math.Ceiling(_container.DesiredSize.Width);
            if (Width <= availableWidth)
            {
                ApplyExtendedStyles();
                return layout;
            }
        }

        _layout = OverlayLayout.Hidden;
        Width = 0;
        ApplyExtendedStyles();
        return OverlayLayout.Hidden;
    }

    private void ApplyLayout(IndicatorState state, UsageSnapshot? snapshot, OverlayLayout layout)
    {
        _layout = layout;
        _usageLabel.Text = IndicatorPresentation.FormatUsageLabel(state, snapshot, layout);
        var showBar = layout is OverlayLayout.Full or OverlayLayout.Narrow;
        var showDetails = layout == OverlayLayout.Full;
        _usageToBarSpacer.Visibility = showBar ? Visibility.Visible : Visibility.Collapsed;
        _barTrack.Visibility = showBar ? Visibility.Visible : Visibility.Collapsed;
        _barToSeparatorSpacer.Visibility = showDetails ? Visibility.Visible : Visibility.Collapsed;
        _separator.Visibility = showDetails ? Visibility.Visible : Visibility.Collapsed;
        _separatorToDateSpacer.Visibility = showDetails ? Visibility.Visible : Visibility.Collapsed;
        _dateLabel.Visibility = showDetails ? Visibility.Visible : Visibility.Collapsed;
    }

    internal void Position(nint codexWindowHandle, NativeMethods.Rect rect, UserSettings settings)
    {
        if (PresentationSource.FromVisual(this) is not HwndSource source)
        {
            return;
        }

        UpdateLayout();
        var dpi = NativeMethods.GetDpiForWindow(codexWindowHandle);
        var scale = dpi / 96d;
        var width = Math.Max(1, (int)Math.Ceiling(Width * scale));
        var height = Math.Max(1, (int)Math.Ceiling(Height * scale));
        var x = rect.Left + (rect.Width - width) / 2 + (int)Math.Round(settings.HorizontalOffset * scale);
        var y = rect.Top + (int)Math.Round(settings.VerticalOffset * scale);
        NativeMethods.SetWindowPos(source.Handle, 0, x, y, width, height,
            NativeMethods.SwpNoActivate | NativeMethods.SwpShowWindow | NativeMethods.SwpNoOwnerZOrder);
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
