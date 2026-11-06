// 5단계: GAFIL이 화면을 조작하는 동안 보이는 빨간 테두리 + 위쪽 안내 띠 (지금 하는 동작, 멈추는 법)
// 클릭은 아래 창으로 그대로 통과하고, GAFIL이 찍는 스크린샷에는 나오지 않음
// v1.3: 모니터마다 하나씩 띄움 (생성할 때 어느 모니터인지 받음)
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;

namespace ClaudeDesk;

public sealed class ScreenControlOverlay : Window
{
    private readonly TextBlock _label;
    private string _stopHint = "";
    private readonly Canvas _markerLayer = new();      // 클릭 예고 표시를 올리는 층
    private readonly FrameworkElement _marker;
    public MonitorRect Monitor { get; }

    public ScreenControlOverlay(MonitorRect monitor)
    {
        Monitor = monitor;
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;   // 나타날 때 포커스를 뺏지 않음 (GAFIL이 조작 중인 창의 포커스를 유지)
        ResizeMode = ResizeMode.NoResize;

        // 처음 위치는 대략만 (창이 만들어진 뒤 실제 픽셀로 정확히 맞춤: FitToMonitor)
        WindowStartupLocation = WindowStartupLocation.Manual;
        Left = monitor.Left;
        Top = monitor.Top;
        Width = 200;
        Height = 200;
        // 배율이 다른 모니터로 옮겨지면 WPF가 크기를 바꾸므로 다시 맞춤
        DpiChanged += (_, _) => FitToMonitor();
        Loaded += (_, _) => FitToMonitor();

        var red = new SolidColorBrush(Color.FromRgb(0xE0, 0x30, 0x20));
        red.Freeze();
        _label = new TextBlock
        {
            Foreground = Brushes.White,
            FontSize = 13,
            FontWeight = FontWeights.SemiBold,
            FontFamily = new FontFamily("Malgun Gothic, Segoe UI"),
            TextTrimming = TextTrimming.CharacterEllipsis,
            MaxWidth = 900,
        };
        // 클릭 예고 표시: 노란 고리 + 가운데 점 (GAFIL이 곧 누를 자리)
        var yellow = new SolidColorBrush(Color.FromRgb(0xFF, 0xC8, 0x2E));
        yellow.Freeze();
        _marker = new Grid
        {
            Width = 44,
            Height = 44,
            Visibility = Visibility.Collapsed,
            Children =
            {
                new System.Windows.Shapes.Ellipse { Stroke = yellow, StrokeThickness = 4 },
                new System.Windows.Shapes.Ellipse { Width = 8, Height = 8, Fill = yellow },
            },
        };
        _markerLayer.Children.Add(_marker);

        Content = new Grid
        {
            Children =
            {
                _markerLayer,
                new Border { BorderBrush = red, BorderThickness = new Thickness(5) },
                new Border
                {
                    Background = red,
                    CornerRadius = new CornerRadius(0, 0, 8, 8),
                    Padding = new Thickness(14, 4, 14, 6),
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Top,
                    Child = _label,
                },
            },
        };
    }

    // 클릭 예고 표시 보이기/숨기기. x, y: 실제 픽셀 (모든 모니터 공통 좌표) → 이 모니터 안의 비율로 바꿔서 그림
    public void ShowMarker(int x, int y)
    {
        double nx = (x - Monitor.Left + 0.5) / Monitor.Width, ny = (y - Monitor.Top + 0.5) / Monitor.Height;
        Canvas.SetLeft(_marker, nx * ActualWidth - _marker.Width / 2);
        Canvas.SetTop(_marker, ny * ActualHeight - _marker.Height / 2);
        _marker.Visibility = Visibility.Visible;
    }

    public void HideMarker() => _marker.Visibility = Visibility.Collapsed;

    public void SetStopHint(string hint)
    {
        _stopHint = hint;
        SetAction(null);
    }

    // 안내 띠에 지금 하는 동작을 보여줌 (예: "클릭 (640, 400)")
    public void SetAction(string action) =>
        _label.Text = L.T("🐾 GAFIL이 화면 조작 중", "🐾 GAFIL is controlling the screen") + (action != null ? " · " + action : "") +
            L.T(" — 멈추기: ", " — stop: ") + _stopHint;

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        // 클릭 통과(WS_EX_TRANSPARENT): 테두리 위를 눌러도 아래 창이 눌림
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        // 스크린샷에서 제외: GAFIL이 자기 테두리를 보고 헷갈리지 않게 (Windows 10 2004 이상)
        ExcludeFromCapture(hwnd);

        FitToMonitor();
    }

    // 창을 맡은 모니터의 실제 픽셀 영역에 정확히 맞춤: 먼저 실제 픽셀로 옮기고(SetWindowPos),
    // 그 모니터의 배율로 WPF 크기 속성도 맞춤 (WPF 속성까지 맞춰야 레이아웃이 크기를 되돌리지 않음)
    private void FitToMonitor()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;
        SetWindowPos(hwnd, IntPtr.Zero, Monitor.Left, Monitor.Top, Monitor.Width, Monitor.Height, SWP_NOZORDER | SWP_NOACTIVATE);
        var dpi = VisualTreeHelper.GetDpi(this);
        Left = Monitor.Left / dpi.DpiScaleX;
        Top = Monitor.Top / dpi.DpiScaleY;
        Width = Monitor.Width / dpi.DpiScaleX;
        Height = Monitor.Height / dpi.DpiScaleY;
    }

    private const uint SWP_NOZORDER = 0x4, SWP_NOACTIVATE = 0x10;
    [DllImport("user32.dll")] private static extern bool SetWindowPos(IntPtr hwnd, IntPtr after, int x, int y, int cx, int cy, uint flags);

    // 창을 화면 캡처에서 빼는 공통 함수 (MainWindow에서도 사용)
    public static void ExcludeFromCapture(IntPtr hwnd)
    {
        try { SetWindowDisplayAffinity(hwnd, WDA_EXCLUDEFROMCAPTURE); } catch { }
    }

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TRANSPARENT = 0x20, WS_EX_TOOLWINDOW = 0x80, WS_EX_NOACTIVATE = 0x08000000;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);
}
