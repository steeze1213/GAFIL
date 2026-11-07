// v1.4 기능: 창 위로 올라가기
// - 화면에 열린 창의 '윗변'이 발판이 됨. 가끔 고양이가 창 위로 올라가서 걷고, 엎드려 꼬리를 늘어뜨리고, 아래를 내려다봄
// - 올라가는 방법 (쉬운 것부터): ① 바로 점프 ② 낮은 창을 밟고 차례로 점프 ③ 창 옆변을 타고 기어오름
// - 올라가 있는 창을 옮기면 같이 따라가고, 창을 닫거나 최소화하거나 다른 창이 그 위를 덮으면 아래로 떨어짐
// - 떨어지거나 뛰어내릴 때도 중간에 다른 창 윗변이 있으면 거기에 착지 (들어서 옮겨 놓을 때도 마찬가지)
// - 창의 모습(제목·내용)은 보지 않음. Windows에게 창의 위치만 물어봄 (개인정보 없음, API 사용 없음)
// 좌표: 창 위치는 실제 픽셀로 받아서 지금 고양이 창의 배율(DIP)로 바꿔 씀
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClaudeDesk;

public partial class MainWindow
{
    // ---- 새 모습 (오른쪽을 보는 기준) ----
    private static readonly string[] CrouchFrame =   // 점프 직전 웅크리기
    {
        "................",
        "................",
        "................",
        "..........K...K.",
        ".........KPK.KPK",
        ".K.......KOOOOOK",
        "KOK......KOEOEOK",
        "KOK.KKKKKKLOPOLK",
        ".KOKODOODOOOOOK.",
        "..KODOODOOOLLOK.",
        ".KOOOOOOOOOLLOK.",
        ".KKKKKKKKKKKKKK.",
    };
    private static readonly string[] LeapFrame =     // 뛰어오르는 중 (몸이 쭉 늘어남)
    {
        "..........K...K.",
        ".........KPK.KPK",
        ".........KOOOOOK",
        ".........KOEOEOK",
        ".........KLOPOLK",
        "K.......KKOOOOKK",
        "KOKKKKKKOOOOOKOK",
        ".KODOODOOOOLLOKK",
        "..KODOODOOLLOK..",
        "..KKOOOOOOKKK...",
        ".KOKKKKKKK......",
        ".KK.............",
    };
    private static readonly string[] LandFrame =     // 착지 (살짝 주저앉음)
    {
        "................",
        "................",
        "................",
        "................",
        "..........K...K.",
        ".........KPK.KPK",
        "K........KOOOOOK",
        "KOK......KOEOEOK",
        ".KOKKKKKKKLOPOLK",
        "..KODOODOOOOOOK.",
        ".KOOOOOOOOOLLLOK",
        "KKKKKKKKKKKKKKKK",
    };
    private static readonly string[] ClimbA =        // 창 옆변 타기 (등이 보임). B는 A를 좌우로 뒤집은 것 → 발이 번갈아 움직임
    {
        ".....K....K.....",
        "..K.KOK..KOK....",
        ".KOKKOOKKOOK....",
        ".KOKKOOOOOOK....",
        "..KOKOOOOOOKK...",
        "...KKKOOOOKKOK..",
        "....KODOODOKOK..",
        "....KOOOOOOKK...",
        "..KKKODOODOK....",
        ".KOOKKOOOOKKK...",
        "..KK..KOKKOOK...",
        "......KOK.KK....",
    };
    private static readonly string[] ClimbB = ClimbA.Select(r => new string(r.Reverse().ToArray())).ToArray();
    private static readonly string[] LedgeA =        // 창 위에 엎드려 꼬리를 창 앞으로 늘어뜨림 (8행 아래가 창 윗변)
    {
        "................",
        "..........K...K.",
        ".........KPK.KPK",
        ".........KOOOOOK",
        ".........KOEOEOK",
        "...KKKKKKKLOPOLK",
        "..KODOODOOOOOOK.",
        "..KODOODOOOLLLK.",
        "..KOKKKKKKKKKKK.",
        "..KOK...........",
        "..KDK...........",
        "...KK...........",
    };
    private static readonly string[] LedgeB = LedgeA.Take(9).Concat(new[]
    {
        "..KOK...........",
        "...KDK..........",
        "....KK..........",
    }).ToArray();
    private static readonly string[] PeekFrame =     // 창 윗변에서 머리를 내밀고 아래를 내려다봄
    {
        "................",
        "................",
        "................",
        "................",
        "................",
        "..KKKKKKK.K...K.",
        ".KODOODOOKPK.KPK",
        ".KODOODOOKOOOOOK",
        ".KKKKKKKKKOOOOOK",
        ".........KOEOEOK",
        ".........KLOPOLK",
        "..........KKKKK.",
    };
    private static readonly string[] BellyFrame =    // 배 보이며 드러눕기
    {
        "................",
        "................",
        "................",
        "..K.......K...K.",
        ".KOK...K.KPK.KPK",
        ".KOK..KOKKOOOOOK",
        "..KOK.KOKKOKOKOK",
        "..KLLKKLLKLOPOLK",
        ".KLLLLLLLLOOOOOK",
        "KOOLLLLLLOOOOOK.",
        ".KKKKKKKKKKKKK..",
        "................",
    };

    private const int LedgeSinkRows = 3;           // 엎드림·내려다보기 그림은 아래 3행이 창 윗변 아래(창 앞)로 내려옴
    private const double MaxLeapUp = 240;          // 한 번에 뛰어오를 수 있는 높이 (DIP)
    private const double MaxLeapGap = 320;         // 한 번에 건너뛸 수 있는 가로 틈 (DIP)
    private const double ClimbSpeed = 0.9;         // 벽 타기 속도 (DIP/틱)
    private const double CaptionButtons = 170;     // 창 오른쪽 위 최소화·최대화·닫기 버튼 자리 (가리지 않기)

    // 발판: 창 윗변 중 다른 창에 가리지 않은 구간. Top·X1·X2는 DIP
    private readonly record struct Platform(IntPtr Hwnd, double Top, double X1, double X2, double WinLeft, double WinRight);

    private IntPtr _perch;              // 올라가 있는 창 (Zero = 바닥)
    private RECT _perchRect;            // 그 창의 마지막 위치 (실제 픽셀)
    private double _perchX1, _perchX2;  // 지금 서 있는 구간 (DIP)
    private double _vx;                 // 공중에서 가로 속도
    private bool _leaping;              // 창으로 뛰어오르는 중 (도약 그림)
    private IntPtr _ignoreLand;         // 이번에 뛰어내린 창 (다시 그 위로 착지하지 않게)
    private List<Platform> _platforms = new();
    private int _scanTick;
    private DateTime _nextExplore = DateTime.Now.AddSeconds(40);

    // 창 위로 가는 여행: 밟을 창 순서. 마지막은 목표 창
    private enum TripPhase { None, WalkTo, Crouch, Air, Land, Climb }
    private readonly Queue<(IntPtr Target, bool Climb)> _trip = new();
    private (IntPtr Target, bool Climb) _hop;
    private TripPhase _phase;
    private int _phaseTicks;
    private double _climbX;             // 벽 타기: 고양이 중심이 붙을 창 옆변 X (DIP)

    private bool Perched => _perch != IntPtr.Zero;
    private bool OnTrip => _phase != TripPhase.None;

    private double PerchTopDip => _perchRect.Top / VisualTreeHelper.GetDpi(this).DpiScaleY;

    // 고양이가 서 있는 높이(창 아래쪽이 닿는 Y, DIP): 창 위면 그 창의 윗변, 아니면 바닥
    private double GroundY(Rect wa) => Perched ? PerchTopDip : wa.Bottom;

    // ---------------- 창 위에서 말풍선 ----------------

    private bool _bubbleBelow;   // 말풍선을 고양이 아래로 여는 중 (창 위 높은 곳이라 위에 자리가 없음)

    // 말풍선을 아래로 열어야 하는지: 창 위에 있고, 고양이 위로 보통 크기 말풍선이 들어갈 자리가 없으면
    private bool WantBubbleBelow(Rect wa) => _bubbleOpen && Perched && GroundY(wa) - OpenHeight < wa.Top;

    // 크게 보기에서 쓸 수 있는 창 높이: 위로 열면 화면 위쪽 끝까지, 아래로 열면 화면 아래쪽 끝까지
    private double BubbleSpace(Rect wa) => _bubbleBelow ? wa.Bottom - (GroundY(wa) - CatHeight) : GroundY(wa) - wa.Top;

    // 말풍선 위치 바꾸기: 아래로 열면 고양이가 맨 위 줄, 말풍선은 그 아래(꼬리는 위를 가리킴)
    private void SetBubbleBelow(bool below)
    {
        if (below == _bubbleBelow) return;
        _bubbleBelow = below;
        Grid.SetRow(Cat, below ? 0 : 2);
        Grid.SetRow(Bubble, below ? 2 : 0);
        RowTop.Height = below ? GridLength.Auto : new GridLength(1, GridUnitType.Star);
        RowBottom.Height = below ? new GridLength(1, GridUnitType.Star) : GridLength.Auto;
        Cat.VerticalAlignment = below ? VerticalAlignment.Top : VerticalAlignment.Stretch;
        Bubble.VerticalAlignment = below ? VerticalAlignment.Top : VerticalAlignment.Bottom;
        Bubble.Margin = below ? new Thickness(0, 2, 0, 0) : new Thickness(0, 0, 0, 2);
        // 꼬리: 위로 열 땐 말풍선 아래에서 아래를, 아래로 열 땐 말풍선 위에서 위를 가리킴
        Bubble.Children.Remove(BubbleTail);
        if (below) Bubble.Children.Insert(0, BubbleTail); else Bubble.Children.Add(BubbleTail);
        BubbleTail.Data = Geometry.Parse(below ? "M0,9 L9,0 L18,9" : "M0,0 L9,9 L18,0");
        BubbleTail.Margin = below ? new Thickness(0, 0, 0, -2.5) : new Thickness(0, -2.5, 0, 0);
        if (_expanded) SetExpanded(true, apply: false);   // 쓸 수 있는 높이가 달라짐
    }

    // 엎드림·내려다보기 그림은 아래 몇 행이 윗변 아래로 내려오도록 창을 그만큼 내림
    private double SinkFor(string[] frame) =>
        !_bubbleOpen && Perched && (ReferenceEquals(frame, LedgeA) || ReferenceEquals(frame, LedgeB) || ReferenceEquals(frame, PeekFrame))
            ? LedgeSinkRows * Cell : 0;

    // ---------------- 창 찾기 ----------------

    // 고양이가 사는 모니터에서 올라설 수 있는 창 윗변 구간들 (위에 있는 창이 덮은 부분은 뺌)
    private List<Platform> ScanPlatforms(Rect wa)
    {
        var dpi = VisualTreeHelper.GetDpi(this);
        double sx = dpi.DpiScaleX, sy = dpi.DpiScaleY;
        var wins = new List<(IntPtr H, RECT R, bool Plat)>();   // 맨 위 창부터 순서대로
        uint myPid = (uint)Environment.ProcessId;
        EnumWindows((h, _) =>
        {
            if (Candidate(h, myPid, out var r, out bool plat)) wins.Add((h, r, plat));
            return true;
        }, IntPtr.Zero);

        var list = new List<Platform>();
        for (int i = 0; i < wins.Count; i++)
        {
            if (!wins[i].Plat) continue;
            var r = wins[i].R;
            double top = r.Top / sy;
            if (top - CatHeight - 6 < wa.Top || top > wa.Bottom - 40) continue;   // 위에 고양이가 설 자리가 없거나 바닥에 너무 가까움
            double left = r.Left / sx, right = r.Right / sx;
            var segs = new List<(double A, double B)> { (Math.Max(left, wa.Left), Math.Min(right, wa.Right)) };
            for (int j = 0; j < i; j++)
            {
                var o = wins[j].R;
                if (o.Top > r.Top + 1 || o.Bottom <= r.Top) continue;   // 윗변 높이를 덮지 않음
                double oa = o.Left / sx, ob = o.Right / sx;
                segs = segs.SelectMany(s => Cut(s, oa, ob)).ToList();
            }
            foreach (var s in segs)
                if (s.B - s.A >= CatWidth + 10) list.Add(new Platform(wins[i].H, top, s.A, s.B, left, right));
        }
        return list;
    }

    private static IEnumerable<(double A, double B)> Cut((double A, double B) s, double a, double b)
    {
        if (b <= s.A || a >= s.B) { yield return s; yield break; }
        if (a > s.A) yield return (s.A, a);
        if (b < s.B) yield return (b, s.B);
    }

    // 보통 프로그램 창인지 (보이고, 최소화 안 됨, 내 창 아님, 도구 창 아님, 제목 있음, 너무 작지 않음)
    // plat = 발판이 될 수 있는지 (최대화·전체 화면 창은 가리기만 하고 발판은 아님)
    private static bool Candidate(IntPtr h, uint myPid, out RECT r, out bool plat)
    {
        r = default; plat = false;
        if (!IsWindowVisible(h) || IsIconic(h)) return false;
        if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0) return false;   // 다른 가상 데스크톱 등
        GetWindowThreadProcessId(h, out uint pid);
        if (pid == myPid) return false;
        long ex = GetWindowLongPtr(h, GWL_EXSTYLE).ToInt64();
        long st = GetWindowLongPtr(h, GWL_STYLE).ToInt64();
        if ((ex & WS_EX_TOOLWINDOW) != 0 || (st & WS_CHILD) != 0) return false;
        if (GetWindowTextLength(h) == 0) return false;
        var cls = new StringBuilder(64);
        GetClassName(h, cls, cls.Capacity);
        string c = cls.ToString();
        if (c is "Progman" or "WorkerW" or "Shell_TrayWnd" or "Shell_SecondaryTrayWnd") return false;
        if (DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf<RECT>()) != 0 && !GetWindowRect(h, out r)) return false;
        if (r.Right - r.Left < 150 || r.Bottom - r.Top < 80) return false;
        plat = !IsZoomed(h);
        return true;
    }

    // 창이 아직 발판으로 쓸 수 있는 상태인지 + 지금 위치
    private static bool PerchAlive(IntPtr h, out RECT r)
    {
        r = default;
        if (!IsWindow(h) || !IsWindowVisible(h) || IsIconic(h) || IsZoomed(h)) return false;
        if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0) return false;
        return DwmGetWindowAttribute(h, DWMWA_EXTENDED_FRAME_BOUNDS, out r, Marshal.SizeOf<RECT>()) == 0 || GetWindowRect(h, out r);
    }

    private void RefreshPlatforms(Rect wa, bool force = false)
    {
        if (!force && ++_scanTick % 6 != 0) return;   // 약 0.1초마다
        try { _platforms = ScanPlatforms(wa); } catch { _platforms = new(); }
    }

    // ---------------- 올라가 있는 창 따라가기 ----------------

    // 매 틱 (말풍선이 열려 있어도): 창이 움직이면 같이 움직이고, 사라지거나 덮이면 떨어짐
    private void TrackPerch()
    {
        if (!Perched) return;
        var wa = WorkArea;
        var dpi = VisualTreeHelper.GetDpi(this);
        if (!PerchAlive(_perch, out var r)) { FallFromPerch(wa); return; }

        double dx = (r.Left - _perchRect.Left) / dpi.DpiScaleX;
        _perchRect = r;
        if (dx != 0) { _centerX += dx; _perchX1 += dx; _perchX2 += dx; _climbX += dx; }

        double top = r.Top / dpi.DpiScaleY;
        double left = r.Left / dpi.DpiScaleX, right = r.Right / dpi.DpiScaleX;
        bool roomAbove = top - CatHeight - 6 >= wa.Top && top <= wa.Bottom - 40;
        if (!roomAbove) { FallFromPerch(wa); return; }
        // 말풍선이 열려 있는 동안은 대화 중이므로 웬만하면 그대로 둠 (창이 사라지거나 화면 끝으로 가면 그때만 내려옴).
        // 말풍선 폭 때문에 고양이가 창 끝에서 살짝 밀려났을 수도 있음
        if (_bubbleOpen) return;
        if (_centerX < left + 8 || _centerX > right - 8) { FallFromPerch(wa); return; }

        // 0.1초마다 다시 확인: 다른 창이 올라와 서 있는 곳을 덮었으면 떨어짐
        RefreshPlatforms(wa);
        if (_scanTick % 6 == 0)
        {
            var seg = _platforms.FirstOrDefault(p => p.Hwnd == _perch && _centerX >= p.X1 + 8 && _centerX <= p.X2 - 8);
            if (seg.Hwnd == IntPtr.Zero) { FallFromPerch(wa); return; }
            _perchX1 = seg.X1; _perchX2 = seg.X2;
        }
    }

    // 발판이 사라짐 → 그 높이에서 떨어지기 시작 (말풍선·메뉴가 열려 있어 멈춘 동안이면 바로 바닥으로)
    private void FallFromPerch(Rect wa)
    {
        double height = wa.Bottom - PerchTopDip;
        _ignoreLand = _perch;
        _perch = IntPtr.Zero;
        CancelTrip();
        if (_bubbleOpen || _menuOpen)
        {
            _jumpY = 0;
            _state = CatState.Sit;
            _stateTicks = 120;
            return;
        }
        _jumpY = Math.Max(0, height);
        _jumpVy = 0;
        _vx = 0;
        _state = CatState.Jump;
        AppSettings.Log("[창 위] 발판이 사라져서 떨어짐");
    }

    // 창 위에서 공중으로: 높이를 바닥 기준으로 바꿈 (공중에서는 항상 바닥 기준)
    private void TakeOff(Rect wa)
    {
        if (!Perched) return;
        _jumpY += wa.Bottom - PerchTopDip;
        _perch = IntPtr.Zero;
    }

    // 창 위에서 내려오기: 가까운 쪽 끝으로 걸어가서 뛰어내림 (지금은 그 자리에서 바깥쪽으로 폴짝)
    private void StepOffPerch()
    {
        if (!Perched) return;
        var wa = WorkArea;
        CancelTrip();
        double mid = (_perchX1 + _perchX2) / 2;
        _dir = _centerX >= mid ? 1 : -1;
        // 끝에서 가까우면 바깥으로, 아니면 그냥 앞으로 크게 뛰어서 넘어감
        double toEdge = _dir > 0 ? _perchX2 - _centerX : _centerX - _perchX1;
        _ignoreLand = _perch;
        TakeOff(wa);
        _vx = _dir * Math.Clamp(toEdge / 14, 2.2, 6);
        _jumpVy = 3;
        _state = CatState.Jump;
    }

    // ---------------- 공중 ----------------

    // 점프·떨어지기 한 틱: 위로 올라갔다 중력으로 내려오고, 내려오는 길에 창 윗변이나 바닥에 닿으면 착지
    private void UpdateAir(Rect wa)
    {
        TakeOff(wa);
        double half = HalfWidth();
        _centerX = Math.Clamp(_centerX + _vx, wa.Left + half, wa.Right - half);
        double prev = _jumpY;
        _jumpY += _jumpVy;
        _jumpVy -= Gravity;

        if (_jumpVy < 0 && _jumpY > 0)
        {
            RefreshPlatforms(wa);
            Platform? best = null;
            foreach (var p in _platforms)
            {
                if (p.Hwnd == _ignoreLand) continue;
                double h = wa.Bottom - p.Top;
                if (h > prev || h < _jumpY) continue;
                if (_centerX < p.X1 + 12 || _centerX > p.X2 - 12) continue;
                if (best == null || p.Top < best.Value.Top) best = p;
            }
            if (best is Platform b && PerchAlive(b.Hwnd, out var r))
            {
                _perch = b.Hwnd;
                _perchRect = r;
                _perchX1 = b.X1; _perchX2 = b.X2;
                _jumpY = 0;
                AppSettings.Log($"[창 위] 착지: 윗변 y={b.Top:0}, 구간 {b.X1:0}~{b.X2:0}");
                Landed(-_jumpVy);
                return;
            }
        }
        if (_jumpY <= 0)
        {
            _jumpY = 0;
            Landed(-_jumpVy);
        }
    }

    private void Landed(double speed)
    {
        _ignoreLand = IntPtr.Zero;
        _leaping = false;
        _vx = 0;
        if (OnTrip) { _state = CatState.Land; _phase = TripPhase.Land; _phaseTicks = 10; return; }
        if (speed > 4) { _state = CatState.Land; _stateTicks = 12; return; }   // 높은 데서 떨어지면 잠깐 주저앉음
        ChooseNextState();
    }

    // ---------------- 창 위로 가는 여행 ----------------

    // 바닥에서 가끔: 올라갈 창을 하나 골라 길을 찾음. 찾으면 true
    private bool TryStartExplore()
    {
        if (!_settings.ClimbWindows || Perched || _runningToRemind || _claude.ScreenActive) return false;
        if (DateTime.Now < _nextExplore) return false;
        _nextExplore = DateTime.Now.AddSeconds(_rnd.Next(60, 180));
        var wa = WorkArea;
        RefreshPlatforms(wa, force: true);
        var goals = _platforms.OrderBy(_ => _rnd.Next()).ToList();
        foreach (var g in goals)
        {
            var path = FindPath(wa, g);
            if (path == null) continue;
            StartTrip(path);
            AppSettings.Log($"[창 위] 올라가기 시작 ({path.Count}단계{(path[^1].Climb ? ", 벽 타기" : "")})");
            return true;
        }
        return false;
    }

    // 바닥에서 목표 창까지: 점프로 닿는 창들을 차례로 밟는 길(너비 우선 탐색). 없으면 목표 창 옆변 타기
    private List<(IntPtr Target, bool Climb)> FindPath(Rect wa, Platform goal)
    {
        var nodes = _platforms;
        var prev = new Dictionary<int, int>();   // 노드 번호 → 이전 노드 번호 (-1 = 바닥)
        var q = new Queue<int>();
        foreach (var (p, i) in nodes.Select((p, i) => (p, i)))
            if (CanLeap(wa, null, p)) { prev[i] = -1; q.Enqueue(i); }
        int goalIdx = nodes.IndexOf(goal);
        while (q.Count > 0 && !prev.ContainsKey(goalIdx))
        {
            int a = q.Dequeue();
            for (int b = 0; b < nodes.Count; b++)
                if (!prev.ContainsKey(b) && nodes[b].Hwnd != nodes[a].Hwnd && CanLeap(wa, nodes[a], nodes[b])) { prev[b] = a; q.Enqueue(b); }
        }
        if (prev.ContainsKey(goalIdx))
        {
            var path = new List<(IntPtr, bool)>();
            for (int i = goalIdx; i != -1; i = prev[i]) path.Insert(0, (nodes[i].Hwnd, false));
            return path;
        }
        // 벽 타기: 목표 창의 옆변이 덮이지 않고 화면 안에 있어야 함
        if (ClimbEdge(wa, goal) != null) return new List<(IntPtr, bool)> { (goal.Hwnd, true) };
        return null;
    }

    // a(null = 바닥)에서 b로 한 번에 뛸 수 있는지
    private static bool CanLeap(Rect wa, Platform? a, Platform b)
    {
        double ha = a == null ? 0 : wa.Bottom - a.Value.Top;
        double hb = wa.Bottom - b.Top;
        if (hb - ha > MaxLeapUp || hb < ha + 30) return false;   // 너무 높거나, 올라가는 길이 아님
        if (a == null) return true;                              // 바닥은 어디로든 걸어갈 수 있음
        double gap = Math.Max(0, Math.Max(b.X1 - a.Value.X2, a.Value.X1 - b.X2));
        return gap <= MaxLeapGap;
    }

    // 벽 타기에 쓸 옆변 X (DIP). 고양이에게 가까운 쪽 먼저. 그 변이 덮였거나 화면 밖이면 다른 쪽, 둘 다 안 되면 null
    private double? ClimbEdge(Rect wa, Platform g)
    {
        var sides = new List<double>();
        if (Math.Abs(g.X1 - g.WinLeft) < 2) sides.Add(g.X1);
        if (Math.Abs(g.X2 - g.WinRight) < 2) sides.Add(g.X2);
        foreach (var x in sides.OrderBy(x => Math.Abs(x - _centerX)))
            if (x - HalfWidth() >= wa.Left && x + HalfWidth() <= wa.Right) return x;
        return null;
    }

    private void StartTrip(List<(IntPtr Target, bool Climb)> path)
    {
        _trip.Clear();
        foreach (var h in path) _trip.Enqueue(h);
        NextHop();
    }

    private void NextHop()
    {
        if (_trip.Count == 0) { CancelTrip(); ChooseNextState(); return; }
        _hop = _trip.Dequeue();
        _phase = TripPhase.WalkTo;
        _phaseTicks = 2400;   // 40초 안에 못 가면 포기 (넓은 화면 끝에서 끝까지 걸어도 충분)
    }

    private void CancelTrip()
    {
        _trip.Clear();
        _phase = TripPhase.None;
        _leaping = false;
    }

    private Platform? FindPlatform(IntPtr h) =>
        _platforms.Where(p => p.Hwnd == h).OrderByDescending(p => p.X2 - p.X1).Cast<Platform?>().FirstOrDefault();

    // 여행 중 한 틱. 공중(점프 상태)은 UpdateAir가 처리
    private void UpdateTrip(Rect wa)
    {
        if (--_phaseTicks < -600) { AbortTrip(); return; }   // 안전장치 (벽 타던 중이면 떨어짐)
        RefreshPlatforms(wa);
        var target = FindPlatform(_hop.Target);
        if (target == null && _phase != TripPhase.Land) { AbortTrip(); return; }

        switch (_phase)
        {
            case TripPhase.WalkTo:
            {
                var t = target.Value;
                double goalX;
                if (_hop.Climb)
                {
                    if (ClimbEdge(wa, t) is not double ex) { AbortTrip(); return; }
                    goalX = ex;
                    _climbX = ex;
                }
                else goalX = LaunchX(wa, t);
                double d = goalX - _centerX;
                if (_phaseTicks <= 0) { AbortTrip(); return; }   // 너무 오래 걸림 (길이 막힘 등) → 포기
                if (Math.Abs(d) <= 1.5)
                {
                    _centerX = goalX;
                    if (_hop.Climb)
                    {
                        _state = CatState.Climb;
                        _phase = TripPhase.Climb;
                        _phaseTicks = (int)((wa.Bottom - t.Top) / ClimbSpeed) + 300;   // 다 오를 시간 + 여유
                    }
                    else { _state = CatState.Crouch; _phase = TripPhase.Crouch; _phaseTicks = 18; _dir = LandX(wa, t) >= _centerX ? 1 : -1; }
                    return;
                }
                _state = CatState.Walk;
                _dir = Math.Sign(d);
                if (++_walkTick % 7 == 0) _legFrameB = !_legFrameB;
                _centerX += Math.Sign(d) * Math.Min(Math.Abs(d), WalkSpeed * 1.6);
                return;
            }
            case TripPhase.Air:   // 공중인데 점프 상태가 아님 (말풍선을 열었다 닫음 등) → 그만둠
                AbortTrip();
                return;
            case TripPhase.Crouch:
                if (--_phaseTicks > 0) return;
                Leap(wa, target.Value);
                return;
            case TripPhase.Land:
                if (--_phaseTicks > 0) return;
                if (Perched && _perch == _hop.Target) { NextHop(); return; }
                AbortTrip();   // 엉뚱한 곳에 내림 (창이 움직였거나 길이 막힘)
                return;
            case TripPhase.Climb:
            {
                var t = target.Value;
                _centerX = _climbX;
                double h = wa.Bottom - t.Top;
                _jumpY += ClimbSpeed;
                if (_jumpY >= h - 4)
                {
                    // 꼭대기: 안쪽으로 폴짝 올라섬
                    _dir = Math.Abs(_climbX - t.X1) < Math.Abs(_climbX - t.X2) ? 1 : -1;
                    _vx = _dir * 1.8;
                    _jumpVy = 2.6;
                    _phase = TripPhase.Air;
                    _state = CatState.Jump;
                }
                return;
            }
        }
    }

    // 여행을 그만둠: 공중이면 떨어지고, 아니면 평소 행동
    private void AbortTrip()
    {
        bool climbing = _phase == TripPhase.Climb;
        CancelTrip();
        if (climbing && _jumpY > 0) { _jumpVy = 0; _vx = 0; _state = CatState.Jump; return; }
        if (_state != CatState.Jump) ChooseNextState();
    }

    // 지금 서 있는 곳에서 뛸 자리: 목표 창의 착지 지점과 가장 가까운, 서 있는 구간 안의 점
    private double LaunchX(Rect wa, Platform t)
    {
        var (a, b) = StandRange(wa);
        double land = (t.X1 + t.X2) / 2;
        if (land > a && land < b)
        {
            // 목표가 바로 위에 있으면 살짝 옆에서 비스듬히 뛰어오름
            double off = _centerX <= land ? -60 : 60;
            return Math.Clamp(land + off, a, b);
        }
        return Math.Clamp(land, a, b);
    }

    private double LandX(Rect wa, Platform t)
    {
        double m = CatWidth / 2 + 14;
        double x = Math.Clamp(_centerX + (_centerX < (t.X1 + t.X2) / 2 ? 50 : -50), t.X1 + m, t.X2 - m);
        return t.X2 - t.X1 < 2 * m ? (t.X1 + t.X2) / 2 : x;
    }

    // 지금 서 있는 곳에서 고양이 중심이 갈 수 있는 범위
    private (double A, double B) StandRange(Rect wa)
    {
        double half = HalfWidth();
        double a = wa.Left + half, b = wa.Right - half;
        if (Perched) { a = Math.Max(a, _perchX1 + CatWidth / 2); b = Math.Min(b, _perchX2 - CatWidth / 2); }
        return a <= b ? (a, b) : ((a + b) / 2, (a + b) / 2);
    }

    // 목표 창 윗변으로 포물선 점프: 꼭대기가 목표보다 조금 높게, 내려오면서 착지 지점에 닿도록 가로 속도를 맞춤
    private void Leap(Rect wa, Platform t)
    {
        double h0 = Perched ? wa.Bottom - PerchTopDip : 0;
        double h1 = wa.Bottom - t.Top;
        double landX = LandX(wa, t);
        double apex = Math.Max(h1 - h0, 0) + 36;
        double vy = Math.Sqrt(2 * Gravity * apex) + Gravity;
        double ticks = (vy + Math.Sqrt(Math.Max(0, vy * vy - 2 * Gravity * (h1 - h0)))) / Gravity;
        _ignoreLand = _perch;
        TakeOff(wa);
        _jumpVy = vy;
        _vx = Math.Clamp((landX - _centerX) / Math.Max(1, ticks), -7, 7);
        _dir = _vx >= 0 ? 1 : -1;
        _leaping = true;
        _phase = TripPhase.Air;
        _state = CatState.Jump;
    }

    // ---------------- 창 위에서의 행동 ----------------

    // 창 위에 있을 때 다음 행동: 걷기·앉기·엎드려 꼬리 늘어뜨리기·내려다보기·눕기·뛰어내리기
    private void ChoosePerchState()
    {
        int roll = _rnd.Next(100);
        bool clearOfButtons = _centerX + CatWidth / 2 < _perchRect.Right / VisualTreeHelper.GetDpi(this).DpiScaleX - CaptionButtons;
        if (roll < 30) { _state = CatState.Walk; _stateTicks = _rnd.Next(90, 240); _dir = _rnd.Next(2) == 0 ? -1 : 1; }
        else if (roll < 45) { _state = CatState.Sit; _stateTicks = _rnd.Next(120, 300); }
        else if (roll < 65 && clearOfButtons) { _state = CatState.Loaf; _stateTicks = _rnd.Next(300, 700); }
        else if (roll < 75 && clearOfButtons) { _state = CatState.Peek; _stateTicks = _rnd.Next(150, 300); }
        else if (roll < 82) { _state = CatState.Lie; _stateTicks = _rnd.Next(240, 480); }
        else if (roll < 97 || !clearOfButtons) StepOffPerch();
        else { _state = CatState.Sit; _stateTicks = 120; }
    }

    // ---------------- Win32 ----------------
    private delegate bool EnumWindowsProc(IntPtr h, IntPtr lp);
    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lp);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr h);
    [DllImport("user32.dll")] private static extern bool IsZoomed(IntPtr h);
    [DllImport("user32.dll")] private static extern int GetWindowTextLength(IntPtr h);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetClassName(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")] private static extern IntPtr GetWindowLongPtr(IntPtr h, int i);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int a, out RECT r, int size);
    [DllImport("dwmapi.dll")] private static extern int DwmGetWindowAttribute(IntPtr h, int a, out int v, int size);
    private const int GWL_STYLE = -16;   // GWL_EXSTYLE는 MainWindow.xaml.cs에 있음
    private const long WS_CHILD = 0x40000000, WS_EX_TOOLWINDOW = 0x80;
    private const int DWMWA_EXTENDED_FRAME_BOUNDS = 9, DWMWA_CLOAKED = 14;
}
