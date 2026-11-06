// v1.3 기능: 작업표시줄과 겹치지 않기
// - 보통 작업표시줄(아래·위·옆)은 Windows의 '작업 영역'이 이미 빼 주므로 고양이는 그 안에서만 다님
// - 문제는 '자동 숨김' 작업표시줄: 자리를 차지하지 않다가 마우스를 대면 튀어나와 고양이를 덮음
//   → 50ms마다 고양이가 사는 모니터의 아래쪽을 확인해서, 튀어나온 작업표시줄(또는 아래에 붙은 다른 프로그램의 독(dock))이 있으면
//     그 높이만큼 '바닥'을 올림 → 고양이가 작업표시줄 위로 폴짝 올라섬. 다시 숨으면 내려옴 (부드럽게)
using System.Runtime.InteropServices;
using System.Windows.Threading;

namespace ClaudeDesk;

public partial class MainWindow
{
    private readonly DispatcherTimer _floorTimer = new() { Interval = TimeSpan.FromMilliseconds(50) };
    private double _floorInsetPhys;        // 지금 바닥을 올린 높이 (실제 픽셀, 부드럽게 따라감)
    private double _floorInsetTarget;      // 올려야 할 높이 (작업표시줄이 튀어나온 만큼)

    private void InitTaskbarFloor()
    {
        _floorTimer.Tick += (_, _) =>
        {
            _floorInsetTarget = ComputeFloorInset();
            double diff = _floorInsetTarget - _floorInsetPhys;
            if (Math.Abs(diff) < 0.5) { if (_floorInsetPhys != _floorInsetTarget) { _floorInsetPhys = _floorInsetTarget; } return; }
            // 올라갈 땐 빠르게(덮이지 않게), 내려올 땐 천천히
            _floorInsetPhys += diff * (diff > 0 ? 0.6 : 0.25);
            if (!_dragging) ApplyPosition();
        };
        Loaded += (_, _) => _floorTimer.Start();
        Closed += (_, _) => _floorTimer.Stop();
    }

    // 고양이가 사는 모니터의 아래쪽을 덮는 것(튀어나온 자동 숨김 작업표시줄, 아래에 붙은 독)의 높이 (실제 픽셀)
    private double ComputeFloorInset()
    {
        try
        {
            var mi = new MONITORINFO { cbSize = Marshal.SizeOf<MONITORINFO>() };
            IntPtr mon = MonitorFromPoint(_monitorPt, MONITOR_DEFAULTTONEAREST);
            if (mon == IntPtr.Zero || !GetMonitorInfo(mon, ref mi)) return 0;
            var work = mi.rcWork;
            int monH = mi.rcMonitor.Bottom - mi.rcMonitor.Top;
            int monW = mi.rcMonitor.Right - mi.rcMonitor.Left;
            double inset = 0;

            // 1) 이 모니터 아래쪽의 자동 숨김 작업표시줄 (Windows가 알려 줌)
            var abd = new APPBARDATA { cbSize = Marshal.SizeOf<APPBARDATA>(), uEdge = ABE_BOTTOM, rc = mi.rcMonitor };
            IntPtr bar = SHAppBarMessage(ABM_GETAUTOHIDEBAREX, ref abd);
            if (bar != IntPtr.Zero && IsWindowVisible(bar) && GetWindowRect(bar, out var br)
                && br.Top < work.Bottom - 4 && br.Bottom >= work.Bottom - 8)
                inset = Math.Max(inset, work.Bottom - br.Top);

            // 2) 아래 가장자리에 붙은 '항상 위' 막대(다른 작업표시줄·독 프로그램): 폭이 넓고 높이가 낮은 창만
            uint myPid = (uint)Environment.ProcessId;
            foreach (double fx in new[] { 0.2, 0.5, 0.8 })
            {
                var p = new POINT { X = work.Left + (int)((work.Right - work.Left) * fx), Y = work.Bottom - 2 };
                IntPtr h = GetAncestor(WindowFromPoint(p), GA_ROOT);
                if (h == IntPtr.Zero) continue;
                GetWindowThreadProcessId(h, out uint pid);
                if (pid == myPid) continue;   // GAFIL 자신(고양이·빨간 테두리)은 제외
                if (h == bar) continue;       // 자동 숨김 작업표시줄은 위에서 따로 판단 (숨어 있어도 2px이 화면에 남음)
                if ((GetWindowLong(h, GWL_EXSTYLE) & WS_EX_TOPMOST) == 0) continue;
                if (!GetWindowRect(h, out var r)) continue;
                int w = r.Right - r.Left, hh = r.Bottom - r.Top;
                // 몇 픽셀만 걸친 것(숨은 작업표시줄의 가장자리 등)은 무시: 자동 숨김 판단과 같은 4px 기준
                if (w >= monW / 2 && hh > 0 && hh <= monH / 4 && r.Bottom >= work.Bottom - 2 && r.Top > work.Top && r.Top < work.Bottom - 4)
                    inset = Math.Max(inset, work.Bottom - r.Top);
            }

            return Math.Clamp(inset, 0, monH * 0.3);
        }
        catch { return 0; }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct APPBARDATA { public int cbSize; public IntPtr hWnd; public uint uCallbackMessage; public uint uEdge; public RECT rc; public IntPtr lParam; }

    private const uint ABM_GETAUTOHIDEBAREX = 0x0B, ABE_BOTTOM = 3, GA_ROOT = 2;
    private const int WS_EX_TOPMOST = 0x8;
    [DllImport("shell32.dll")] private static extern IntPtr SHAppBarMessage(uint msg, ref APPBARDATA data);
    [DllImport("user32.dll")] private static extern IntPtr WindowFromPoint(POINT p);
    [DllImport("user32.dll")] private static extern IntPtr GetAncestor(IntPtr h, uint flags);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr h, out RECT r);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
}
