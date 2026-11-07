// v1.1 기능: 고양이를 마우스로 집어서 옮기기 + 다른 모니터로 데려가기
// - 고양이를 누른 채 움직이면 어미 고양이가 새끼를 물어 옮기듯 '목덜미'를 잡힌 모습으로 바뀜:
//   목이 커서에 붙고, 몸통·다리·꼬리는 아래로 축 늘어져 움직이는 반대 방향으로 대롱대롱 흔들림
// - 놓으면 그 자리에서 바닥(작업표시줄 위)으로 떨어짐
// - 놓은 모니터에서 계속 돌아다니고, 위치는 settings.json에 저장돼서 다시 켜도 그 모니터에 나타남
// 좌표: 모니터 판단은 실제 픽셀(물리 좌표), 창 위치는 지금 창의 배율로 바꾼 DIP
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media;

namespace ClaudeDesk;

public partial class MainWindow
{
    private POINT _monitorPt;          // 고양이가 사는 모니터 안의 한 점 (실제 픽셀)
    private bool _mouseDown;           // 고양이 위에서 왼쪽 버튼을 누른 상태
    private bool _dragging;            // 집어 들고 옮기는 중
    private POINT _downAt;             // 누른 위치 (실제 픽셀) → 일정 거리 이상 움직여야 '집기'로 봄 (그냥 클릭과 구분)

    private const int DragThreshold = 6;   // 실제 픽셀

    // 목덜미를 잡힌 모습 (정면). 머리는 위, 몸통·앞발·뒷발·꼬리는 아래로 늘어짐
    private static readonly string[] HangC =
    {
        "....K......K....",
        "...KPK....KPK...",
        "...KOOKKKKOOK...",
        "...KOOEOOEOOK...",
        "...KLOOPPOOLK...",
        "....KKOOOOKK....",
        ".....KODDOK.....",
        "...KOKOOOOKOK...",
        "...KOKLLLLKOK...",
        "...KKKLLLLKKK...",
        ".....KOOOOK.KK..",
        ".....KK..KKKOK..",
    };
    // 흔들린 모습: 목 아래(6행부터)를 왼쪽/오른쪽으로 밀어서 만듦 (아래로 갈수록 더 많이 → 진자처럼)
    private static readonly string[] HangL = SwingRows(HangC, 6, -1);
    private static readonly string[] HangR = SwingRows(HangC, 6, 1);

    // 잡히는 점(목덜미): 그림의 7.5열, 5.5행 → 고양이 칸 기준 DIP
    private const double ScruffX = 7.5 * Cell, ScruffY = 5.5 * Cell;

    private double _swing;            // 흔들림 정도 (+면 커서가 오른쪽으로 움직이는 중 → 몸은 왼쪽으로 처짐)
    private int _lastDragX;

    private static string[] SwingRows(string[] rows, int fromRow, int dx)
    {
        var o = new string[rows.Length];
        for (int i = 0; i < rows.Length; i++)
        {
            int d = i < fromRow ? 0 : i < fromRow + 3 ? dx : dx * 2;
            string r = rows[i];
            o[i] = d > 0 ? new string('.', d) + r[..^d] : d < 0 ? r[(-d)..] + new string('.', -d) : r;
        }
        return o;
    }

    // 들려 있을 때 보여줄 프레임: 움직임 반대쪽으로 처지고, 멈추면 천천히 좌우로 흔들림
    private string[] HangFrame()
    {
        if (_swing > 3) return HangL;
        if (_swing < -3) return HangR;
        return ((_animTick / 25) % 4) switch { 1 => HangL, 3 => HangR, _ => HangC };
    }

    private void InitDrag()
    {
        // 저장된 모니터 위치가 있으면 그 모니터에서 시작 (없거나 사라진 모니터면 Windows가 가장 가까운 모니터를 고름)
        if (_settings.CatScreenX is int sx && _settings.CatScreenY is int sy) _monitorPt = new POINT { X = sx, Y = sy };

        Cat.MouseLeftButtonDown += (_, e) =>
        {
            if (_bubbleOpen || _claude.ScreenActive) return;   // 말풍선이 열렸거나 화면 조작 중이면 옮기지 않음
            _mouseDown = true;
            GetCursorPos(out _downAt);
            Cat.CaptureMouse();
            e.Handled = true;
        };

        Cat.MouseMove += (_, _) =>
        {
            if (!_mouseDown) return;
            GetCursorPos(out var p);
            if (!_dragging && (Math.Abs(p.X - _downAt.X) > DragThreshold || Math.Abs(p.Y - _downAt.Y) > DragThreshold))
            {
                _dragging = true;
                _perch = IntPtr.Zero;     // 창 위에 있었어도 들어 올림 (높이는 아래 FollowCursor가 바닥 기준으로 다시 정함)
                CancelTrip();
                _state = CatState.Jump;   // 놓으면 바로 떨어지도록 (들려 있는 동안 그림은 HangFrame)
                _jumpVy = 0;
                _vx = 0;
                _swing = 0;
                _lastDragX = p.X;
            }
            if (_dragging) FollowCursor(p);
        };

        // 버튼을 뗌: 끌었으면 '내려놓기', 아니면 보통 클릭(말풍선 열기/닫기)
        Cat.MouseLeftButtonUp += (_, e) =>
        {
            e.Handled = true;
            if (!_mouseDown)
            {
                // 누를 때 끌기를 시작하지 않은 경우(말풍선이 열려 있었음 등) → 보통 클릭
                if (!_claude.ScreenActive && !RegisterCatClick()) ToggleBubble();   // 1.5초 안에 5번이면 하악 (MainWindow.Eggs.cs)
                return;
            }
            // 플래그를 먼저 정리한 뒤 캡처를 풂: ReleaseMouseCapture는 LostMouseCapture를 '즉시' 부르므로
            // 순서가 반대면 그 처리기가 _dragging을 지워서 내려놓기 처리가 통째로 건너뛰어짐
            bool wasDragging = _dragging;
            _mouseDown = false;
            _dragging = false;
            Cat.ReleaseMouseCapture();
            if (!wasDragging)
            {
                if (!RegisterCatClick()) ToggleBubble();
                return;
            }

            // 놓은 자리에서 떨어지기 시작 (점프 상태에서 위로 속도 0 → 중력으로 떨어짐). 창 위에 놓으면 그 창에 내려앉음
            _state = CatState.Jump;
            _jumpVy = 0;
            _vx = 0;

            // 어느 모니터에 놓았는지 기억 (다시 켜도 그 모니터에서 시작)
            GetCursorPos(out var p);
            _monitorPt = p;
            _settings.CatScreenX = p.X;
            _settings.CatScreenY = p.Y;
            try { _settings.Save(); } catch { }
            AppSettings.Log($"[고양이] 옮김 → 화면 좌표 ({p.X}, {p.Y})");
            CatMeow.TryMeow(_settings.Meow);   // 내려놓으면 가끔 야옹 (10분에 한 번까지)
        };

        // 배율이 다른 모니터로 넘어가면 창의 배율이 바뀜 → DIP로 저장한 위치도 같은 실제 픽셀이 되게 다시 계산
        // (안 하면 125%→150% 모니터로 옮길 때 고양이가 엉뚱한 곳으로 튐)
        DpiChanged += (_, e) =>
        {
            double kx = e.OldDpi.DpiScaleX / e.NewDpi.DpiScaleX;
            double ky = e.OldDpi.DpiScaleY / e.NewDpi.DpiScaleY;
            _centerX *= kx;
            _jumpY *= ky;
        };

        // 끌기 도중 마우스 캡처를 잃으면(다른 창이 뺏음 등) 그 자리에서 떨어지게
        Cat.LostMouseCapture += (_, _) =>
        {
            if (!_dragging) { _mouseDown = false; return; }
            _dragging = false;
            _mouseDown = false;
            _state = CatState.Jump;
            _jumpVy = 0;
            _vx = 0;
        };
    }

    // 커서를 따라 고양이를 움직임: 목덜미가 커서에 붙도록
    private void FollowCursor(POINT p)
    {
        _monitorPt = p;
        var wa = WorkArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        double x = p.X / dpi.DpiScaleX, y = p.Y / dpi.DpiScaleY;

        // 흔들림: 최근 가로 움직임을 부드럽게 섞음 (빨리 휘두를수록 더 처짐)
        _swing = Math.Clamp(_swing * 0.6 + (p.X - _lastDragX) * 0.4, -20, 20);
        _lastDragX = p.X;

        // 고양이 칸은 창 아래쪽 가운데에 있음 → 칸의 가운데 = _centerX, 칸의 위쪽 = 바닥 - 점프 높이 - 고양이 높이
        double half = HalfWidth();
        _centerX = Math.Clamp(x + (CatWidth / 2 - ScruffX), wa.Left + half, wa.Right - half);
        _jumpY = Math.Clamp(wa.Bottom - CatHeight + ScruffY - y, 0, Math.Max(0, wa.Height - CatHeight - PillHeight));
        UpdateSprite();
        ApplyPosition();
    }

    // 끌기를 즉시 끝냄 (끌던 중에 말풍선을 열게 될 때 등): 바닥에 내려놓고 앉힘
    private void CancelDrag()
    {
        if (!_dragging && !_mouseDown) return;
        _dragging = false;
        _mouseDown = false;
        Cat.ReleaseMouseCapture();
        _perch = IntPtr.Zero;
        _jumpY = 0;
        _jumpVy = 0;
        _vx = 0;
        _state = CatState.Sit;
        _stateTicks = 120;
    }

    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
}
