// 5단계: GAFIL이 화면을 조작하는 동안 보이는 빨간 테두리 + 위쪽 안내 띠 (지금 하는 동작, 멈추는 법)
// 클릭은 아래 창으로 그대로 통과하고, GAFIL이 찍는 스크린샷에는 나오지 않음
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

    public ScreenControlOverlay()
    {
        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = Brushes.Transparent;
        Topmost = true;
        ShowInTaskbar = false;
        ShowActivated = false;   // 나타날 때 포커스를 뺏지 않음 (GAFIL이 조작 중인 창의 포커스를 유지)
        ResizeMode = ResizeMode.NoResize;

        // 주 모니터 전체 (GAFIL이 보고 조작하는 영역과 같음)
        Left = 0;
        Top = 0;
        Width = SystemParameters.PrimaryScreenWidth;
        Height = SystemParameters.PrimaryScreenHeight;

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
        Content = new Grid
        {
            Children =
            {
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

    public void SetStopHint(string hint)
    {
        _stopHint = hint;
        SetAction(null);
    }

    // 안내 띠에 지금 하는 동작을 보여줌 (예: "클릭 (640, 400)")
    public void SetAction(string action) =>
        _label.Text = $"🐾 GAFIL이 화면 조작 중{(action != null ? " · " + action : "")} — 멈추기: {_stopHint}";

    protected override void OnSourceInitialized(EventArgs e)
    {
        base.OnSourceInitialized(e);
        var hwnd = new WindowInteropHelper(this).Handle;
        // 클릭 통과(WS_EX_TRANSPARENT): 테두리 위를 눌러도 아래 창이 눌림
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        SetWindowLong(hwnd, GWL_EXSTYLE, ex | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_NOACTIVATE);
        // 스크린샷에서 제외: GAFIL이 자기 테두리를 보고 헷갈리지 않게 (Windows 10 2004 이상)
        ExcludeFromCapture(hwnd);

        // 크기를 주 모니터의 실제 픽셀 영역에 정확히 맞춤 (WPF의 PrimaryScreenWidth는 앱을 켤 때의 배율에 고정됨)
        var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
        IntPtr mon = MonitorFromPoint(new POINT(), MONITOR_DEFAULTTOPRIMARY);
        if (mon != IntPtr.Zero && GetMonitorInfo(mon, ref mi))
        {
            // 실제 픽셀 → 이 창의 지금 배율 기준 DIP (WPF 속성으로 설정해야 레이아웃이 덮어쓰지 않음)
            var r = mi.rcMonitor;
            var dpi = VisualTreeHelper.GetDpi(this);
            Left = r.Left / dpi.DpiScaleX;
            Top = r.Top / dpi.DpiScaleY;
            Width = (r.Right - r.Left) / dpi.DpiScaleX;
            Height = (r.Bottom - r.Top) / dpi.DpiScaleY;
        }
    }

    [StructLayout(LayoutKind.Sequential)] private struct POINT { public int X, Y; }
    [StructLayout(LayoutKind.Sequential)] private struct RECT { public int Left, Top, Right, Bottom; }
    [StructLayout(LayoutKind.Sequential)] private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    private const uint MONITOR_DEFAULTTOPRIMARY = 1;
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);

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
