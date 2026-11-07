// v1.1 기능: 알림이 울리면 고양이가 화면 가운데로 뛰어와 말풍선으로 알려줌 (+ 알림 소리)
// 15초마다 때가 된 알림을 확인. 화면 조작 중이면 끝날 때까지 기다렸다가 알림
using System.Media;
using System.Windows.Threading;

namespace ClaudeDesk;

public partial class MainWindow
{
    private readonly ReminderStore _reminders = new();
    private readonly DispatcherTimer _reminderTimer = new() { Interval = TimeSpan.FromSeconds(15) };
    private readonly List<Reminder> _pendingAnnounce = new();   // 때가 됐지만 아직 보여주지 못한 알림 (파일에는 남아 있음)
    private bool _runningToRemind;        // 알림 때문에 화면 가운데로 달려가는 중

    private const double RunSpeed = 7;    // 달리기 속도 (걷기의 약 6배)

    private void InitReminders()
    {
        _claude.Reminders = _reminders;
        _reminderTimer.Tick += (_, _) => CheckReminders();
        Loaded += (_, _) =>
        {
            _reminderTimer.Start();
            CheckReminders();   // 꺼진 사이 지난 알림도 켜자마자 알려줌
        };
        // 화면 조작이 시작되면 달리기를 멈춤 (조작이 끝난 뒤 다음 확인 때 다시 알림)
        _claude.ScreenControlChanged += on => { if (on) _runningToRemind = false; };
    }

    private void CheckReminders()
    {
        foreach (var r in _reminders.PeekDue())
            if (!_pendingAnnounce.Any(p => p.Id == r.Id)) _pendingAnnounce.Add(r);
        if (_pendingAnnounce.Count == 0 || _claude.ScreenActive || _dragging) return;

        if (_bubbleOpen) { _runningToRemind = false; AnnounceAll(); return; }   // 이미 말풍선이 열려 있으면 바로 알림

        // 고양이가 사는 모니터 가운데로 달려감 → 도착하면 OnTick에서 알림
        _runningToRemind = true;
    }

    // OnTick에서 부름: 달려가는 중이면 이동하고 true (평소 움직임 대신)
    // 공중에 있으면(점프 중·막 내려놓음) false → 평소 점프 처리로 먼저 착지
    private bool UpdateRunToReminder()
    {
        if (!_runningToRemind) return false;
        if (OnTrip) AbortTrip();   // 창 위로 가던 중이면 그만둠 (벽 타던 중이면 떨어짐)
        if (Perched) { if (_state != CatState.Jump) StepOffPerch(); return false; }   // 창 위에 있으면 먼저 뛰어내림
        if (_state == CatState.Jump || _jumpY > 0) return false;

        // 목표는 매번 다시 계산: 그 사이 다른 모니터로 옮겼거나 배율이 바뀌어도 맞게
        var wa = WorkArea;
        double target = wa.Left + wa.Width / 2;
        _state = CatState.Walk;
        _stateTicks = 60;
        _dir = target >= _centerX ? 1 : -1;
        if (++_walkTick % 5 == 0) _legFrameB = !_legFrameB;   // 달릴 땐 다리를 빨리
        double step = Math.Min(RunSpeed, Math.Abs(target - _centerX));
        _centerX += Math.Sign(target - _centerX) * step;
        if (Math.Abs(target - _centerX) < 1)
        {
            _runningToRemind = false;
            _state = CatState.Sit;
            _stateTicks = 120;
            AnnounceAll();
        }
        return true;
    }

    private void AnnounceAll()
    {
        // 화면 조작 중이거나 들고 옮기는 중이면 나중에 (알림은 목록에 그대로 남아 있음)
        if (_pendingAnnounce.Count == 0 || _claude.ScreenActive || _dragging) return;
        if (!_bubbleOpen) ToggleBubble(activate: false);   // 사용자가 다른 창에 타이핑 중일 수 있어 포커스는 뺏지 않음
        foreach (var r in _pendingAnnounce.OrderBy(r => r.Due))
        {
            bool late = DateTime.Now - r.Due > TimeSpan.FromMinutes(2);
            string text = "⏰ " + r.Message + (late ? L.T($"  (놓친 알림 · {r.Due:M/d HH:mm})", $"  (missed · {r.Due:M/d HH:mm})") : "");
            AddCatLine(text);
            _history.Add((false, text));
            AppSettings.Log($"[알림] 울림: {r.Message}");
        }
        // 화면에 보여준 다음에야 파일에서 지움 (그 전에 꺼지면 다음에 켤 때 '놓친 알림'으로 다시 알려줌)
        _reminders.Remove(_pendingAnnounce.Select(r => r.Id));
        _pendingAnnounce.Clear();
        // 알림은 야옹 두 번 (야옹을 꺼 두었으면 원래의 Windows 알림음)
        if (!CatMeow.TryMeow(_settings.Meow, force: true, times: 2))
            try { SystemSounds.Asterisk.Play(); } catch { }
        SaveHistory();
    }
}
