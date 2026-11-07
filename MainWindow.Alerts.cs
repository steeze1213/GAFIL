// v1.5 기능: 몸짓 알림 — 고양이가 몸짓으로 알려 줌
// - 알림 창(오류·경고 같은 작은 대화 상자)이 뜨면 → 그 창 위로 올라가 앉아서 머리 옆에 '!'
// - 프로그램이 '응답 없음'이 되면 → 그 창 위로 가서 앞발로 툭툭
// - 쉬지 않고 오래 일하면(기본 90분) → 화면 가운데 바닥에서 배를 보이며 드러누움
// - 토큰을 쓰지 않음: 전부 Windows가 알려 주는 정보(창 종류·응답 여부·마지막 입력 시각)로만 판단.
//   화면을 찍거나 Claude를 부르지 않음. 고양이를 클릭하면 PC에서 바로 정해진 말을 보여 줌.
//   알림 창일 때만 입력창에 "이 창이 무슨 뜻이야?" 질문을 미리 써 둠 → 사용자가 Enter를 눌러야 그때 Claude에게 보냄
// - 5분 동안 안 보면 그 창은 그만 알림. 오른쪽 클릭 메뉴 '몸짓 알림'으로 끌 수 있음
using System.Runtime.InteropServices;
using System.Text;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Media;
using System.Windows.Threading;

namespace ClaudeDesk;

public partial class MainWindow
{
    // 앞발 툭툭 (앉은 모습을 한 칸 왼쪽으로 옮기고 앞발을 듦 / 뻗음)
    private static readonly string[] TapA =
    {
        ".........K...K..",
        "........KPK.KPK.",
        "........KOOOOOK.",
        "........KOEOEOK.",
        "........KLOPOLK.",
        ".......KKOOOOKKK",
        "......KODOOLLKOK",
        ".....KODOOLLLKOK",
        "....KOOOOOLLLKK.",
        ".KK.KOOOOOOOK...",
        "KOOKKOOKKOOOK...",
        ".KKKKKKKKKKKK...",
    };
    private static readonly string[] TapB =
    {
        ".........K...K..",
        "........KPK.KPK.",
        "........KOOOOOK.",
        "........KOEOEOK.",
        "........KLOPOLK.",
        ".......KKOOOOK..",
        "......KODOOLLK..",
        ".....KODOOLLLK..",
        "....KOOOOOLLLK..",
        ".KK.KOOOOOOOKKK.",
        "KOOKKOOKKOOOOOOK",
        ".KKKKKKKKKKKKKK.",
    };

    private enum AlertKind { None, Dialog, Hung, Break }

    private readonly DispatcherTimer _alertTimer = new() { Interval = TimeSpan.FromSeconds(1) };
    private AlertKind _alert;
    private IntPtr _alertHwnd;
    private bool _alertArrived;          // 목표에 도착해서 몸짓 중
    private DateTime _alertSince;
    private int _alertTries;             // 올라가기를 몇 번 시도했는지 (계속 실패하면 창 아래에서 기다림)
    private bool _alertUnder;            // 못 올라가서 창 아래 바닥에서 기다리는 중
    private readonly Dictionary<IntPtr, AlertKind> _alertDone = new();     // 이미 알린 창 (다시 가지 않음)
    private readonly Dictionary<IntPtr, bool> _dialogVerdict = new();      // 대화 상자가 '알림 창'인지 (한 번만 판단)
    private DateTime _activeSince = DateTime.Now;   // 쉬지 않고 일하기 시작한 때
    private bool _breakShown;                       // 이번에 쉬라고 이미 알림 (한 번 쉬면 다시 셈)

    private const double AlertMaxUp = 5000;         // 알릴 때는 높이 상관없이 한 번에 크게 뜀 (대화 상자는 보통 화면 가운데에 떠 있음)
    private static readonly TimeSpan AlertGiveUp = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan IdleIsBreak = TimeSpan.FromMinutes(5);   // 5분 넘게 입력이 없으면 쉰 것으로 봄

    private bool AlertActive => _alert != AlertKind.None;

    private void InitAlerts()
    {
        _alertTimer.Tick += (_, _) => { try { CheckAlerts(); } catch (Exception ex) { AppSettings.Log($"[몸짓 알림] 확인 실패: {ex.Message}"); } };
        Loaded += (_, _) => _alertTimer.Start();
        Closed += (_, _) => _alertTimer.Stop();
    }

    // ---------------- 알아채기 (1초마다, 토큰 0) ----------------

    private void CheckAlerts()
    {
        UpdateBreakClock();
        PruneAlertDone();
        if (!_settings.BodyAlerts) { if (AlertActive) EndAlert(markDone: false); return; }

        if (AlertActive)
        {
            // 멈춘 창은 몇 초 뒤 Windows가 '(응답 없음)' 대역 창(Ghost)으로 덮음 → 그 대역 창으로 목표를 옮김 (원래 창은 가려져서 못 올라감)
            if (_alert == AlertKind.Hung && ClassOf(_alertHwnd) != "Ghost" && FindGhostFor(_alertHwnd) is IntPtr ghost)
            {
                _alertDone[_alertHwnd] = AlertKind.Hung;   // 원래 창으로 다시 알리지 않게
                _alertHwnd = ghost;
                _alertArrived = false;
                _alertUnder = false;
                _alertTries = 0;
            }
            if (_alert != AlertKind.Break && !AlertWindowStillThere(_alertHwnd, _alert)) { EndAlert(markDone: false); return; }
            if (_alert == AlertKind.Break && !_breakShown && DateTime.Now - _activeSince < TimeSpan.FromMinutes(1)) { EndAlert(markDone: false); return; }   // 그새 쉬었음
            if (DateTime.Now - _alertSince > AlertGiveUp) { AppSettings.Log("[몸짓 알림] 5분 동안 안 봐서 그만둠"); EndAlert(markDone: true); }
            return;
        }
        if (_claude.ScreenActive || _dragging || _bubbleOpen || _runningToRemind) return;

        var (kind, h) = FindAlertWindow();
        if (kind != AlertKind.None) { StartAlert(kind, h); return; }

        if (!_breakShown && _settings.BreakMinutes > 0 && DateTime.Now - _activeSince >= TimeSpan.FromMinutes(_settings.BreakMinutes))
            StartAlert(AlertKind.Break, IntPtr.Zero);
    }

    // 마지막 입력 시각으로 '쉬지 않고 일한 시간'을 셈: 5분 넘게 입력이 없으면 쉰 것 → 처음부터 다시
    private void UpdateBreakClock()
    {
        var lii = new LASTINPUTINFO { cbSize = (uint)Marshal.SizeOf<LASTINPUTINFO>() };
        if (!GetLastInputInfo(ref lii)) return;
        uint idleMs = unchecked((uint)Environment.TickCount - lii.dwTime);
        if (idleMs >= IdleIsBreak.TotalMilliseconds)
        {
            _activeSince = DateTime.Now;
            _breakShown = false;
        }
    }

    // 알릴 창 찾기: 알림 창이 먼저, 그다음 응답 없는 창. 맨 위 창부터
    private (AlertKind, IntPtr) FindAlertWindow()
    {
        uint myPid = (uint)Environment.ProcessId;
        IntPtr dialog = IntPtr.Zero, hung = IntPtr.Zero;
        EnumWindows((h, lp) =>
        {
            if (!IsWindowVisible(h) || IsIconic(h) || _alertDone.ContainsKey(h)) return true;
            if (DwmGetWindowAttribute(h, DWMWA_CLOAKED, out int cloaked, 4) == 0 && cloaked != 0) return true;
            GetWindowThreadProcessId(h, out uint pid);
            if (pid == myPid) return true;
            string cls = ClassOf(h);
            if (dialog == IntPtr.Zero && cls == "#32770" && IsAlertDialog(h)) dialog = h;
            else if (hung == IntPtr.Zero && (cls == "Ghost" || (IsHungAppWindow(h) && Candidate(h, myPid, out RECT _, out bool _)))) hung = h;
            return true;
        }, IntPtr.Zero);
        if (dialog != IntPtr.Zero) return (AlertKind.Dialog, dialog);
        if (hung != IntPtr.Zero) return (AlertKind.Hung, hung);
        return (AlertKind.None, IntPtr.Zero);
    }

    // 대화 상자 중 '알림 창'만: 메시지 상자처럼 글과 버튼만 있는 작은 창
    // 파일 열기·저장, 설정(탭), 진행률, 입력 칸이 있는 창은 제외 (입력 칸이 있으면 비밀번호일 수도 있으니 아예 안 봄)
    private bool IsAlertDialog(IntPtr h)
    {
        if (_dialogVerdict.TryGetValue(h, out bool v)) return v;
        bool button = false, bad = false;
        EnumChildWindows(h, (c, _) =>
        {
            string cls = ClassOf(c);
            if (cls is "Button" or "DirectUIHWND") button = true;
            if (cls.StartsWith("Edit") || cls.StartsWith("ComboBox") || cls.StartsWith("RichEdit") || cls is "SysListView32" or "SysTreeView32"
                or "SysTabControl32" or "msctls_progress32" or "SHELLDLL_DefView" or "ReBarWindow32" or "ToolbarWindow32") bad = true;
            return !bad;
        }, IntPtr.Zero);
        GetWindowRect(h, out var r);
        bool small = r.Right - r.Left < 1600 && r.Bottom - r.Top < 1200;
        v = button && !bad && small;
        _dialogVerdict[h] = v;
        return v;
    }

    private static bool AlertWindowStillThere(IntPtr h, AlertKind kind)
    {
        if (!IsWindow(h) || !IsWindowVisible(h) || IsIconic(h)) return false;
        return kind != AlertKind.Hung || ClassOf(h) == "Ghost" || IsHungAppWindow(h);
    }

    // 멈춘 창 h를 덮은 '(응답 없음)' 대역 창: 위치·크기가 거의 같은 Ghost 창
    private static IntPtr? FindGhostFor(IntPtr h)
    {
        if (!GetWindowRect(h, out var r)) return null;
        IntPtr found = IntPtr.Zero;
        EnumWindows((g, lp) =>
        {
            if (ClassOf(g) == "Ghost" && IsWindowVisible(g) && GetWindowRect(g, out var gr)
                && Math.Abs(gr.Left - r.Left) < 12 && Math.Abs(gr.Top - r.Top) < 12 && Math.Abs(gr.Right - r.Right) < 12)
            { found = g; return false; }
            return true;
        }, IntPtr.Zero);
        return found == IntPtr.Zero ? null : found;
    }

    // 사라진 창은 목록에서 지움. 응답이 돌아온 창도 지움 (다시 멈추면 또 알림)
    private void PruneAlertDone()
    {
        foreach (var h in _alertDone.Keys.ToList())
            if (!IsWindow(h) || (_alertDone[h] == AlertKind.Hung && ClassOf(h) != "Ghost" && !IsHungAppWindow(h))) _alertDone.Remove(h);
        foreach (var h in _dialogVerdict.Keys.ToList())
            if (!IsWindow(h)) _dialogVerdict.Remove(h);
    }

    private void StartAlert(AlertKind kind, IntPtr h)
    {
        _alert = kind;
        _alertHwnd = h;
        _alertArrived = false;
        _alertUnder = false;
        _alertTries = 0;
        _alertSince = DateTime.Now;
        if (OnTrip) AbortTrip();
        AppSettings.Log($"[몸짓 알림] {kind switch { AlertKind.Dialog => "알림 창", AlertKind.Hung => "응답 없음", _ => "오래 일함" }}"
            + (h != IntPtr.Zero ? $": {Shorten(TitleOf(h), 60)}" : ""));
    }

    private void EndAlert(bool markDone)
    {
        if (markDone && _alert is AlertKind.Dialog or AlertKind.Hung) _alertDone[_alertHwnd] = _alert;
        if (markDone && _alert == AlertKind.Break) _breakShown = true;
        bool wasPose = _alertArrived;
        _alert = AlertKind.None;
        _alertHwnd = IntPtr.Zero;
        _alertArrived = false;
        _alertUnder = false;
        if (wasPose && _state is CatState.Tap or CatState.Belly or CatState.Sit) _stateTicks = 1;   // 곧 평소 행동으로
    }

    // ---------------- 찾아가기 (OnTick에서 매 틱) ----------------

    // 알림 중이면 고양이를 움직이고 true (평소 행동 대신). 공중이거나 가는 중이면 false → 평소 처리(착지·여행)
    private bool UpdateAlert(Rect wa)
    {
        if (!AlertActive || _state == CatState.Jump) return false;
        if (_claude.ScreenActive) return false;   // 화면 조작 중에는 잠시 멈춤 (클릭을 가리지 않게). 끝나면 이어서 감
        if (_alert == AlertKind.Break) return UpdateBreakAlert(wa);

        if (!OnSameMonitor(_alertHwnd))
        {
            if (Perched) { StepOffPerch(); return false; }
            if (OnTrip) AbortTrip();
            MoveToMonitorOf(_alertHwnd);
            return true;
        }
        if (Perched && _perch == _alertHwnd) { HoldAlertPose(wa); return true; }   // 도착
        if (OnTrip) return false;                                                   // 가는 중
        if (Perched) { StepOffPerch(); return false; }                              // 다른 창 위 → 먼저 내려옴

        if (!_alertUnder && _alertTries < 3)
        {
            _alertTries++;
            RefreshPlatforms(wa, force: true);
            if (FindPlatform(_alertHwnd) is Platform goal)
            {
                _leapUp = AlertMaxUp;
                var path = FindPath(wa, goal);
                if (path != null) { StartTrip(path); return false; }
            }
        }
        // 올라갈 수 없음(너무 높이 떠 있거나 윗변이 가려짐): 창 바로 아래 바닥에서 기다림
        _alertUnder = true;
        GetWindowRect(_alertHwnd, out var r);
        double cx = (r.Left + r.Right) / 2.0 / VisualTreeHelper.GetDpi(this).DpiScaleX;
        if (WalkToward(wa, cx)) HoldAlertPose(wa);
        return true;
    }

    // 오래 일함: 화면 가운데 바닥으로 가서 배를 보이며 드러누움
    private bool UpdateBreakAlert(Rect wa)
    {
        if (Perched) { StepOffPerch(); return false; }
        if (OnTrip) AbortTrip();
        if (WalkToward(wa, wa.Left + wa.Width / 2)) HoldAlertPose(wa);
        return true;
    }

    // x까지 걸어감. 도착했으면 true
    private bool WalkToward(Rect wa, double x)
    {
        var (a, b) = StandRange(wa);
        x = Math.Clamp(x, a, b);
        double d = x - _centerX;
        if (Math.Abs(d) <= 1.5) return true;
        _state = CatState.Walk;
        _stateTicks = 30;
        _dir = Math.Sign(d);
        if (++_walkTick % 7 == 0) _legFrameB = !_legFrameB;
        _centerX += Math.Sign(d) * Math.Min(Math.Abs(d), WalkSpeed * 1.6);
        return false;
    }

    // 도착해서 몸짓: 알림 창은 앉아서 '!', 응답 없음은 앞발 툭툭, 오래 일함은 배 보이기
    private void HoldAlertPose(Rect wa)
    {
        CancelTrip();
        if (!_alertArrived)
        {
            _alertArrived = true;
            CatMeow.TryMeow(_settings.Meow);   // 한 번 야옹 (10분에 한 번까지)
            if (_alertHwnd != IntPtr.Zero && GetWindowRect(_alertHwnd, out var r))
            {
                // 창 가운데 쪽을 보게
                double cx = (r.Left + r.Right) / 2.0 / VisualTreeHelper.GetDpi(this).DpiScaleX;
                _dir = cx >= _centerX ? 1 : -1;
            }
        }
        _state = _alert switch
        {
            AlertKind.Hung when !_alertUnder => CatState.Tap,
            AlertKind.Break => CatState.Belly,
            _ => CatState.Sit,
        };
        _stateTicks = 60;
    }

    private bool OnSameMonitor(IntPtr h) =>
        MonitorFromWindow(h, MONITOR_DEFAULTTONEAREST) == MonitorFromPoint(_monitorPt, MONITOR_DEFAULTTONEAREST);

    // 알릴 창이 다른 모니터에 있으면 그 모니터의 바닥(창 아래쪽)으로 건너감
    private void MoveToMonitorOf(IntPtr h)
    {
        if (!GetWindowRect(h, out var r)) return;
        var p = new POINT { X = (r.Left + r.Right) / 2, Y = (r.Top + r.Bottom) / 2 };
        _perch = IntPtr.Zero;
        _jumpY = 0;
        _vx = 0;
        _monitorPt = p;
        // 지금 창의 배율 기준 DIP로 둠 → 새 모니터로 넘어가며 배율이 바뀌면 DpiChanged가 다시 맞춤 (들어 옮길 때와 같은 방식)
        _centerX = p.X / VisualTreeHelper.GetDpi(this).DpiScaleX;
        _state = CatState.Sit;
        AppSettings.Log("[몸짓 알림] 다른 모니터로 건너감");
    }

    // ---------------- 클릭하면 (토큰 0, 알림 창만 질문을 미리 써 둠) ----------------

    // 말풍선이 열릴 때 부름: 알림 중이면 그 내용을 고양이 말로 보여 주고 알림을 끝냄
    private void ShowAlertInBubble()
    {
        if (!AlertActive) return;
        var kind = _alert;
        var h = _alertHwnd;
        EndAlert(markDone: true);
        switch (kind)
        {
            case AlertKind.Dialog:
            {
                string title = Shorten(TitleOf(h), 80);
                string body = Shorten(DialogText(h), 500);
                AddCatLine(L.T($"이런 창이 떠 있었어냥.\n「{title}」{(body.Length > 0 ? "\n" + body : "")}\n\n무슨 뜻인지 궁금하면 Enter를 눌러. 그때만 Claude에게 보내.",
                               $"This window popped up.\n“{title}”{(body.Length > 0 ? "\n" + body : "")}\n\nPress Enter if you want me to explain it. Only then is it sent to Claude."));
                string q = L.T("이 창이 무슨 뜻이야? 어떻게 하면 돼?", "What does this window mean? What should I do?");
                Input.Text = $"{q}\n[{title}] {body}".TrimEnd();
                Input.CaretIndex = Input.Text.Length;
                break;
            }
            case AlertKind.Hung:
            {
                string title = Shorten(TitleOf(h), 80);
                AddCatLine(L.T($"「{title}」이(가) 응답이 없어냥. 조금 기다려 봐.\n계속 멈춰 있으면 작업 관리자(Ctrl+Shift+Esc)에서 끌 수 있어.",
                               $"“{title}” isn't responding. Give it a moment.\nIf it stays frozen, you can end it in Task Manager (Ctrl+Shift+Esc)."));
                break;
            }
            case AlertKind.Break:
            {
                int mins = (int)(DateTime.Now - _activeSince).TotalMinutes;
                AddCatLine(L.T($"{mins}분째 쉬지 않고 일했어냥. 잠깐 일어나서 쉬자.", $"You've been at it for {mins} minutes straight. Take a short break."));
                break;
            }
        }
        ChatScroll.ScrollToEnd();
    }

    // 알림 창의 글: 메시지 상자는 자식 글자 칸(Static)에서, 새 형식(TaskDialog)은 접근성 정보(UI Automation)에서 읽음
    private static string DialogText(IntPtr h)
    {
        var sb = new StringBuilder();
        EnumChildWindows(h, (c, _) =>
        {
            if (ClassOf(c) == "Static" && IsWindowVisible(c))
            {
                string t = TitleOf(c).Trim();
                if (t.Length > 0) sb.AppendLine(t);
            }
            return sb.Length < 2000;
        }, IntPtr.Zero);
        if (sb.Length > 0) return sb.ToString().Trim();
        try
        {
            // 멈춘 창이면 기다리지 않도록 1.5초만
            var task = Task.Run(() =>
            {
                var el = AutomationElement.FromHandle(h);
                var texts = el.FindAll(TreeScope.Descendants, new PropertyCondition(AutomationElement.ControlTypeProperty, ControlType.Text));
                var parts = new List<string>();
                foreach (AutomationElement t in texts)
                {
                    string n = t.Current.Name?.Trim();
                    if (!string.IsNullOrEmpty(n) && !parts.Contains(n)) parts.Add(n);
                }
                return string.Join("\n", parts);
            });
            return task.Wait(1500) ? task.Result : "";
        }
        catch { return ""; }
    }

    // 창 제목: 메시지를 보내지 않는 방식(InternalGetWindowText)으로 읽음 → 멈춘 프로그램 때문에 고양이까지 멈추지 않음
    private static string TitleOf(IntPtr h)
    {
        var sb = new StringBuilder(512);
        InternalGetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string ClassOf(IntPtr h)
    {
        var sb = new StringBuilder(64);
        GetClassName(h, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string Shorten(string s, int max) => s.Length <= max ? s : s[..max] + "…";

    // 알림 중 머리 옆에 깜빡이는 '!' (몸짓 중일 때만)
    private void AddAlertMark(List<(int X, int Y, char C)> dots)
    {
        if (!AlertActive || !_alertArrived || _bubbleOpen || (_animTick / 30) % 2 != 0) return;
        dots.Add((7, 0, 'X'));
        dots.Add((7, 1, 'X'));
        dots.Add((7, 3, 'X'));
    }

    // ---------------- Win32 ----------------
    [StructLayout(LayoutKind.Sequential)] private struct LASTINPUTINFO { public uint cbSize; public uint dwTime; }
    [DllImport("user32.dll")] private static extern bool GetLastInputInfo(ref LASTINPUTINFO lii);
    [DllImport("user32.dll")] private static extern bool IsHungAppWindow(IntPtr h);
    [DllImport("user32.dll")] private static extern bool EnumChildWindows(IntPtr parent, EnumWindowsProc cb, IntPtr lp);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int InternalGetWindowText(IntPtr h, StringBuilder s, int n);
    [DllImport("user32.dll")] private static extern IntPtr MonitorFromWindow(IntPtr h, uint flags);
}
