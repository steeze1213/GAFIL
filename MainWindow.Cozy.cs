// 고양이다운 행동 세 가지 (토큰 0)
// 1. 들어맞으면 들어간다: 작은 창이 새로 뜨면 그 위로 올라가 상자에 들어간 것처럼 머리만 내밂
// 2. 그루밍: 끌려다닌 뒤(가끔은 그냥) 앞발을 핥으며 정리
// 3. 꾹꾹이: 말풍선을 열어 두고 한동안 조용하면, 또 츄르를 먹은 뒤 앞발로 꾹꾹

using System.Text;
using System.Windows.Media;

namespace ClaudeDesk;

public partial class MainWindow
{
    // ---- 그림 (오른쪽을 보는 기준) ----
    private static readonly string[] BoxA =      // 창(상자) 윗변에 앞발을 걸치고 머리만 내밂. 맨 아래 행이 윗변에 겹침
    {
        "................",
        "................",
        "................",
        "................",
        "..........K...K.",
        ".........KPK.KPK",
        "..K......KOOOOOK",
        ".KOK.....KOEOEOK",
        ".KDK.....KLOPOLK",
        ".KOK.....KKOOOKK",
        ".KOK....KLLKKKLL",
        "KKKKKKKKKKKKKKKK",
    };
    private static readonly string[] BoxB = BoxA.Take(6).Concat(new[]   // 꼬리 끝을 살랑
    {
        "...K.....KOOOOOK",
        "..KOK....KOEOEOK",
        "..KDK....KLOPOLK",
        ".KOK.....KKOOOKK",
        ".KOK....KLLKKKLL",
        "KKKKKKKKKKKKKKKK",
    }).ToArray();
    private static readonly string[] GroomA =    // 눈 감고 앞발을 입가로 올림
    {
        "..........K...K.",
        ".........KPK.KPK",
        ".........KOOOOOK",
        ".........KOKOKOK",
        ".........KLOPOLK",
        "........KKOOKLLK",
        ".......KODOOKLLK",
        "......KODOOLKLK.",
        ".KK..KOOOOOLKLK.",
        "KOOK.KOOOOOOOK..",
        ".KOOKKOOKKOOOK..",
        "..KKKKKKKKKKKK..",
    };
    private static readonly string[] GroomB =    // 앞발을 내리고 혀로 핥음
    {
        "..........K...K.",
        ".........KPK.KPK",
        ".........KOOOOOK",
        ".........KOKOKOK",
        ".........KLOPOLK",
        "........KKOOOPK.",
        ".......KODOOLLK.",
        "......KODOOKLLK.",
        ".KK..KOOOOOKLLK.",
        "KOOK.KOOOOOOOK..",
        ".KOOKKOOKKOOOK..",
        "..KKKKKKKKKKKK..",
    };
    private static readonly string[] KneadA =    // 눈 감고 앞발을 번갈아 꾹꾹
    {
        "................",
        "..........K...K.",
        ".........KPK.KPK",
        ".........KOOOOOK",
        ".........KOKOKOK",
        ".........KLOPOLK",
        ".K......KKOOOOK.",
        "KOK...KKODOOLLK.",
        "KOK..KODOOOLLLK.",
        ".KOKKOOOOOOOKLK.",
        "..KOOOOOOOOKLKK.",
        "...KKKKKKKKKKLLK",
    };
    private static readonly string[] KneadB = KneadA.Take(9).Concat(new[]
    {
        ".KOKKOOOOOOOOKLK",
        "..KOOOOOOOOOKKLK",
        "...KKKKKKKKKLLK.",
    }).ToArray();

    private static readonly TimeSpan KneadAfter = TimeSpan.FromSeconds(25);   // 말풍선을 열어 두고 이만큼 조용하면 꾹꾹이
    private static readonly TimeSpan KneadFor = TimeSpan.FromSeconds(8);

    private bool _groomNext;                  // 다음 행동은 그루밍 (방금 끌려다님)
    private DateTime _bubbleQuietSince = DateTime.Now;
    private bool _bubbleKneading;
    private IntPtr _boxTarget;                // 상자처럼 들어가려고 가는 창
    private int _boxTries;
    private IntPtr _boxCandidate;             // 방금 새로 뜬 작은 창
    private DateTime _boxSeenAt;
    private IntPtr _lastFg;
    private HashSet<IntPtr> _knownWins;       // 이미 있던 창들 (처음 본 창 = 새로 뜬 창)
    private int _boxScanTick;

    // ---------------- 꾹꾹이 (말풍선이 열려 있을 때) ----------------

    // 말풍선에서 무언가 하면(열기·입력·답 기다림) 조용한 시간을 처음부터
    private void BubbleActivity() { _bubbleQuietSince = DateTime.Now; _bubbleKneading = false; }

    // 말풍선이 열려 있는 동안 매 틱 (OnTick): 한동안 조용하면 잠깐 꾹꾹이
    private void TickBubbleCozy()
    {
        if (Thinking || _menuOpen) _bubbleQuietSince = DateTime.Now;
        var quiet = DateTime.Now - _bubbleQuietSince;
        bool knead = _bubbleOpen && quiet >= KneadAfter && quiet < KneadAfter + KneadFor;
        if (knead || _bubbleKneading) { _animTick++; _bubbleKneading = knead; UpdateSprite(); }
    }

    // 츄르를 먹으면 바로 꾹꾹이
    private void KneadNow() => _bubbleQuietSince = DateTime.Now - KneadAfter;

    // ---------------- 새로 뜬 작은 창 알아채기 ----------------

    // 1초마다 (OnTick): 맨 앞 창이 바뀌었고 처음 보는 작은 창이면 기억해 둠. 다른 때는 아무것도 안 함
    private void WatchNewWindows()
    {
        if (++_boxScanTick % 60 != 0) return;
        if (_knownWins == null)
        {
            _knownWins = new HashSet<IntPtr>();
            EnumWindows((h, _) => { _knownWins.Add(h); return true; }, IntPtr.Zero);
            return;
        }
        IntPtr fg = GetForegroundWindow();
        if (fg == _lastFg || fg == IntPtr.Zero) return;
        _lastFg = fg;
        if (!_knownWins.Add(fg)) return;                 // 원래 있던 창
        if (_knownWins.Count > 4000) _knownWins.Clear();  // 오래 켜 두면 가끔 비움 (그다음 처음 보는 창도 그냥 새 창으로 봄)
        if (!Candidate(fg, (uint)Environment.ProcessId, out var r, out bool plat) || !plat) return;
        var dpi = VisualTreeHelper.GetDpi(this);
        double w = (r.Right - r.Left) / dpi.DpiScaleX, h = (r.Bottom - r.Top) / dpi.DpiScaleY;
        if (w > 640 || h > 520) return;                  // 상자라기엔 큼
        if (_settings.BodyAlerts && IsAlertDialog(fg)) return;   // 알림 창은 몸짓 알림이 맡음
        _boxCandidate = fg;
        _boxSeenAt = DateTime.Now;
        // 바닥에서 쉬는 중이면 지금 하던 걸 멈추고 곧 보러 감
        if (!Perched && !OnTrip && _state is CatState.Walk or CatState.Sit or CatState.Lie or CatState.Belly or CatState.Groom or CatState.Knead)
            _stateTicks = Math.Min(_stateTicks, 20);
        AppSettings.Log("[상자] 작은 창이 새로 뜸");
    }

    // 다음 행동을 고를 때: 방금 새로 뜬 작은 창이 있으면 그 위로 가 봄
    private bool TryStartBox()
    {
        if (_boxCandidate == IntPtr.Zero) return false;
        var h = _boxCandidate;
        _boxCandidate = IntPtr.Zero;
        if (DateTime.Now - _boxSeenAt > TimeSpan.FromSeconds(20)) return false;
        if (!_settings.ClimbWindows || Perched || OnTrip || _runningToRemind || _claude.ScreenActive || AlertActive) return false;
        if (_rnd.Next(100) >= 70) { AppSettings.Log("[상자] 이번엔 관심 없음"); return false; }   // 늘 그러진 않음
        var wa = WorkArea;
        RefreshPlatforms(wa, force: true);
        _leapUp = MaxLeapUp;
        var goal = _platforms.FirstOrDefault(p => p.Hwnd == h);
        if (goal.Hwnd == IntPtr.Zero) { AppSettings.Log("[상자] 올라설 자리가 없음"); return false; }
        var path = FindPath(wa, goal);
        if (path == null) { AppSettings.Log("[상자] 갈 길이 없음"); return false; }
        _boxTarget = h;
        _boxTries = 0;
        StartTrip(path);
        AppSettings.Log("[상자] 새 창으로 올라가는 중");
        return true;
    }

    // 창 위에서 다음 행동을 고를 때: 상자 창에 도착했으면 들어가 앉음. 처리했으면 true
    private bool TrySitInBox(bool clearOfButtons)
    {
        if (_boxTarget == IntPtr.Zero) return false;
        if (_perch != _boxTarget || ++_boxTries > 3) { _boxTarget = IntPtr.Zero; return false; }
        if (!clearOfButtons)
        {
            // 닫기 버튼 앞이면 버튼을 비켜설 만큼 왼쪽으로 걸어간 뒤 다시. 창이 너무 좁아 비킬 자리가 없으면 그만둠
            double right = _perchRect.Right / VisualTreeHelper.GetDpi(this).DpiScaleX - CaptionButtons;
            double need = _centerX + CatWidth / 2 - right + 8;
            if (right - CatWidth < _perchX1 + 4) { _boxTarget = IntPtr.Zero; return false; }
            _state = CatState.Walk; _dir = -1;
            _stateTicks = (int)Math.Ceiling(need / CurrentWalkSpeed) + 4;
            return true;
        }
        _boxTarget = IntPtr.Zero;
        _state = CatState.Box;
        _stateTicks = _rnd.Next(600, 1100);   // 약 10~18초
        return true;
    }

    // 끌려다닌 뒤 내려앉으면 그루밍. 처리했으면 true
    private bool TryGroomAfterDrag()
    {
        if (!_groomNext) return false;
        _groomNext = false;
        _state = CatState.Groom;
        _stateTicks = _rnd.Next(150, 240);   // 약 2.5~4초
        return true;
    }

}
