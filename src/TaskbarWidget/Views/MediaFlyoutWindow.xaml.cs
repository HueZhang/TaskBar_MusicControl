using System.Windows;
using System.Windows.Interop;
using System.Runtime.InteropServices;
using System.Windows.Media.Animation;
using TaskbarWidget.Interop;
using TaskbarWidget.ViewModels;

namespace TaskbarWidget.Views;

/// <summary>
/// A small floating flyout with media controls, shown above the taskbar widget
/// when the media page is clicked. Styled like a FluentFlyout media flyout.
/// </summary>
public partial class MediaFlyoutWindow : Window
{
    private bool _closing;
    private Storyboard? _activeStoryboard;

    public MediaFlyoutWindow(WidgetViewModel viewModel)
    {
        InitializeComponent();
        DataContext = viewModel;
        SourceInitialized += (_, _) =>
        {
            nint hwnd = new WindowInteropHelper(this).Handle;
            long ex = NativeMethods.GetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE).ToInt64();
            ex |= NativeMethods.WS_EX_NOACTIVATE;
            // Do NOT enable the composition blur behind this window: it paints a
            // square acrylic rectangle that shows outside the rounded surface.
            NativeMethods.SetWindowLongPtr(hwnd, NativeMethods.GWL_EXSTYLE, (nint)ex);
        };
    }

    /// <summary>
    /// Stop any running animation and release its held value. Without this, a
    /// HoldEnd animation keeps overriding Top, so each show/hide cycle drifts
    /// the flyout down by the slide distance.
    /// </summary>
    private void StopActiveAnimation()
    {
        if (_activeStoryboard is null) return;
        _activeStoryboard.Stop(this);
        _activeStoryboard = null;
    }

    /// <summary>Show the flyout above the widget, anchored to its horizontal centre.</summary>
    public void ShowAt(int widgetLeft, int widgetRight, int widgetTop, double dpiScale)
    {
        StopActiveAnimation();

        double scale = dpiScale > 0 ? dpiScale : 1.0;

        // Force layout so ActualWidth/Height reflect the XAML (164x54). On the
        // very first Show the window has not been laid out yet, so these are 0
        // and the fallback below wrongly shifts the flyout.
        if (!IsLoaded)
        {
            Measure(new System.Windows.Size(double.PositiveInfinity, double.PositiveInfinity));
            Arrange(new System.Windows.Rect(0, 0, Width, Height));
            UpdateLayout();
        }

        double width = ActualWidth > 0 ? ActualWidth : 164;
        double height = ActualHeight > 0 ? ActualHeight : 54;

        double center = (widgetLeft + widgetRight) / 2.0;
        Left = center - (width * scale) / 2.0;
        double targetTop = (widgetTop / scale) - height - 10;
        Top = targetTop;

        _closing = false;
        Show();

        // Fluent-style slide-up + fade-in. FillBehavior.Stop releases the
        // animated value when the storyboard ends so Top returns to targetTop.
        var storyboard = new Storyboard();
        var slide = new DoubleAnimation
        {
            From = targetTop + 8,
            To = targetTop,
            Duration = TimeSpan.FromMilliseconds(180),
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
        };
        Storyboard.SetTarget(slide, this);
        Storyboard.SetTargetProperty(slide, new PropertyPath(Window.TopProperty));
        storyboard.Children.Add(slide);

        var fade = new DoubleAnimation(0, 1, TimeSpan.FromMilliseconds(160))
        {
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(fade, this);
        Storyboard.SetTargetProperty(fade, new PropertyPath(Window.OpacityProperty));
        storyboard.Children.Add(fade);

        storyboard.Completed += (_, _) =>
        {
            Opacity = 1;
            if (ReferenceEquals(_activeStoryboard, storyboard)) _activeStoryboard = null;
        };
        _activeStoryboard = storyboard;
        storyboard.Begin(this, true);
    }

    /// <summary>Slide down + fade out, then hide. Safe to call repeatedly.</summary>
    public void CloseWithAnimation()
    {
        if (!IsVisible || _closing) return;
        _closing = true;
        StopActiveAnimation();

        double targetTop = Top;
        var storyboard = new Storyboard();
        var slide = new DoubleAnimation
        {
            From = targetTop,
            To = targetTop + 8,
            Duration = TimeSpan.FromMilliseconds(150),
            FillBehavior = FillBehavior.Stop,
            EasingFunction = new CubicEase { EasingMode = EasingMode.EaseIn }
        };
        Storyboard.SetTarget(slide, this);
        Storyboard.SetTargetProperty(slide, new PropertyPath(Window.TopProperty));
        storyboard.Children.Add(slide);

        var fade = new DoubleAnimation(1, 0, TimeSpan.FromMilliseconds(140))
        {
            FillBehavior = FillBehavior.Stop
        };
        Storyboard.SetTarget(fade, this);
        Storyboard.SetTargetProperty(fade, new PropertyPath(Window.OpacityProperty));
        storyboard.Children.Add(fade);

        storyboard.Completed += (_, _) =>
        {
            Hide();
            Opacity = 1;
            _closing = false;
            if (ReferenceEquals(_activeStoryboard, storyboard)) _activeStoryboard = null;
        };
        _activeStoryboard = storyboard;
        storyboard.Begin(this, true);
    }
}
