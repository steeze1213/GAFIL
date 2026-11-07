// 친밀도와 소개 (토큰 0, 기능은 하나도 잠그지 않음)
// - 친밀도: 함께 켜 둔 시간(10분에 1), 말풍선 열기(하루 6번까지 1씩), 츄르(하루 한 번 2). Claude에게 묻는 건 세지 않음
// - 할 줄 아는 것 소개: 친해질수록 말풍선을 열 때 "나 이런 것도 할 수 있어" 하고 하나씩 알려 줌 (3시간에 하나까지)
// - 이스터에그 귀띔: 친밀도가 높으면 1분마다 0.01% 확률로 이스터에그 하는 법을 하나 알려 줌. 보라색 글씨
// - 메뉴의 "할 줄 아는 것 알려 주기"를 끄면 소개와 귀띔 모두 안 함
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;

namespace ClaudeDesk;

public partial class MainWindow
{
    private const int EggHintBond = 60;                 // 이만큼 친해져야 귀띔 (함께 켜 둔 시간만으로 약 10시간)
    private const double EggHintChance = 0.0001;        // 1분마다 0.01%
    private static readonly TimeSpan IntroGap = TimeSpan.FromHours(3);

    private int _bondMinutes;
    private DateTime _bondOpenDay;
    private int _bondOpens;
    private DateTime _bondChuruDay;
    private string _pendingEggHint;                     // 뽑혔지만 아직 말풍선을 열지 않아 못 보여 준 귀띔

    private sealed record Skill(string Id, int Bond, Func<MainWindow, bool> On, Func<MainWindow, string> Ko, Func<MainWindow, string> En);

    // 소개 순서. On이 false인 기능(설정에서 끈 것)은 건너뜀
    private static readonly Skill[] Skills =
    {
        new("quick", 3, w => w._settings.QuickAnswers,
            _ => "\"3.5인치 cm\"나 \"뉴욕 지금 몇 시\"처럼 물어보면 Claude한테 안 보내고 내가 바로 계산해 줘. 토큰 0이야.",
            _ => "Ask things like \"3.5 inch to cm\" or \"time in New York\" and I'll answer right here without asking Claude. Zero tokens."),
        new("drag", 6, _ => true,
            _ => "나를 꾹 눌러서 끌면 목덜미 잡힌 아기 고양이처럼 따라가. 다른 모니터에 놔도 돼.",
            _ => "Press and drag me and I'll dangle along like a kitten held by the scruff. You can drop me on another monitor."),
        new("hotkey", 10, w => !string.IsNullOrWhiteSpace(w._settings.OpenHotkey),
            w => $"어디서든 {w._settings.OpenHotkey}를 누르면 말풍선이 열려. 나를 찾으러 올 필요 없어.",
            w => $"Press {w._settings.OpenHotkey} anywhere and my bubble opens. No need to come find me."),
        new("remind", 14, _ => true,
            _ => "\"30분 뒤에 회의 알려줘\" 하면 시간 됐을 때 화면 가운데로 뛰어가서 알려 줄게.",
            _ => "Say \"remind me about the meeting in 30 minutes\" and I'll run to the middle of the screen when it's time."),
        new("drop", 18, _ => true,
            _ => "파일이나 선택한 글을 나한테 끌어다 놓고 \"요약해줘\" 해 봐.",
            _ => "Drag a file or some selected text onto me and say \"summarize this\"."),
        new("climb", 22, w => w._settings.ClimbWindows,
            _ => "가끔 창 위에 올라가 있지? 작은 창이 새로 뜨면 상자인 줄 알고 들어가 보기도 해. 싫으면 메뉴에서 끌 수 있어.",
            _ => "Seen me on top of windows? When a small new window pops up I might hop in like it's a box. You can turn it off in the menu."),
        new("alerts", 26, w => w._settings.BodyAlerts,
            _ => "오류 창이 뜨거나 프로그램이 멈추면 그 창 위로 가서 알려 줘. 그때 나를 누르면 무슨 일인지 말해 줄게.",
            _ => "If an error dialog pops up or an app freezes, I'll go sit on it. Click me then and I'll tell you what happened."),
        new("voice", 30, w => !string.IsNullOrWhiteSpace(w._settings.VoiceHotkey),
            w => $"{w._settings.VoiceHotkey}를 누르고 말하면 받아 적어서 보내 줘.",
            w => $"Press {w._settings.VoiceHotkey} and talk. I'll write it down and send it."),
        new("look", 34, _ => true,
            _ => "나를 오른쪽 클릭 → 모습 바꾸기에서 털 색이랑 옷을 바꿀 수 있어.",
            _ => "Right-click me → Change look to pick my fur color and outfit."),
        new("screen", 40, _ => true,
            _ => "\"웨일에서 날씨 검색해줘\"처럼 화면을 보고 해야 하는 일도 할 수 있어. 할 때마다 먼저 물어볼게.",
            _ => "I can also do things that need the screen, like \"search the weather in my browser\". I'll ask before each one."),
        new("handoff", 46, _ => true,
            _ => "말풍선 맨 위 줄을 더블클릭하면 지금 대화를 Claude 앱으로 넘겨 줘. 길게 얘기할 땐 그게 편해.",
            _ => "Double-click the top bar of my bubble to hand this chat over to the Claude app. Handy for long talks."),
    };

    // 이스터에그 하는 법 (MainWindow.Eggs.cs)
    private static readonly (string Id, string Ko, string En)[] EggHints =
    {
        ("laser", "내 앞에서 마우스를 좌우로 빠르게 흔들어 봐. 못 참아…", "Wiggle your mouse left and right fast in front of me. I can't resist…"),
        ("churu", "입력창에 \"츄르\"라고만 써서 보내 봐. Claude한테는 안 가.", "Type just \"churu\" and send it. It won't go to Claude."),
        ("zoom", "밤 11시 40분에서 0시 40분 사이엔 가끔 나도 모르게 우다다 해.", "Between 11:40 pm and 12:40 am I sometimes get the zoomies."),
        ("hiss", "나를 엄청 빨리 다섯 번 누르면… 화낼 거야. 진짜로.", "Click me five times really fast and… I'll get mad. For real."),
        ("catday", "8월 8일이랑 9월 9일은 고양이의 날이야. 그날 나를 봐.", "August 8 and September 9 are Cat Days. Come see me then."),
    };

    // ---------------- 친밀도 ----------------

    // 1분마다 (_idleTimer): 10분 함께하면 1. 친밀도가 높으면 아주 낮은 확률로 귀띔을 하나 뽑아 둠
    private void BondMinute()
    {
        if (++_bondMinutes % 10 == 0) AddBond(1);
        if (!_settings.SkillIntros || _pendingEggHint != null || _settings.Bond < EggHintBond) return;
        bool force = false;
#if DEBUG
        force = Environment.GetEnvironmentVariable("GAFIL_TEST_EGGHINT") == "1";
#endif
        if (!force && _rnd.NextDouble() >= EggHintChance) return;
        var left = EggHints.Where(e => !_settings.HintedEggs.Contains(e.Id)).ToArray();
        if (left.Length == 0) { _settings.HintedEggs.Clear(); left = EggHints; }   // 다 알려 줬으면 처음부터 다시
        _pendingEggHint = left[_rnd.Next(left.Length)].Id;
        AppSettings.Log("[친밀도] 이스터에그 귀띔이 뽑힘");
        if (_bubbleOpen) ShowPendingEggHint();
    }

    private void AddBond(int n)
    {
        _settings.Bond += n;
        try { _settings.Save(); } catch { }
    }

    // 말풍선을 열 때: 하루 6번까지 친밀도 1
    private void BondOnOpen()
    {
        if (_bondOpenDay != DateTime.Today) { _bondOpenDay = DateTime.Today; _bondOpens = 0; }
        if (++_bondOpens <= 6) AddBond(1);
    }

    // 츄르를 주면: 하루 한 번 친밀도 2
    private void BondOnChuru()
    {
        if (_bondChuruDay == DateTime.Today) return;
        _bondChuruDay = DateTime.Today;
        AddBond(2);
    }

    // ---------------- 소개 ----------------

    // 말풍선을 열 때: 귀띔이 기다리고 있으면 그것, 아니면 친해진 만큼 다음 소개 하나 (3시간에 하나까지)
    private void IntroduceOnOpen()
    {
        BondOnOpen();
        if (!_settings.SkillIntros || Thinking || AlertActive) return;
        if (ShowPendingEggHint()) return;
        if (_settings.LastIntroAt is DateTime t && DateTime.Now - t < IntroGap) return;
        var next = Skills.FirstOrDefault(s => !_settings.IntroducedSkills.Contains(s.Id) && s.On(this));
        if (next == null || _settings.Bond < next.Bond) return;
        _settings.IntroducedSkills.Add(next.Id);
        _settings.LastIntroAt = DateTime.Now;
        try { _settings.Save(); } catch { }
        AddCatLine(L.T("있잖아, 나 이런 것도 할 수 있어냥.\n", "Hey, did you know I can do this?\n") + L.T(next.Ko(this), next.En(this)));
        AddNoteLine(L.T("(친해질수록 하나씩 알려 줄게. 메뉴의 \"할 줄 아는 것 알려 주기\"로 끌 수 있어)",
                        "(I'll share one now and then as we get closer. Turn off \"Tell me what you can do\" in the menu)"));
        AppSettings.Log($"[친밀도] 소개: {next.Id} (친밀도 {_settings.Bond})");
    }

    private bool ShowPendingEggHint()
    {
        if (_pendingEggHint == null) return false;
        var e = EggHints.FirstOrDefault(h => h.Id == _pendingEggHint);
        _pendingEggHint = null;
        if (e.Id == null) return false;
        _settings.HintedEggs.Add(e.Id);
        try { _settings.Save(); } catch { }
        AddEggHintLine(L.T("✦ 비밀인데 너한테만 알려 줄게.\n", "✦ A secret, just for you.\n") + L.T(e.Ko, e.En));
        AppSettings.Log($"[친밀도] 이스터에그 귀띔: {e.Id}");
        return true;
    }

    // 작은 회색 안내 (대화 기록에는 안 남음)
    private void AddNoteLine(string text)
    {
        ChatPanel.Children.Add(new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 11,
            Foreground = (Brush)FindResource("Muted"), Margin = new Thickness(0, 0, 0, 4),
        });
        TrimChat();
    }

    // 이스터에그 귀띔: 보라색 글씨에 연보라 바탕 (평소 말과 구별되게)
    private static readonly Brush HintInk = Frozen(Color.FromRgb(0x6E, 0x3F, 0xC4));
    private static readonly Brush HintBack = Frozen(Color.FromRgb(0xF1, 0xEA, 0xFB));
    private static Brush Frozen(Color c) { var b = new SolidColorBrush(c); b.Freeze(); return b; }

    private void AddEggHintLine(string text)
    {
        ChatPanel.Children.Add(new Border
        {
            Background = HintBack,
            BorderBrush = HintInk,
            BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(8),
            Padding = new Thickness(9, 5, 9, 6),
            Margin = new Thickness(0, 4, 24, 4),
            Child = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, FontSize = 13, LineHeight = 20, Foreground = HintInk },
        });
        TrimChat();
    }
}
