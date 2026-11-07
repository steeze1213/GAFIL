// 바탕화면 고양이 GAFIL: 모니터 아래쪽 모서리 위에서 걷기/앉기/눕기/점프를 반복함
// 클릭하면 말풍선이 열리고, 입력한 내용을 Claude(ClaudeClient)에게 보내 답을 실시간으로 보여줌
// 대화하지 않을 때는 고양이 위에 CPU/GPU 사용량을 표시함 (3단계)
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using System.Windows.Threading;

namespace ClaudeDesk;

public partial class MainWindow : Window
{
    // ---- 화면 설정 ----
    private const double Cell = 5;            // 한 칸 크기(픽셀)
    private const double CatWidth = 80;       // 16칸 x 5px
    private const double CatHeight = 60;      // 12칸 x 5px
    private const double IdleWidth = 200;     // 평소 창 폭: 시스템 정보 한 줄이 들어갈 만큼 (빈 곳은 투명이라 클릭이 통과함)
    private const double PillHeight = 22;     // 시스템 정보 한 줄 높이 (여백 포함)
    private const double OpenWidth = 340;     // 말풍선이 열렸을 때 창 폭
    private const double OpenHeight = 480;    // 말풍선이 열렸을 때 창 높이 (보통 크기)
    private const double ChatMaxNormal = 240; // 보통 크기일 때 대화 기록 최대 높이
    private const double BubbleChrome = 190;  // 말풍선에서 대화 기록을 뺀 나머지(머리줄·입력창·꼬리·여백) 높이

    // ---- 움직임 설정 ----
    private const double WalkSpeed = 1.2;     // 걷기 속도 (픽셀/틱)
    private const double JumpPower = 5.5;     // 점프 초기 속도 (위 방향)
    private const double Gravity = 0.3;       // 틱마다 위 방향 속도가 줄어드는 양

    // ---- 픽셀 스프라이트. 오른쪽을 보는 모습 ----
    // K=외곽선, O=몸통 주황, D=줄무늬, L=배·볼 크림색, P=귀 안쪽·코 분홍, E=눈, .=빈칸
    private static readonly string[] WalkA =
    {
        "..........K...K.",
        ".........KPK.KPK",
        ".........KOOOOOK",
        ".K.......KOEOEOK",
        "KOK......KLOPOLK",
        "KOK.KKKKKKOOOOK.",
        ".KOKODOODOOOOK..",
        "..KODOODOOLLOK..",
        "..KOOOOOOOLLOK..",
        "..KOKKKKKKKOKK..",
        "..KOK.....KOK...",
        "..KK......KK....",
    };

    private static readonly string[] WalkB =
    {
        "..........K...K.",
        ".........KPK.KPK",
        ".........KOOOOOK",
        ".K.......KOEOEOK",
        "KOK......KLOPOLK",
        "KOK.KKKKKKOOOOK.",
        ".KOKODOODOOOOK..",
        "..KODOODOOLLOK..",
        "..KOOOOOOOLLOK..",
        "...KOKKKKKOKKK..",
        "...KOK...KOK....",
        "....KK...KK.....",
    };

    private static readonly string[] SitFrame =
    {
        "..........K...K.",
        ".........KPK.KPK",
        ".........KOOOOOK",
        ".........KOEOEOK",
        ".........KLOPOLK",
        "........KKOOOOK.",
        ".......KODOOLLK.",
        "......KODOOLLLK.",
        ".KK..KOOOOOLLLK.",
        "KOOK.KOOOOOOOK..",
        ".KOOKKOOKKOOOK..",
        "..KKKKKKKKKKKK..",
    };

    // 눈 감은 앉기: 대답을 생각하는 동안 사용
    private static readonly string[] ThinkFrame =
    {
        "..........K...K.",
        ".........KPK.KPK",
        ".........KOOOOOK",
        ".........KOKOKOK",
        ".........KLOPOLK",
        "........KKOOOOK.",
        ".......KODOOLLK.",
        "......KODOOLLLK.",
        ".KK..KOOOOOLLLK.",
        "KOOK.KOOOOOOOK..",
        ".KOOKKOOKKOOOK..",
        "..KKKKKKKKKKKK..",
    };

    private static readonly string[] LieFrame =
    {
        "................",
        "................",
        "................",
        "..........K...K.",
        ".........KPK.KPK",
        ".........KOOOOOK",
        "..KKKKKKKKOKOKOK",
        ".KODOODOOOLOPOLK",
        "KOODOODOOOOOOOOK",
        "KOOOOOOOOOOOOOK.",
        ".KKKKKKKKKKKKK..",
        "................",
    };

    private static readonly string[] JumpFrame =
    {
        "..........K...K.",
        ".........KPK.KPK",
        ".........KOOOOOK",
        "K........KOEOEOK",
        "KOK......KLOPOLK",
        ".KOKKKKKKKOOOOK.",
        "..KODOODOOOOOK..",
        "..KODOODOOLLOK..",
        ".KOOOOOOOOLLOKK.",
        "KOKKKKKKKKKKKOK.",
        "KK..........KK..",
        "................",
    };

    // 색 팔레트는 고른 털 색에 따라 만듦 (MainWindow.Look.cs의 _palette)

    // 고양이가 할 수 있는 행동
    private enum CatState { Walk, Sit, Lie, Jump, Pant, Crouch, Land, Climb, Loaf, Peek, Belly }

    private readonly DispatcherTimer _timer = new() { Interval = TimeSpan.FromMilliseconds(16) };
    private readonly DispatcherTimer _dotsTimer = new() { Interval = TimeSpan.FromMilliseconds(350) };
    private readonly Random _rnd = new();
    private readonly AppSettings _settings;
    private readonly ClaudeClient _claude;
    private readonly SystemStats _stats = new();

    private CatState _state;
    private int _stateTicks;          // 현재 행동이 끝나기까지 남은 틱 수
    private int _dir = 1;             // 1 = 오른쪽, -1 = 왼쪽
    private double _centerX;          // 고양이 중심의 화면 X 좌표
    private double _jumpY;            // 바닥에서 떠 있는 높이 (0 = 바닥)
    private double _jumpVy;           // 점프 중 위 방향 속도
    private int _walkTick;            // 걷기 다리 애니메이션용 틱
    private bool _legFrameB;          // 걷기 프레임 (false = A, true = B)
    private bool _bubbleOpen;
    private bool _expanded;           // 말풍선 크게 보기 (화면 위쪽까지)
    private bool _menuOpen;           // 오른쪽 클릭 메뉴가 열려 있으면 고양이 멈춤
    private bool _unread;             // 말풍선을 닫은 사이 답이 도착함

    // 대화 상태
    private readonly List<(bool IsUser, string Text)> _history = new();   // 6단계 앱 전환용 대화 기록 (메모리에만)
    private CancellationTokenSource _askCts;   // 진행 중인 질문 (null이면 대기 상태)
    private TextBlock _answerBlock;            // 지금 답이 채워지고 있는 말풍선 줄
    private bool _gotFirstText;                // 답변 첫 글자가 왔는지 (오기 전까지 "···" 표시)
    private int _dots;
    private StatsSnapshot _lastStats;

    private string[] _drawnFrame;     // 마지막으로 그린 프레임 (같으면 다시 그리지 않음)
    private bool _drawnFlip;

    private bool Thinking => _askCts != null;

    public MainWindow()
    {
        InitializeComponent();

        _settings = AppSettings.Load();
        L.Init(_settings.Language);
        ApplyLanguage();
        _settings.ApplyAutoStart();     // exe 위치가 바뀌었어도 자동 실행 경로를 최신으로 맞춤
        _claude = new ClaudeClient(_settings) { Approve = ShowApprovalAsync };

        // 칸 사이에 얇은 틈이 생기지 않도록 안티앨리어싱(anti-aliasing)을 끔
        RenderOptions.SetEdgeMode(Cat, EdgeMode.Aliased);

        // 지난 대화가 있으면 복원, 없으면 첫 인사
        if (!RestoreHistory())
            AddCatLine(L.T("뭐 궁금한 거 있어? 귀찮지만… 물어봐냥.", "Need something? Fine… I'm listening. Meow."));

        Loaded += (_, _) =>
        {
            var wa = WorkArea;   // 고양이가 사는 모니터의 작업 영역 (작업표시줄 제외)
            var dpi0 = VisualTreeHelper.GetDpi(this);
            _centerX = _settings.CatScreenX is int sx0 ? sx0 / dpi0.DpiScaleX : wa.Right - 120;   // 저장된 위치 또는 오른쪽 아래
            ChooseNextState();
            UpdateSprite();
            UpdatePill();
            ApplyPosition();
            _timer.Tick += OnTick;
            _timer.Start();

            // 측정은 백그라운드 스레드에서 → 화면 갱신은 UI 스레드로 넘김
            _stats.Updated += s => Dispatcher.BeginInvoke(() => { _lastStats = s; UpdatePill(); });
            if (_settings.ShowStats) _stats.Start();
        };

        // 고양이 왼쪽 클릭: 말풍선 토글 (오른쪽 클릭은 메뉴)
        // 고양이 왼쪽 클릭(말풍선 열기/닫기)과 끌기는 MainWindow.Drag.cs의 한 처리기에서 함께 구분함
        CloseButton.Click += (_, _) => { if (_bubbleOpen) ToggleBubble(); };
        ExpandButton.Click += (_, _) => SetExpanded(!_expanded);
        StopButton.Click += (_, _) => { _claude.StopScreenControl(); _askCts?.Cancel(); };
        HandoffButton.Click += (_, _) => HandOffToDesktop();

        // Esc: 말풍선 닫기
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key == Key.Escape && _bubbleOpen) { StopListening(null); ToggleBubble(); e.Handled = true; }
        };

        // 6단계: 말풍선(머리줄·대화 기록) 더블클릭 → Claude 데스크톱 앱에서 이어하기. 입력창은 제외 (단어 선택과 겹침)
        MouseButtonEventHandler onDouble = (_, e) =>
        {
            if (e.ClickCount == 2) { e.Handled = true; HandOffToDesktop(); }
        };
        Header.MouseLeftButtonDown += onDouble;
        ChatScroll.MouseLeftButtonDown += onDouble;

        // 답을 기다리는 동안 "·  ··  ···" 애니메이션
        _dotsTimer.Tick += (_, _) =>
        {
            _dots = _dots % 3 + 1;
            if (!_gotFirstText && _answerBlock != null) _answerBlock.Text = new string('·', _dots);
        };

        InitScreenControl();
        InitOpenHotkey();
        InitDrag();
        InitDrop();
        InitLook();
        InitReminders();
        InitUpdateCheck();
        InitVoice();
        InitTaskbarFloor();
        SetupMenu();

        // 앱이 꺼지면 실행 중인 claude 프로세스와 측정 타이머도 정리
        // (Closing이 아니라 Closed: 닫기가 취소될 수 있으므로 정말 닫힌 뒤에만 정리)
        Closed += (_, _) =>
        {
            _askCts?.Cancel();
            _claude.Dispose();
            _stats.Dispose();
        };
    }

    // 고양이가 사는 모니터의 작업 영역(작업표시줄 제외)을 이 창의 지금 배율 기준 DIP로 돌려줌
    // 이유: 모니터별 DPI 인식(Per-Monitor V2)에서는 SystemParameters.WorkArea가 주 모니터·앱을 켤 때의 배율에 고정됨.
    //       창 위치(Left/Top)도 지금 창의 배율로 해석되므로 같은 배율로 바꿔야 위치가 맞음
    private Rect WorkArea
    {
        get
        {
            var mi = new MONITORINFO { cbSize = System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
            IntPtr mon = MonitorFromPoint(_monitorPt, MONITOR_DEFAULTTONEAREST);
            if (mon == IntPtr.Zero || !GetMonitorInfo(mon, ref mi)) return SystemParameters.WorkArea;
            var dpi = VisualTreeHelper.GetDpi(this);
            // 튀어나온 자동 숨김 작업표시줄·독이 있으면 그만큼 바닥을 올림 (MainWindow.Taskbar.cs)
            double bottom = mi.rcWork.Bottom - _floorInsetPhys;
            return new Rect(mi.rcWork.Left / dpi.DpiScaleX, mi.rcWork.Top / dpi.DpiScaleY,
                (mi.rcWork.Right - mi.rcWork.Left) / dpi.DpiScaleX, Math.Max(1, bottom - mi.rcWork.Top) / dpi.DpiScaleY);
        }
    }

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct RECT { public int Left, Top, Right, Bottom; }
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct MONITORINFO { public int cbSize; public RECT rcMonitor, rcWork; public uint dwFlags; }
    private const uint MONITOR_DEFAULTTONEAREST = 2;
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr MonitorFromPoint(POINT pt, uint flags);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool GetMonitorInfo(IntPtr mon, ref MONITORINFO mi);

    // XAML에 적힌 고정 문구를 지금 언어로 바꿔 넣음 (XAML 기본값은 한국어)
    private void ApplyLanguage()
    {
        StopButton.ToolTip = L.T("하던 일 멈추기", "Stop");
        HandoffButton.ToolTip = L.T("Claude 앱에서 이어서 대화하기 (말풍선 머리줄 더블클릭과 같음)", "Continue in the Claude app (same as double-clicking the header)");
        ExpandButton.ToolTip = L.T("크게 보기 (위로 길게)", "Expand (taller)");
        CloseButton.ToolTip = L.T("닫기 (Esc)", "Close (Esc)");
        Placeholder.Text = L.T("GAFIL에게 시킬 일 입력하고 Enter", "Ask GAFIL anything, then press Enter");
        HintText.Text = L.T("Enter 보내기 · Shift+Enter 줄바꿈 · Esc 닫기", "Enter send · Shift+Enter new line · Esc close");
        MenuNewChat.Header = L.T("새 대화 시작", "New conversation");
        MenuStats.Header = L.T("CPU/GPU 표시", "Show CPU/GPU");
        MenuAutoStart.Header = L.T("Windows 시작 시 자동 실행", "Start with Windows");
        MenuSettings.Header = L.T("설정 파일 열기 (허용 폴더 등)", "Open settings file (allowed folders, …)");
        MenuLog.Header = L.T("명령 로그 열기", "Open command log");
        MenuExit.Header = L.T("GAFIL 끄기", "Quit GAFIL");
    }

    // ---------------- 오른쪽 클릭 메뉴 ----------------

    private void SetupMenu()
    {
        CatMenu.Opened += (_, _) =>
        {
            _menuOpen = true;
            MenuStats.IsChecked = _settings.ShowStats;
            MenuAutoStart.IsChecked = _settings.AutoStart;
            // 자동 실행은 설치 위치(%LOCALAPPDATA%\Programs\GAFIL)에서 실행 중일 때만 바꿀 수 있음
            MenuAutoStart.IsEnabled = AppSettings.IsInstalledCopy;
            MenuAutoStart.Header = L.T("Windows 시작 시 자동 실행", "Start with Windows") +
                (AppSettings.IsInstalledCopy ? "" : L.T(" (설치본에서만)", " (installed copy only)"));
        };
        CatMenu.Closed += (_, _) => _menuOpen = false;

        MenuTalk.Click += (_, _) => { if (!_bubbleOpen) ToggleBubble(); };
        MenuNewChat.Click += (_, _) => StartNewChat();
        MenuStats.Click += (_, _) =>
        {
            _settings.ShowStats = MenuStats.IsChecked;
            _settings.Save();
            if (_settings.ShowStats) _stats.Start(); else { _stats.Stop(); _lastStats = null; }
            UpdatePill();
            ApplyPosition();
        };
        MenuAutoStart.Click += (_, _) =>
        {
            _settings.AutoStart = MenuAutoStart.IsChecked;
            _settings.Save();
            _settings.ApplyAutoStart();
        };
        MenuSettings.Click += (_, _) => OpenInNotepad(AppSettings.FilePath);
        MenuLog.Click += (_, _) =>
        {
            if (!File.Exists(AppSettings.LogPath)) AppSettings.Log("[로그] 로그 파일 생성");
            OpenInNotepad(AppSettings.LogPath);
        };
        MenuExit.Click += (_, _) => { _exitRequested = true; Close(); };

    }

    // ---------------- 5단계: GAFIL 화면 조작 ----------------

    private readonly List<ScreenControlOverlay> _overlays = new();   // 모니터마다 하나
    private string _hotkeyError;       // 긴급 정지 단축키 등록 실패 이유
    private bool _reopenBubbleAfterScreen;
    private const int HotkeyId = 0x6AF1;

    // 멈추는 방법 안내 (단축키 등록에 실패하면 화면 조작 자체를 시작하지 않으므로 항상 단축키)
    private const string StopHint = "Ctrl+Alt+Shift+F12";
    private IntPtr _prevForeground;    // 말풍선을 열기 전에 쓰던 창 (화면 조작을 시작할 때 포커스를 돌려줌)
    private bool _exitRequested;       // 메뉴에서 'GAFIL 끄기'를 눌렀는지

    private void InitScreenControl()
    {
        // 예전 버전(휴대폰 원격 제어)이 남긴 토큰·인증서 파일은 이제 쓰지 않으므로 지움
        foreach (string old in new[] { "remote.token", "remote.pfx" })
        {
            try
            {
                string path = System.IO.Path.Combine(AppSettings.Dir, old);
                if (File.Exists(path)) { File.Delete(path); AppSettings.Log($"[정리] 예전 원격 제어 파일 삭제: {old}"); }
            }
            catch { }
        }

        _claude.ScreenControlChanged += OnScreenControlChanged;
        _claude.EmergencyStopAvailable = () => _hotkeyError == null;

        // 화면 조작 중에 GAFIL 창이 닫히지 않게 (조작 중 Alt+F4가 GAFIL에 들어가는 경우 대비). 메뉴의 'GAFIL 끄기'는 허용
        Closing += (_, e) =>
        {
            if (_claude.ScreenActive && !_exitRequested)
            {
                e.Cancel = true;
                AppSettings.Log("[화면 조작] 조작 중 GAFIL 창 닫기 요청을 막음");
            }
        };
        _claude.ScreenAction += a => { foreach (var o in _overlays) o.SetAction(a); };
        _claude.SetMarkerHandlers(
            (x, y) => { foreach (var o in _overlays) { if (o.Monitor.Contains(x, y)) o.ShowMarker(x, y); else o.HideMarker(); } },
            () => { foreach (var o in _overlays) o.HideMarker(); });
        _claude.UserTookOverScreen += () => StatusText.Text = L.T("마우스를 움직여서 멈췄어", "Stopped: you moved the mouse");

        // 긴급 정지 단축키(Ctrl+Alt+Shift+F12): 창 핸들이 생긴 뒤 등록하고, 창 메시지에서 받음
        SourceInitialized += (_, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
            if (!RegisterHotKey(hwnd, HotkeyId, MOD_CONTROL | MOD_ALT | MOD_SHIFT | MOD_NOREPEAT, VK_F12))
            {
                _hotkeyError = "단축키 등록 실패";
                AppSettings.Log("[단축키] Ctrl+Alt+Shift+F12 등록 실패 (다른 프로그램이 쓰는 중일 수 있음)");
            }
            System.Windows.Interop.HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
            {
                if (msg == WM_HOTKEY && wp.ToInt32() == HotkeyId)
                {
                    handled = true;
                    _claude.StopScreenControl();
                    StatusText.Text = L.T("멈췄어", "Stopped");
                }
                return IntPtr.Zero;
            });
        };

        Closed += (_, _) =>
        {
            try { UnregisterHotKey(new System.Windows.Interop.WindowInteropHelper(this).Handle, HotkeyId); } catch { }
            foreach (var o in _overlays) o.Close();
            _overlays.Clear();
        };
    }

    // 화면 조작 시작/끝
    // 시작: 빨간 테두리, 말풍선 접기, 고양이 창은 클릭 통과 + 스크린샷에서 제외 (GAFIL이 자기 자신을 누르지 않게)
    // 끝: 모두 되돌리고, 접어 둔 말풍선을 다시 열어 결과를 보여줌
    private void OnScreenControlChanged(bool on)
    {
        var hwnd = new System.Windows.Interop.WindowInteropHelper(this).Handle;
        if (on)
        {
            // 모든 모니터에 빨간 테두리 (GAFIL이 어느 모니터를 조작하든 보이게)
            foreach (var o in _overlays) o.Close();
            _overlays.Clear();
            foreach (var m in ScreenInput.Monitors())
            {
                var o = new ScreenControlOverlay(m);
                o.SetStopHint(StopHint);
                o.Show();
                _overlays.Add(o);
            }

            _reopenBubbleAfterScreen = _bubbleOpen;
            if (_bubbleOpen) ToggleBubble();
            SetClickThrough(hwnd, true);
            SetCaptureExcluded(hwnd, true);

            // 키보드 포커스를 GAFIL에서 사용자가 쓰던 창으로 돌려줌
            // (안 그러면 GAFIL의 키 입력·글자 입력이 보이지 않는 고양이 창으로 들어감)
            if (_prevForeground != IntPtr.Zero && _prevForeground != hwnd && IsWindow(_prevForeground))
                SetForegroundWindow(_prevForeground);
        }
        else
        {
            foreach (var o in _overlays) o.Close();
            _overlays.Clear();
            SetClickThrough(hwnd, false);
            SetCaptureExcluded(hwnd, false);
            if (_reopenBubbleAfterScreen && !_bubbleOpen) ToggleBubble();
            _reopenBubbleAfterScreen = false;
        }
        UpdatePill();
    }

    // 클릭 통과 + 포커스 받지 않기를 함께 켜고 끔
    private static void SetClickThrough(IntPtr hwnd, bool on)
    {
        int ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        const int bits = WS_EX_TRANSPARENT | WS_EX_NOACTIVATE;
        SetWindowLong(hwnd, GWL_EXSTYLE, on ? ex | bits : ex & ~bits);
    }

    private static void SetCaptureExcluded(IntPtr hwnd, bool on)
    {
        try { SetWindowDisplayAffinity(hwnd, on ? WDA_EXCLUDEFROMCAPTURE : WDA_NONE); } catch { }
    }

    private const int WM_HOTKEY = 0x0312;
    private const int GWL_EXSTYLE = -20, WS_EX_TRANSPARENT = 0x20, WS_EX_NOACTIVATE = 0x08000000;
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hwnd);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hwnd);
    private const uint WDA_NONE = 0, WDA_EXCLUDEFROMCAPTURE = 0x11;
    private const uint MOD_ALT = 0x1, MOD_CONTROL = 0x2, MOD_SHIFT = 0x4, MOD_NOREPEAT = 0x4000, VK_F12 = 0x7B;
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hwnd, int id, uint mods, uint vk);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hwnd, int id);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hwnd, int index);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hwnd, int index, int value);
    [System.Runtime.InteropServices.DllImport("user32.dll")] private static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    private static void OpenInNotepad(string path)
    {
        try { Process.Start(new ProcessStartInfo("notepad.exe", $"\"{path}\"") { UseShellExecute = false }); }
        catch (Exception ex) { AppSettings.Log($"[메뉴] 메모장 열기 실패: {ex.Message}"); }
    }

    // 새 대화: 화면의 대화 기록과 Claude 세션을 모두 비움
    private void StartNewChat()
    {
        _askCts?.Cancel();
        _claude.Reset();
        _history.Clear();
        _attachedFiles.Clear();
        _attachedText = null;
        RefreshAttachments();
        ClearHistoryFile();
        ChatPanel.Children.Clear();
        StatusText.Text = "";
        AddCatLine(L.T("새로 시작하자. 뭐 해줄까?", "Fresh start. What do you need?"));
        if (!_bubbleOpen) ToggleBubble();
    }

    // ---------------- 고양이 움직임 ----------------

    // 매 틱: 행동 시간 확인 → 행동 갱신 → 그림 갱신 → 창 위치 갱신
    private void OnTick(object sender, EventArgs e)
    {
        if (_dragging)
        {
            // 들려 있는 동안: 흔들림이 천천히 잦아들고, 멈춰 있으면 살짝 대롱대롱
            _animTick++;
            _swing *= 0.85;
            UpdateSprite();
            return;
        }
        bool wasPerched = Perched;
        TrackPerch();                             // 올라가 있는 창이 움직이면 따라가고, 사라지면 떨어짐 (MainWindow.Perch.cs)
        // 말풍선이 열려 있는 동안 창이 움직이거나 사라져도(→ 바닥으로) 위치를 맞춤
        if (_bubbleOpen || _menuOpen) { if (wasPerched) ApplyPosition(); return; }     // 말풍선·메뉴가 열려 있으면 고양이는 멈춤
        _animTick++;
        if (UpdateRunToReminder()) { UpdateSprite(); ApplyPosition(); return; }   // 알림 때문에 달려가는 중 (공중이면 먼저 착지)

        var wa = WorkArea;
        // 화면 조작 중에는 창 위에 있으면 클릭을 가리지 않게 내려옴
        if (_claude.ScreenActive && Perched && _state != CatState.Jump) StepOffPerch();

        if (OnTrip && _state != CatState.Jump)
            UpdateTrip(wa);   // 창 위로 가는 중 (걷기·웅크리기·벽 타기)
        else
        {
            if (_state != CatState.Jump && --_stateTicks <= 0)
                ChooseNextState();

            switch (_state)
            {
                case CatState.Walk: UpdateWalk(wa); break;
                case CatState.Jump: UpdateAir(wa); break;
            }
        }

        UpdateSprite();
        ApplyPosition();
    }

    // 다음 행동을 확률로 고름: 걷기 45%, 앉기 18%, 눕기 15%, 배 보이기 4%, 점프 18%
    // 가끔(1~3분에 한 번쯤) 창 위로 올라가기. 창 위에 있으면 창 위의 행동을 고름 (MainWindow.Perch.cs)
    private void ChooseNextState()
    {
        if (TryChooseMoodState()) return;   // 밤·CPU 바쁨 등 기분이 먼저
        if (Perched) { ChoosePerchState(); return; }
        if (_rnd.Next(100) < 30 && TryStartExplore()) return;
        int roll = _rnd.Next(100);

        if (roll < 45)
        {
            _state = CatState.Walk;
            _stateTicks = _rnd.Next(120, 300);     // 약 2~5초
            _dir = _rnd.Next(2) == 0 ? -1 : 1;
        }
        else if (roll < 63)
        {
            _state = CatState.Sit;
            _stateTicks = _rnd.Next(120, 240);     // 약 2~4초
        }
        else if (roll < 78)
        {
            _state = CatState.Lie;
            _stateTicks = _rnd.Next(180, 360);     // 약 3~6초
        }
        else if (roll < 82)
        {
            _state = CatState.Belly;
            _stateTicks = _rnd.Next(240, 420);     // 약 4~7초 배를 보이며 뒹굴
        }
        else
        {
            StartJump();
        }
    }

    private void UpdateWalk(Rect wa)
    {
        // 약 0.16초(10틱)마다 다리 프레임을 바꿔서 걷는 모습을 만듦
        if (++_walkTick % 10 == 0) _legFrameB = !_legFrameB;

        _centerX += _dir * CurrentWalkSpeed;

        // 화면 가장자리(창 위라면 그 창 윗변의 끝)에 닿으면 방향을 바꿈 (시스템 정보 줄이 보이면 그 폭까지 화면 안에 들어오게)
        var (min, max) = StandRange(wa);
        if (_centerX <= min || _centerX >= max)
        {
            _dir = -_dir;
            _centerX = Math.Clamp(_centerX, min, max);
        }
    }

    // 평소 창의 절반 폭: 시스템 정보 줄이 보이면 그 줄 폭 기준
    private double HalfWidth() =>
        (StatsPill.Visibility == Visibility.Visible ? IdleWidth : CatWidth) / 2;

    // 제자리 점프: 조금 앞으로 나가며 뛰어오름. 공중 처리는 UpdateAir (창 위에서 뛰면 보통 그 창 위로 다시 내려앉음)
    private void StartJump()
    {
        _state = CatState.Jump;
        _jumpY = 0;
        _jumpVy = JumpPower;
        _vx = _dir * 1.8;
    }

    // 현재 행동에 맞는 프레임 고르기. 말풍선이 열려 있으면 앉아서 듣고, 생각 중이면 눈을 감음
    private void UpdateSprite()
    {
        string[] frame;
        if (_dragging)
            frame = HangFrame();   // 목덜미를 잡힌 모습 (정면이라 좌우 반전 없음)
        else if (_bubbleOpen)
            frame = Thinking && !_gotFirstText ? ThinkFrame : SitFrame;
        else
            frame = _state switch
            {
                CatState.Walk => _legFrameB ? WalkB : WalkA,
                CatState.Sit => SitFrame,
                CatState.Lie => LieFrame,
                CatState.Pant => (_animTick / 12) % 2 == 0 ? PantA : PantB,   // 약 0.2초마다 혀를 넣었다 뺐다
                CatState.Crouch => CrouchFrame,
                CatState.Land => LandFrame,
                CatState.Climb => _grabbing ? LeapFrame : (_animTick / 10) % 2 == 0 ? ClimbA : ClimbB,   // 매달리러 뛰어오른 뒤 앞발·뒷발 번갈아
                CatState.Loaf => (_animTick / 30) % 2 == 0 ? LedgeA : LedgeB,    // 늘어뜨린 꼬리 끝을 살랑살랑
                CatState.Peek => PeekFrame,
                CatState.Belly => BellyFrame,
                _ => _leaping ? LeapFrame : JumpFrame,
            };

        bool flip = _dir < 0 && !_dragging;     // 왼쪽을 보고 있으면 좌우 반전 (들려 있을 땐 정면)
        var overlay = MoodOverlay(frame);
        overlay.AddRange(OutfitOverlay(frame));   // 옷·소품은 땀·Zz 위에
        string overlayKey = string.Join(";", overlay);
        if (ReferenceEquals(frame, _drawnFrame) && flip == _drawnFlip && overlayKey == _drawnOverlay) return;

        _drawnFrame = frame;
        _drawnFlip = flip;
        _drawnOverlay = overlayKey;
        DrawFrame(frame, flip, overlay);
    }

    private string _drawnOverlay = "";

    // 프레임 문자열을 칸 단위 사각형으로 그림. overlay: 그 위에 덧그릴 점(땀·Zz 등)
    private void DrawFrame(string[] rows, bool flip, List<(int X, int Y, char C)> overlay = null)
    {
        Cat.Children.Clear();

        for (int y = 0; y < rows.Length; y++)
        {
            for (int x = 0; x < rows[y].Length; x++)
            {
                char c = rows[y][x];
                if (!_palette.TryGetValue(c, out var brush)) continue;     // 빈칸('.')은 그리지 않음

                int drawX = flip ? rows[y].Length - 1 - x : x;

                var cell = new Rectangle { Width = Cell, Height = Cell, Fill = brush };
                Canvas.SetLeft(cell, drawX * Cell);
                Canvas.SetTop(cell, y * Cell);
                Cat.Children.Add(cell);
            }
        }

        if (overlay == null) return;
        int width = rows[0].Length;
        foreach (var (x, y, c) in overlay)
        {
            if (!_palette.TryGetValue(c, out var brush) || x < 0 || x >= width || y < 0 || y >= rows.Length) continue;
            var dot = new Rectangle { Width = Cell, Height = Cell, Fill = brush };
            Canvas.SetLeft(dot, (flip ? width - 1 - x : x) * Cell);
            Canvas.SetTop(dot, y * Cell);
            Cat.Children.Add(dot);
        }
    }

    // 창 크기와 위치 갱신: 창 아래쪽이 항상 작업 영역 바닥(작업표시줄 위)에 붙도록 함
    private void ApplyPosition()
    {
        var wa = WorkArea;
        double w, h;
        if (_bubbleOpen)
        {
            w = OpenWidth;
            // 창 위 높은 곳이면 말풍선을 아래로 엶 (MainWindow.Perch.cs). 크게 보기면 여는 쪽 화면 끝까지
            SetBubbleBelow(WantBubbleBelow(wa));
            h = _expanded ? Math.Max(OpenHeight, BubbleSpace(wa)) : OpenHeight;
        }
        else
        {
            bool pill = StatsPill.Visibility == Visibility.Visible;
            w = pill ? IdleWidth : CatWidth;
            h = CatHeight + (pill ? PillHeight : 0);
            // 화면 가장자리에 있다가 시스템 정보 줄이 새로 나타나면 창이 넓어져 화면 밖으로 잘림 → 안쪽으로 당김
            _centerX = Math.Clamp(_centerX, wa.Left + w / 2, wa.Right - w / 2);
        }

        Width = w;
        Height = h;
        Left = _centerX - w / 2;
        if (!_bubbleOpen) SetBubbleBelow(false);
        // 창 위에 올라가 있으면 그 창의 윗변이 바닥. 말풍선을 아래로 열 땐 창의 맨 위가 고양이 머리
        Top = _bubbleBelow ? GroundY(wa) - CatHeight - _jumpY
                           : GroundY(wa) - h - _jumpY + SinkFor(_drawnFrame);      // 점프 중이면 그만큼 위로
    }

    // ---------------- 말풍선 ----------------

    // activate=false: 다른 프로그램을 쓰는 중에 알림으로 열 때 키보드 포커스를 뺏지 않음
    private void ToggleBubble(bool activate = true)
    {
        CancelDrag();   // 들고 옮기던 중이면 먼저 내려놓음 (말풍선이 공중에 떠 버리지 않게)
        _bubbleOpen = !_bubbleOpen;
        Bubble.Visibility = _bubbleOpen ? Visibility.Visible : Visibility.Collapsed;

        if (_bubbleOpen)
        {
            _unread = false;
            // 점프·벽 타기 중이었다면 바닥(또는 올라가 있던 창 위)에 내려놓고 앉힘
            CancelTrip();
            _jumpY = 0;
            _vx = 0;
            if (_state is CatState.Jump or CatState.Climb or CatState.Crouch or CatState.Land) _state = CatState.Sit;

            // 창이 커지므로 화면 밖으로 나가지 않도록 중심 위치를 보정
            var wa = WorkArea;
            _centerX = Math.Clamp(_centerX, wa.Left + OpenWidth / 2, wa.Right - OpenWidth / 2);

            // 지금 쓰던 창을 기억해 둠 (화면 조작을 시작하면 그 창으로 포커스를 돌려주려고)
            var fg = GetForegroundWindow();
            if (fg != new System.Windows.Interop.WindowInteropHelper(this).Handle) _prevForeground = fg;
            if (activate)
            {
                Activate();
                Input.Focus();
            }
            ChatScroll.ScrollToEnd();
        }
        else
        {
            SetExpanded(false, apply: false);   // 다음에 열 때는 보통 크기로
            StopListening(null);                // 말풍선을 닫으면 듣기도 끝 (받아 적던 글이 몰래 보내지지 않게)
        }

        UpdateSprite();
        UpdatePill();
        ApplyPosition();
    }

    // 크게 보기: 대화 기록 영역을 화면 위쪽 끝까지 늘림 (말풍선은 아래에 붙어 있으므로 위로 길어짐)
    private void SetExpanded(bool expanded, bool apply = true)
    {
        _expanded = expanded;
        var wa = WorkArea;
        // 최대 높이만 올리면 대화가 짧을 때 말풍선이 그대로라서, 크게 보기에서는 높이 자체를 정해 줌
        // 쓸 수 있는 높이: 바닥이면 화면 전체, 창 위면 말풍선을 여는 쪽(위·아래)의 화면 끝까지
        double tall = Math.Max(ChatMaxNormal, BubbleSpace(wa) - CatHeight - BubbleChrome);
        ChatScroll.MaxHeight = expanded ? tall : ChatMaxNormal;
        ChatScroll.Height = expanded ? tall : double.NaN;   // NaN = 내용에 맞춤
        ExpandButton.Content = expanded ? "" : "";   // 작게 보기 / 크게 보기 아이콘
        ExpandButton.ToolTip = expanded ? L.T("작게 보기", "Smaller") : L.T("크게 보기 (위로 길게)", "Expand (taller)");
        if (apply)
        {
            ApplyPosition();
            ChatScroll.ScrollToEnd();
        }
    }

    // 3단계: 대화하지 않을 때만 고양이 위에 CPU/GPU 표시. 닫힌 사이 답이 왔으면 그 알림을 대신 보여줌
    private void UpdatePill()
    {
        bool wasVisible = StatsPill.Visibility == Visibility.Visible;

        if (_bubbleOpen)
        {
            StatsPill.Visibility = Visibility.Collapsed;
        }
        else if (_dropHover)
        {
            StatsText.Text = L.T("📎 여기 놓아줘", "📎 Drop it here");
            StatsPill.ToolTip = null;
            StatsPill.Visibility = Visibility.Visible;
        }
        else if (_claude.ScreenActive)
        {
            // 화면 조작 중이면 다른 표시보다 먼저 알림
            StatsText.Text = L.T("🐾 화면 조작 중", "🐾 Controlling screen");
            StatsPill.ToolTip = L.T("GAFIL이 화면을 조작하는 중이에요 · 멈추기: ", "GAFIL is controlling the screen · stop: ") + StopHint;
            StatsPill.Visibility = Visibility.Visible;
        }
        else if (ShowUpdateNotice)
        {
            StatsText.Text = _updateNoticeLabel;
            StatsPill.ToolTip = L.T("오른쪽 클릭 → 새 버전 받기", "Right-click → Get the new version");
            StatsPill.Visibility = Visibility.Visible;
        }
        else if (_unread)
        {
            StatsText.Text = Thinking ? L.T("GAFIL 일하는 중…", "GAFIL is working…") : L.T("💬 GAFIL이 답했어", "💬 GAFIL replied");
            StatsPill.ToolTip = L.T("고양이를 클릭해서 확인", "Click the cat to read it");
            StatsPill.Visibility = Visibility.Visible;
        }
        else if (_settings.ShowStats && _lastStats != null)
        {
            var s = _lastStats;
            var g = s.Gpu;
            string temp = g.TempC is double t ? $" {t:0}°C" : "";
            StatsText.Text = $"CPU {Pct(s.Cpu)} · GPU {Pct(g.Util)}{temp}";
            string vram = g.VramUsedGB is double used
                ? (g.VramTotalGB is double total ? $"\nVRAM {used:0.0} / {total:0} GB" : $"\nVRAM {used:0.0} GB")
                : "\nVRAM N/A";
            StatsPill.ToolTip = $"{s.GpuName}{vram}\n" + L.T("온도 ", "Temp ") + (g.TempC is double tt ? $"{tt:0}°C" : "N/A");
            StatsPill.Visibility = Visibility.Visible;
        }
        else
        {
            StatsPill.Visibility = Visibility.Collapsed;
        }

        // 표시 여부가 바뀌면 창 크기도 바뀌어야 함
        if (wasVisible != (StatsPill.Visibility == Visibility.Visible)) ApplyPosition();
    }

    private static string Pct(double? v) => v is double d ? $"{d:0}%" : "N/A";

    // ---------------- 대화 기록 표시 ----------------

    // 사용자 말: 오른쪽 정렬된 옅은 칩
    private void AddUserLine(string text)
    {
        var chip = new Border
        {
            Background = (Brush)FindResource("Chip"),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(9, 5, 9, 6),
            Margin = new Thickness(40, 8, 0, 4),
            HorizontalAlignment = HorizontalAlignment.Right,
            Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 12.5, Foreground = (Brush)FindResource("Ink") },
        };
        ChatPanel.Children.Add(chip);
        TrimChat();
    }

    // GAFIL 말: 왼쪽 평문. 답이 스트리밍되는 동안 같은 줄을 계속 고침
    private TextBlock AddCatLine(string text)
    {
        var tb = new TextBlock
        {
            Text = text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 13,
            LineHeight = 20,
            Foreground = (Brush)FindResource("Ink"),
            Margin = new Thickness(0, 2, 0, 2),
        };
        ChatPanel.Children.Add(tb);
        TrimChat();
        return tb;
    }

    // 도구 사용 같은 진행 상황: 작은 회색 글씨
    private void AddActivityLine(string text)
    {
        var tb = new TextBlock
        {
            Text = "⚙ " + text,
            TextWrapping = TextWrapping.Wrap,
            FontSize = 11,
            Foreground = (Brush)FindResource("Muted"),
            Margin = new Thickness(0, 2, 0, 2),
        };
        // 진행 표시는 답변 줄 바로 위에 넣어 순서가 자연스럽게 보이도록 함
        int at = _answerBlock != null ? ChatPanel.Children.IndexOf(_answerBlock) : -1;
        if (at >= 0) ChatPanel.Children.Insert(at, tb); else ChatPanel.Children.Add(tb);
        ChatScroll.ScrollToEnd();
    }

    // 화면 요소가 너무 많아지지 않게 오래된 줄부터 지움 (Claude 쪽 대화 맥락은 그대로)
    private void TrimChat()
    {
        while (ChatPanel.Children.Count > 300) ChatPanel.Children.RemoveAt(0);
        ChatScroll.ScrollToEnd();
    }

    // ---------------- 질문 보내기 ----------------

    // 입력창이 비었을 때만 안내 문구 표시
    private void Input_TextChanged(object sender, TextChangedEventArgs e) =>
        Placeholder.Visibility = Input.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;

    // 입력창에서 Enter → 질문 전송. Shift+Enter는 줄바꿈
    private async void Input_KeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        if (Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
        {
            int at = Input.CaretIndex;
            Input.Text = Input.Text.Insert(at, "\n");
            Input.CaretIndex = at + 1;
            return;
        }

        await SubmitInputAsync(fromVoice: false);
    }

    // 질문 보내기 → "···" 표시 → 답이 오는 대로 말풍선 갱신 → 오류면 오류 표시
    // question: Claude에게 보낼 글, shown: 말풍선에 보여줄 글 (첨부 목록이 짧게 정리된 것)
    private async Task AskAsync(string question, string shown = null)
    {
        _askCts = new CancellationTokenSource();
        _gotFirstText = false;

        AddUserLine(shown ?? question);
        _history.Add((true, shown ?? question));
        var block = AddCatLine("·");   // 이 질문의 답 줄 (콜백은 필드 대신 이 지역 변수를 씀 → 질문이 끝난 뒤 불려도 안전)
        _answerBlock = block;
        StatusText.Text = L.T("생각 중…", "Thinking…");
        StopButton.Visibility = Visibility.Visible;
        Input.IsReadOnly = true;
        _dotsTimer.Start();
        UpdateSprite();

        string answer = null;
        try
        {
            answer = await _claude.AskAsync(question,
                text =>
                {
                    if (!_gotFirstText) { _gotFirstText = true; StatusText.Text = L.T("답하는 중…", "Answering…"); UpdateSprite(); }
                    block.Text = text;
                    ChatScroll.ScrollToEnd();    // 긴 답은 끝을 따라감
                },
                activity =>
                {
                    StatusText.Text = L.T("일하는 중…", "Working…");
                    AddActivityLine(activity);
                },
                _askCts.Token);
            StatusText.Text = _claude.ModelName != null ? ShortModel(_claude.ModelName) : "";
        }
        catch (OperationCanceledException)
        {
            if (!_gotFirstText) block.Text = L.T("(멈췄어.)", "(stopped.)");
            StatusText.Text = "";
        }
        catch (Exception ex)
        {
            block.Foreground = (Brush)FindResource("ErrorInk");
            block.Text = L.T("앗, 오류가 났어.\n", "Oops, something went wrong.\n") + ex.Message;
            StatusText.Text = "";
        }
        finally
        {
            _dotsTimer.Stop();
            _askCts.Dispose();
            _askCts = null;
            _gotFirstText = false;
            _answerBlock = null;
            StopButton.Visibility = Visibility.Collapsed;
            Input.IsReadOnly = false;
            UpdateSprite();
            // 말로 한 질문이면 입력창에 포커스를 다시 주지 않음 (Windows 음성 입력이 주변 소리를 계속 받아 적지 않게)
            if (_bubbleOpen && !_keepInputUnfocused) Input.Focus();
            if (!_bubbleOpen)
            {
                _unread = true; UpdatePill();   // 닫혀 있는 동안 답이 오면 고양이 위에 알림
                if (answer != null) CatMeow.TryMeow(_settings.Meow);   // 가끔 한 번 야옹 (10분에 한 번까지)
            }
        }

        if (answer != null) _history.Add((false, answer));
        SaveHistory();
    }

    // 상태 줄에 보여줄 짧은 모델 이름 (예: "claude-opus-4-8[1m]" → "opus 4.8")
    private static string ShortModel(string id)
    {
        string s = id.Replace("claude-", "");
        int br = s.IndexOf('[');
        if (br >= 0) s = s[..br];
        var parts = s.Split('-');
        return parts.Length >= 3 ? $"{parts[0]} {parts[1]}.{parts[2]}" : s;
    }

    // ---------------- 4단계 승인 창 ----------------

    // 승인 창을 띄우고, 사용자가 고르거나 CLI가 요청을 취소할 때까지 기다림
    private Task<bool> ShowApprovalAsync(ApprovalRequest req, Action<Action> registerClose)
    {
        var tcs = new TaskCompletionSource<bool>();
        var win = new ApprovalWindow(req);
        win.Closed += (_, _) => tcs.TrySetResult(win.Approved);
        registerClose(() => { tcs.TrySetResult(false); win.Close(); });

        StatusText.Text = L.T("허락 기다리는 중…", "Waiting for your approval…");
        win.Show();
        return tcs.Task;
    }

    // ---------------- 6단계: 데스크톱 앱으로 넘기기 ----------------

    // Claude 데스크톱 앱의 딥링크(claude://claude.ai/new?q=...)로 새 대화를 열고 지금까지의 대화를 입력창에 채움
    // 링크 길이 제한 때문에 링크에는 최근 대화만 넣고, 전체 대화는 클립보드에 복사함
    private void HandOffToDesktop()
    {
        string intro = L.T("바탕화면 고양이 GAFIL과 나누던 대화를 이어서 하고 싶어.\n\n", "I'd like to continue a conversation I was having with GAFIL, my desktop cat.\n\n");
        var full = new StringBuilder();
        foreach (var (isUser, text) in _history)
            full.Append(isUser ? L.T("나: ", "Me: ") : "GAFIL: ").Append(text).Append('\n');

        string convo = full.ToString();
        const int maxChars = 600;   // 한글은 URL 인코딩하면 9배로 길어짐 → 링크가 너무 길어지지 않게 제한
        string recent = convo.Length > maxChars ? "…" + convo[^maxChars..] : convo;
        string q = convo.Length == 0 ? "" : intro + recent;

        try
        {
            if (convo.Length > 0) Clipboard.SetText(intro + convo);
        }
        catch { }   // 다른 프로그램이 클립보드를 잡고 있으면 실패할 수 있음 (링크 열기는 계속)

        string url = q.Length == 0 ? "claude://claude.ai/new" : "claude://claude.ai/new?q=" + Uri.EscapeDataString(q);
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            StatusText.Text = convo.Length > maxChars ? L.T("앱으로 넘겼어 (전체 대화는 클립보드에)", "Opened in the app (full chat copied)") : L.T("Claude 앱으로 넘겼어", "Opened in the Claude app");
            AppSettings.Log($"[앱 전환] Claude 데스크톱 앱 열기 ({convo.Length}자 대화)");
        }
        catch (Exception ex)
        {
            StatusText.Text = L.T("Claude 앱을 열지 못했어", "Couldn't open the Claude app");
            AppSettings.Log($"[앱 전환] 실패: {ex.Message}");
        }
    }

    private static SolidColorBrush Brush(byte r, byte g, byte b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(r, g, b));
        brush.Freeze();
        return brush;
    }
}
