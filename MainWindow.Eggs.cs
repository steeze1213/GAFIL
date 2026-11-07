// v1.6 이스터에그 (전부 PC 안에서만, 토큰 0)
// 1. 레이저 포인터: 고양이 가까이에서 마우스 커서를 좌우로 빠르게 흔들면 → 웅크려 엉덩이를 씰룩이다가 커서 쪽으로 덮침
// 2. 한밤중 우다다: 밤 11시 40분~0시 40분 사이 하루 한 번 → 갑자기 화면을 전력 질주하고 이리저리 뛰다가 시치미
// 3. 츄르: 입력창에 "츄르"만 쓰고 Enter → Claude에게 보내지 않고 하트를 날리며 골골
// 4. 고양이의 날: 8월 8일(세계 고양이의 날)·9월 9일(한국 고양이의 날)엔 파티 모자 + 첫 인사
// 5. 귀찮게 하기: 고양이를 1.5초 안에 5번 클릭하면 → 털을 세우고 하악! 하고 도망감
using System.Windows;
using System.Windows.Media;

namespace ClaudeDesk;

public partial class MainWindow
{
    // 하악질: 등을 둥글게 세우고 털을 부풀림, 꼬리는 위로 빳빳, 입을 벌림
    private static readonly string[] HissFrame =
    {
        ".K.K............",
        "KOKOK...........",
        ".KOOK.KKKKK.....",
        "..KOKKODODOK....",
        "..KOKODODODOK.K.",
        "...KODODODOOKKOK",
        "...KOOOOOOOKOOOK",
        "...KOOOOOOKOEOEK",
        "....KOKKOKKLKPZK",
        "....KOK.KOKKPPK.",
        "....KOK.KOK.KK..",
        "....KK..KK......",
    };

    private enum EggKind { None, Stalk, Zoom, Hiss }
    private EggKind _egg;
    private int _eggTicks;
    private double _pounceX;

    // 레이저 포인터: 커서가 고양이 앞에서 좌우로 방향을 바꾼 때(틱)
    private readonly List<int> _cursorTurns = new();
    private double _lastCursorX = double.NaN, _lastTurnX;
    private int _lastCursorDir, _laserCooldown;

    // 귀찮게 하기: 최근 클릭 시각
    private readonly List<DateTime> _catClicks = new();

    // 츄르 하트 (남은 틱)
    private int _heartTicks;

    // 우다다: 오늘 밤 할 시각, 이미 한 밤
    private DateTime _zoomAt = DateTime.MinValue;
    private DateTime _zoomNight = DateTime.MinValue;
    private DateTime _zoomDoneNight = DateTime.MinValue;

    private DateTime _catDayGreeted = DateTime.MinValue;

    private static bool IsCatDay => DateTime.Now is { Month: 8, Day: 8 } or { Month: 9, Day: 9 };

    // 이스터에그를 시작해도 되는지 (알림·여행·화면 조작 등 다른 일 중이면 안 함)
    private bool EggFree => !AlertActive && !OnTrip && !_runningToRemind && !_claude.ScreenActive && !_dragging && !_bubbleOpen && !_menuOpen;

    // OnTick에서 매 틱: 이스터에그 중이면 고양이를 움직이고 true. 공중이면 false → 평소 착지 처리
    private bool UpdateEggs(Rect wa)
    {
        if (_laserCooldown > 0) _laserCooldown--;
        if (_egg == EggKind.None)
        {
            if (EggFree && _state != CatState.Jump) WatchLaser(wa);
            return false;
        }
        if (_state == CatState.Jump) { if (_egg == EggKind.Zoom) _eggTicks--; return false; }

        switch (_egg)
        {
            case EggKind.Stalk:
            {
                // 웅크리고 엉덩이 씰룩씰룩 (몸을 좌우로 1픽셀씩), 커서를 계속 노려봄
                _state = CatState.Crouch;
                if (CursorDip(out double cx, out _)) { _pounceX = cx; _dir = cx >= _centerX ? 1 : -1; }
                _centerX += (_eggTicks / 3) % 2 == 0 ? 0.8 : -0.8;
                if (--_eggTicks > 0) return true;
                // 덮치기: 같은 높이로 착지하도록 포물선 (약 0.5초)
                double d = Math.Clamp(_pounceX - _centerX, -320, 320);
                TakeOff(wa);
                _jumpVy = 4.5;
                _vx = Math.Clamp(d / (2 * 4.5 / Gravity), -8, 8);
                _leaping = true;
                _state = CatState.Jump;
                _egg = EggKind.None;
                _laserCooldown = 90;
                return false;
            }
            case EggKind.Zoom:
            {
                if (--_eggTicks <= 0)
                {
                    // 시치미: 아무 일 없었다는 듯 앉음
                    _egg = EggKind.None;
                    _state = CatState.Sit;
                    _stateTicks = 240;
                    return true;
                }
                var (a, b) = StandRange(wa);
                if (_centerX <= a + 1 || _centerX >= b - 1)
                {
                    // 끝에 닿음: 창 위라면 반쯤은 그대로 뛰어내림, 아니면 홱 돌아섬
                    if (Perched && _rnd.Next(2) == 0) { StepOffPerch(); _vx *= 1.6; _leaping = true; return false; }
                    _dir = _centerX <= a + 1 ? 1 : -1;
                }
                if (_rnd.Next(100) < 3)
                {
                    // 갑자기 높이 점프 (중간에 창 윗변이 있으면 거기 착지)
                    TakeOff(wa);
                    _jumpVy = 6 + _rnd.NextDouble() * 3;
                    _vx = _dir * 5;
                    _leaping = true;
                    _state = CatState.Jump;
                    return false;
                }
                _state = CatState.Walk;
                if (++_walkTick % 4 == 0) _legFrameB = !_legFrameB;
                _centerX = Math.Clamp(_centerX + _dir * 5, a, b);
                return true;
            }
            case EggKind.Hiss:
            {
                _state = CatState.Hiss;
                if (--_eggTicks > 0) return true;
                // 도망: 커서 반대쪽으로 크게 뜀 (창 위로 올라갈 수도)
                if (CursorDip(out double cx, out _)) _dir = cx >= _centerX ? -1 : 1;
                TakeOff(wa);
                _jumpVy = 7;
                _vx = _dir * 4.5;
                _leaping = true;
                _state = CatState.Jump;
                _egg = EggKind.None;
                return false;
            }
        }
        return false;
    }

    // ---------------- 1. 레이저 포인터 ----------------

    // 커서가 고양이 앞(좌우 260, 위 200 안쪽)에서 1.2초 안에 4번 넘게 좌우로 방향을 바꾸면 사냥 시작
    private void WatchLaser(Rect wa)
    {
        if (!CursorDip(out double x, out double y)) return;
        double feet = GroundY(wa) - _jumpY;
        bool near = Math.Abs(x - _centerX) < 260 && y > feet - CatHeight - 200 && y < feet + 40;
        if (!near) { _cursorTurns.Clear(); _lastCursorX = x; return; }
        if (!double.IsNaN(_lastCursorX))
        {
            double dx = x - _lastCursorX;
            if (Math.Abs(dx) > 1)
            {
                int dir = Math.Sign(dx);
                if (dir != _lastCursorDir && Math.Abs(x - _lastTurnX) >= 20)
                {
                    _cursorTurns.Add(_animTick);
                    _lastTurnX = x;
                }
                if (dir != _lastCursorDir) _lastCursorDir = dir;
            }
        }
        _lastCursorX = x;
        _cursorTurns.RemoveAll(t => _animTick - t > 72);
        if (_cursorTurns.Count >= 4 && _laserCooldown == 0)
        {
            _cursorTurns.Clear();
            _egg = EggKind.Stalk;
            _eggTicks = 60;   // 약 1초 웅크리고 씰룩
            _pounceX = x;
            AppSettings.Log("[이스터에그] 레이저 포인터");
        }
    }

    // 마우스 커서 위치 (지금 창의 배율 기준 DIP)
    private bool CursorDip(out double x, out double y)
    {
        x = y = 0;
        if (!GetCursorPos(out var p)) return false;
        var dpi = VisualTreeHelper.GetDpi(this);
        x = p.X / dpi.DpiScaleX;
        y = p.Y / dpi.DpiScaleY;
        return true;
    }

    // ---------------- 2. 한밤중 우다다 ----------------

    // ChooseNextState에서 부름: 오늘 밤 정해 둔 시각(11시 40분~0시 40분 중 무작위)이 지났으면 한 번 우다다
    private bool TryStartZoomies()
    {
        var now = DateTime.Now;
        var night = (now.Hour < 12 ? now.Date.AddDays(-1) : now.Date);   // 이 밤이 시작된 날
        if (_zoomNight != night)
        {
            _zoomNight = night;
            _zoomAt = night.AddHours(23).AddMinutes(40 + _rnd.Next(60));
        }
        if (_zoomDoneNight == night || now < _zoomAt || now > _zoomAt.AddMinutes(30) || !EggFree) return false;
        _zoomDoneNight = night;
        _egg = EggKind.Zoom;
        _eggTicks = 420;   // 약 7초
        _dir = _rnd.Next(2) == 0 ? -1 : 1;
        AppSettings.Log("[이스터에그] 한밤중 우다다");
        return true;
    }

    // ---------------- 3. 츄르 ----------------

    // 입력창에 "츄르"만 썼으면 Claude에게 보내지 않고 골골 + 하트. 처리했으면 true
    private bool TryChuru()
    {
        string t = Input.Text.Trim().TrimEnd('!', '~', '.', ' ').ToLowerInvariant();
        if (t is not ("츄르" or "츄르줄게" or "churu" or "ちゅーる")) return false;
        if (_attachedFiles.Count > 0 || _attachedText != null) return false;
        Input.Text = "";
        AddUserLine("츄르");
        AddCatLine(L.T("츄르다!! 냠냠냠… 골골골골 ♥\n(이건 Claude한테 안 보냈어. 토큰 0)", "Churu!! Nom nom nom… purrrr ♥\n(Not sent to Claude. Zero tokens.)"));
        ChatScroll.ScrollToEnd();
        _heartTicks = 300;   // 약 5초
        KneadNow();          // 하트 보내며 꾹꾹이 (MainWindow.Cozy.cs)
        CatMeow.TryMeow(_settings.Meow, force: true);
        AppSettings.Log("[이스터에그] 츄르");
        return true;
    }

    // 말풍선이 열려 있어서 고양이가 멈춘 동안에도 하트는 움직이게 (OnTick에서 부름)
    private void TickHearts()
    {
        if (_heartTicks <= 0) return;
        _heartTicks--;
        _animTick++;
        UpdateSprite();
    }

    // ---------------- 4. 고양이의 날 ----------------

    private void GreetCatDay()
    {
        if (!IsCatDay || _catDayGreeted == DateTime.Today) return;
        _catDayGreeted = DateTime.Today;
        bool korea = DateTime.Now.Month == 9;
        AddCatLine(korea
            ? L.T("오늘은 한국 고양이의 날이야냥! 🎉 모자 어때?", "It's Korean Cat Day today! 🎉 Like my hat?")
            : L.T("오늘은 세계 고양이의 날이야냥! 🎉 모자 어때?", "It's International Cat Day! 🎉 Like my hat?"));
    }

    // ---------------- 5. 귀찮게 하기 ----------------

    // 고양이를 클릭할 때마다 부름: 1.5초 안에 5번째 클릭이면 하악질하고 true (그 클릭은 말풍선을 열지 않음)
    private bool RegisterCatClick()
    {
        var now = DateTime.Now;
        _catClicks.RemoveAll(t => now - t > TimeSpan.FromSeconds(1.5));
        _catClicks.Add(now);
        if (_catClicks.Count < 5 || AlertActive || _claude.ScreenActive || Thinking || _state is CatState.Jump or CatState.Climb) return false;
        _catClicks.Clear();
        if (_bubbleOpen) ToggleBubble();
        CancelTrip();
        _egg = EggKind.Hiss;
        _eggTicks = 55;   // 약 1초 하악
        if (CursorDip(out double cx, out _)) _dir = cx >= _centerX ? 1 : -1;
        _state = CatState.Hiss;
        CatMeow.TryHiss(_settings.Meow);
        AppSettings.Log("[이스터에그] 하악!");
        UpdateSprite();
        return true;
    }

    // ---------------- 그림 덧그리기 ----------------

    // 츄르 하트: 머리 왼쪽 빈칸에서 두 개가 번갈아 위로 떠오름
    private void AddEggOverlay(List<(int X, int Y, char C)> dots)
    {
        if (_heartTicks <= 0) return;
        (int, int)[] heart = { (0, 0), (2, 0), (0, 1), (1, 1), (2, 1), (1, 2) };
        for (int k = 0; k < 2; k++)
        {
            int phase = (300 - _heartTicks) / 8 + k * 3;
            int y = 4 - phase % 6;          // 아래에서 위로
            int x = k == 0 ? 2 : 5;
            if (y < -2) continue;
            foreach (var (hx, hy) in heart)
                if (y + hy >= 0) dots.Add((x + hx, y + hy, 'H'));
        }
    }
}
