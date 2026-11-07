// 처음 시작 도우미: Claude Code 설치 → Claude 연결(구독 로그인 또는 API 키)을 단계별로 안내하고 버튼 하나로 도와줌
// - 시작할 때 claude가 없거나 로그인 흔적(자격 증명 파일·API 키)이 없으면 자동으로 뜸. 메뉴 "Claude 연결 설정"으로 언제든 다시 열기
// - 설치·로그인은 보이는 창에서 공식 명령을 그대로 실행 (사용자가 무엇이 실행되는지 볼 수 있게)
// - API 키는 Claude Code가 읽는 사용자 환경 변수 ANTHROPIC_API_KEY에 저장 (GAFIL 설정 파일에는 저장하지 않음)
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;

namespace ClaudeDesk;

public sealed class SetupWindow : Window
{
    private static readonly Brush Paper = Hex("#FFFBF5"), Ink = Hex("#3A2218"), Accent = Hex("#D9653B"),
                                  Muted = Hex("#9A8274"), Chip = Hex("#F3E6DC"), Good = Hex("#3C8A4A"), Bad = Hex("#B33A2A");

    private readonly TextBlock _installState = new(), _authState = new(), _note = new();
    private readonly Button _installBtn, _loginBtn, _keyBtn, _doneBtn;
    private readonly PasswordBox _key = new() { FontSize = 13, Padding = new Thickness(6, 4, 6, 4), Margin = new Thickness(0, 0, 8, 0) };
    private readonly DispatcherTimer _poll = new() { Interval = TimeSpan.FromSeconds(2) };
    private bool _installed, _loggedIn, _checking;

    public SetupWindow()
    {
        Title = L.T("GAFIL 시작하기", "Getting started with GAFIL");
        Width = 470; SizeToContent = SizeToContent.Height; ResizeMode = ResizeMode.NoResize;
        WindowStartupLocation = WindowStartupLocation.CenterScreen; Topmost = true;
        Background = Paper; FontFamily = new FontFamily("Malgun Gothic, Segoe UI");

        _installBtn = PillButton(L.T("설치하기", "Install"), Accent, Brushes.White);
        _loginBtn = PillButton(L.T("구독 계정으로 로그인", "Log in with subscription"), Accent, Brushes.White);
        _keyBtn = PillButton(L.T("키 저장", "Save key"), Chip, Ink);
        _doneBtn = PillButton(L.T("나중에", "Later"), Chip, Ink);

        var root = new StackPanel { Margin = new Thickness(22, 18, 22, 18) };
        root.Children.Add(Text(L.T("안녕! 나랑 얘기하려면 두 가지만 준비하면 돼냥.", "Hi! Two quick things and we can talk."), 15, Ink, FontWeights.SemiBold));
        root.Children.Add(Text(L.T("버튼을 누르면 내가 도와줄게. 둘 다 ✓가 되면 끝이야.", "Press the buttons and I'll help. Once both show ✓, you're done."), 12, Muted, margin: new Thickness(0, 4, 0, 14)));

        // 1. Claude Code 설치
        root.Children.Add(Step("1", L.T("Claude Code 설치", "Install Claude Code"), _installState));
        root.Children.Add(Text(L.T("내 머리 역할을 하는 Anthropic의 공식 프로그램이야. 누르면 PowerShell 창에서 공식 설치 명령이 실행돼 (1~2분).",
                                   "Anthropic's official tool that does my thinking. Runs the official installer in a PowerShell window (1–2 min)."), 12, Muted, margin: new Thickness(26, 2, 0, 6)));
        root.Children.Add(Row(new Thickness(26, 0, 0, 16), _installBtn));

        // 2. Claude 연결
        root.Children.Add(Step("2", L.T("Claude 연결", "Connect to Claude"), _authState));
        if (Edition.ApiKeyOnly)
        {
            root.Children.Add(Text(L.T("이 배포판은 Anthropic 정책에 따라 Anthropic API 키로만 연결돼. 쓴 만큼 요금이 나가.",
                                       "Per Anthropic's policy, this edition connects only with an Anthropic API key (pay as you go)."), 12, Muted, margin: new Thickness(26, 2, 0, 8)));
        }
        else
        {
        root.Children.Add(Text(L.T("둘 중 하나만 하면 돼.", "Pick one."), 12, Muted, margin: new Thickness(26, 2, 0, 8)));
        root.Children.Add(Text(L.T("A. Claude Pro / Max 구독이 있으면 (추천)", "A. If you have Claude Pro / Max (recommended)"), 12.5, Ink, FontWeights.SemiBold, new Thickness(26, 0, 0, 2)));
        root.Children.Add(Text(L.T("누르면 브라우저가 열려. Claude 계정으로 로그인하고 '허용'을 누르면 끝.",
                                   "Your browser opens. Log in to Claude and press Authorize."), 12, Muted, margin: new Thickness(26, 0, 0, 6)));
        root.Children.Add(Row(new Thickness(26, 0, 0, 12), _loginBtn, Link(L.T("구독 알아보기", "See plans"), "https://claude.com/pricing")));
        root.Children.Add(Text(L.T("B. API 키로 쓰기 (쓴 만큼 요금)", "B. Use an API key (pay as you go)"), 12.5, Ink, FontWeights.SemiBold, new Thickness(26, 0, 0, 2)));
        }
        root.Children.Add(Text(L.T("Anthropic Console에서 키를 만들어 붙여 넣어. 이 PC의 사용자 환경 변수(ANTHROPIC_API_KEY)에 저장돼.",
                                   "Create a key in the Anthropic Console and paste it. It's saved to this PC's user environment variable ANTHROPIC_API_KEY."), 12, Muted, margin: new Thickness(26, 0, 0, 6)));
        var keyRow = new DockPanel { Margin = new Thickness(26, 0, 0, 4) };
        DockPanel.SetDock(_keyBtn, Dock.Right);
        keyRow.Children.Add(_keyBtn);
        keyRow.Children.Add(_key);
        root.Children.Add(keyRow);
        root.Children.Add(Row(new Thickness(16, 0, 0, 14), Link(L.T("API 키 만들기 (Anthropic Console)", "Create an API key (Anthropic Console)"), "https://console.anthropic.com/settings/keys")));

        _note.TextWrapping = TextWrapping.Wrap; _note.FontSize = 12; _note.Foreground = Bad; _note.Margin = new Thickness(0, 0, 0, 10);
        _note.Visibility = Visibility.Collapsed;
        root.Children.Add(_note);
        var bottom = new DockPanel { LastChildFill = false };
        DockPanel.SetDock(_doneBtn, Dock.Right);
        bottom.Children.Add(_doneBtn);
        root.Children.Add(bottom);

        Content = new Border { BorderBrush = Ink, BorderThickness = new Thickness(0, 3, 0, 0), Child = root };

        _installBtn.Click += (_, _) => RunInstall();
        _loginBtn.Click += (_, _) => RunLogin();
        _keyBtn.Click += (_, _) => SaveKey();
        _doneBtn.Click += (_, _) => Close();
        _poll.Tick += async (_, _) => { if (!_installed) await Refresh(checkAuth: false); };
        Loaded += async (_, _) => { _poll.Start(); await Refresh(checkAuth: true); };
        Closed += (_, _) => _poll.Stop();
    }

    // ---------------- 시작할 때 띄울지 (가볍게: 파일만 봄, claude를 실행하지 않음) ----------------

    public static bool NeedsSetup()
    {
        if (ClaudeClient.FindClaude() == null) return true;
        if (Edition.ApiKeyOnly) return !Edition.HasApiKey();
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"))) return false;
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_AUTH_TOKEN"))) return false;
        string dir = Environment.GetEnvironmentVariable("CLAUDE_CONFIG_DIR");
        if (string.IsNullOrWhiteSpace(dir)) dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude");
        return !File.Exists(Path.Combine(dir, ".credentials.json"));
    }

    // ---------------- 상태 확인 ----------------

    private async Task Refresh(bool checkAuth)
    {
        if (_checking) return;
        _checking = true;
        try
        {
            string exe = ClaudeClient.FindClaude();
            _installed = exe != null;
            SetState(_installState, _installed, _installed ? L.T("✓ 설치됨", "✓ Installed") : L.T("아직 없음", "Not yet"));
            _installBtn.Visibility = _installed ? Visibility.Collapsed : Visibility.Visible;

            if (!_installed) { SetState(_authState, false, L.T("설치 먼저", "Install first")); _loginBtn.IsEnabled = _keyBtn.IsEnabled = false; }
            else
            {
                _loginBtn.IsEnabled = _keyBtn.IsEnabled = true;
                if (checkAuth || !_loggedIn)
                {
                    SetState(_authState, false, L.T("확인하는 중…", "Checking…"), Muted);
                    _loggedIn = (!Edition.ApiKeyOnly || Edition.HasApiKey()) && await Task.Run(() => CheckLogin(exe));
                }
                SetState(_authState, _loggedIn, _loggedIn ? L.T("✓ 연결됨", "✓ Connected") : L.T("아직 안 됨", "Not yet"));
            }
            bool done = _installed && _loggedIn;
            _doneBtn.Content = done ? L.T("다 됐어! 시작하기", "All set! Start") : L.T("나중에", "Later");
            _doneBtn.Background = done ? Accent : Chip;
            _doneBtn.Foreground = done ? Brushes.White : Ink;
        }
        finally { _checking = false; }
    }

    // claude auth status --json → loggedIn
    private static bool CheckLogin(string exe)
    {
        try
        {
            var psi = Launch(exe, "auth status --json");
            psi.UseShellExecute = false; psi.CreateNoWindow = true; psi.RedirectStandardOutput = true; psi.RedirectStandardError = true;
            using var p = Process.Start(psi);
            if (p == null) return false;
            var read = p.StandardOutput.ReadToEndAsync();
            if (!p.WaitForExit(20000)) { try { p.Kill(true); } catch { } return false; }
            string json = read.Result;
            int at = json.IndexOf('{');
            if (at < 0) return false;
            using var doc = JsonDocument.Parse(json[at..]);
            if (!doc.RootElement.TryGetProperty("loggedIn", out var v) || v.ValueKind != JsonValueKind.True) return false;
            // API 키 전용 배포판: 구독 로그인이 아니라 API 키로 연결됐는지까지 봄
            if (!Edition.ApiKeyOnly) return true;
            return doc.RootElement.TryGetProperty("apiKeySource", out var src) && src.GetString() == "ANTHROPIC_API_KEY";
        }
        catch (Exception ex) { AppSettings.Log($"[시작 도우미] 로그인 확인 실패: {ex.Message}"); return false; }
    }

    // ---------------- 버튼 ----------------

    private void RunInstall()
    {
        ShowNote(null);
        const string cmd = "irm https://claude.ai/install.ps1 | iex; Write-Host ''; Write-Host 'GAFIL: 끝났어요. 이 창은 닫아도 돼요. / Done. You can close this window.'";
        try
        {
            Process.Start(new ProcessStartInfo("powershell.exe", $"-NoProfile -ExecutionPolicy Bypass -NoExit -Command \"{cmd}\"") { UseShellExecute = true });
            AppSettings.Log("[시작 도우미] Claude Code 설치 창을 엶");
            SetState(_installState, false, L.T("설치하는 중… (창이 끝나면 저절로 ✓)", "Installing… (✓ appears when done)"), Muted);
        }
        catch (Exception ex) { ShowNote(L.T("설치 창을 열지 못했어: ", "Couldn't open the installer: ") + ex.Message); }
    }

    private async void RunLogin()
    {
        if (Edition.ApiKeyOnly) return;   // 배포판에는 구독 로그인이 없음
        ShowNote(null);
        string exe = ClaudeClient.FindClaude();
        if (exe == null) { await Refresh(true); return; }
        try
        {
            var psi = Launch(exe, "auth login --claudeai");
            psi.UseShellExecute = false;   // 보이는 콘솔 창 (브라우저 주소가 안 열릴 때 거기서 복사)
            using var p = Process.Start(psi);
            AppSettings.Log("[시작 도우미] 구독 로그인 창을 엶");
            SetState(_authState, false, L.T("브라우저에서 로그인해 줘…", "Log in in your browser…"), Muted);
            _loginBtn.IsEnabled = false;
            if (p != null) await Task.Run(() => p.WaitForExit(10 * 60 * 1000));
        }
        catch (Exception ex) { ShowNote(L.T("로그인 창을 열지 못했어: ", "Couldn't start login: ") + ex.Message); }
        _loginBtn.IsEnabled = true;
        await Refresh(true);
        if (!_loggedIn) ShowNote(L.T("아직 연결이 안 됐어. 브라우저에서 '허용'까지 눌렀는지 보고 다시 해 봐.", "Not connected yet. Make sure you pressed Authorize in the browser, then try again."));
    }

    private async void SaveKey()
    {
        string key = _key.Password.Trim();
        if (!key.StartsWith("sk-ant-", StringComparison.Ordinal) || key.Length < 20 || key.Any(char.IsWhiteSpace))
        {
            ShowNote(L.T("API 키는 sk-ant- 로 시작해. 통째로 복사해서 붙여 넣어 줘.", "API keys start with sk-ant-. Paste the whole key."));
            return;
        }
        ShowNote(null);
        try
        {
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", key, EnvironmentVariableTarget.User);    // 다음에 켤 때도
            Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", key, EnvironmentVariableTarget.Process); // 지금 GAFIL이 띄우는 claude에도
            _key.Clear();
            AppSettings.Log("[시작 도우미] API 키를 사용자 환경 변수에 저장함 (값은 기록하지 않음)");
        }
        catch (Exception ex) { ShowNote(L.T("키를 저장하지 못했어: ", "Couldn't save the key: ") + ex.Message); return; }
        await Refresh(true);
        if (!_loggedIn) ShowNote(L.T("키는 저장했는데 연결 확인이 안 돼. 키가 맞는지 Console에서 확인해 줘.", "Saved, but the connection check failed. Double-check the key in the Console."));
    }

    // npm 설치의 claude.cmd는 cmd.exe를 거쳐야 실행됨
    private static ProcessStartInfo Launch(string exe, string args) =>
        exe.EndsWith(".cmd", StringComparison.OrdinalIgnoreCase)
            ? new ProcessStartInfo("cmd.exe", $"/d /s /c \"\"{exe}\" {args}\"")
            : new ProcessStartInfo(exe, args);

    // ---------------- 모양 ----------------

    private void ShowNote(string text)
    {
        _note.Text = text ?? "";
        _note.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
    }

    private static void SetState(TextBlock tb, bool ok, string text, Brush color = null)
    {
        tb.Text = text;
        tb.Foreground = color ?? (ok ? Good : Bad);
    }

    private static UIElement Step(string n, string title, TextBlock state)
    {
        var dp = new DockPanel();
        state.FontSize = 12.5; state.FontWeight = FontWeights.SemiBold; state.VerticalAlignment = VerticalAlignment.Center;
        DockPanel.SetDock(state, Dock.Right);
        dp.Children.Add(state);
        var badge = new Border
        {
            Width = 20, Height = 20, CornerRadius = new CornerRadius(10), Background = Ink, Margin = new Thickness(0, 0, 6, 0),
            Child = new TextBlock { Text = n, Foreground = Paper, FontSize = 11.5, FontWeight = FontWeights.Bold, HorizontalAlignment = HorizontalAlignment.Center, VerticalAlignment = VerticalAlignment.Center },
        };
        dp.Children.Add(badge);
        dp.Children.Add(new TextBlock { Text = title, FontSize = 14, FontWeight = FontWeights.SemiBold, Foreground = Ink, VerticalAlignment = VerticalAlignment.Center });
        return dp;
    }

    private static TextBlock Text(string s, double size, Brush color, FontWeight? weight = null, Thickness? margin = null) =>
        new() { Text = s, FontSize = size, Foreground = color, FontWeight = weight ?? FontWeights.Normal, TextWrapping = TextWrapping.Wrap, Margin = margin ?? new Thickness(0) };

    private static StackPanel Row(Thickness margin, params UIElement[] items)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Margin = margin };
        foreach (var i in items) sp.Children.Add(i);
        return sp;
    }

    private static TextBlock Link(string text, string url)
    {
        var tb = new TextBlock { Text = text, FontSize = 12, Foreground = Accent, TextDecorations = TextDecorations.Underline, Cursor = System.Windows.Input.Cursors.Hand, VerticalAlignment = VerticalAlignment.Center, Margin = new Thickness(10, 0, 0, 0) };
        tb.MouseLeftButtonUp += (_, _) => { try { Process.Start(new ProcessStartInfo(url) { UseShellExecute = true }); } catch { } };
        return tb;
    }

    private static Button PillButton(string text, Brush bg, Brush fg)
    {
        var template = new ControlTemplate(typeof(Button));
        var border = new FrameworkElementFactory(typeof(Border));
        border.SetValue(Border.CornerRadiusProperty, new CornerRadius(6));
        border.SetValue(Border.PaddingProperty, new Thickness(16, 7, 16, 7));
        border.SetBinding(Border.BackgroundProperty, new System.Windows.Data.Binding("Background") { RelativeSource = System.Windows.Data.RelativeSource.TemplatedParent });
        var cp = new FrameworkElementFactory(typeof(ContentPresenter));
        cp.SetValue(ContentPresenter.HorizontalAlignmentProperty, HorizontalAlignment.Center);
        border.AppendChild(cp);
        template.VisualTree = border;
        var b = new Button { Content = text, Background = bg, Foreground = fg, FontSize = 13, FontWeight = FontWeights.SemiBold, Cursor = System.Windows.Input.Cursors.Hand, Template = template };
        b.IsEnabledChanged += (_, _) => b.Opacity = b.IsEnabled ? 1 : 0.45;
        return b;
    }

    private static Brush Hex(string s) { var b = (Brush)new BrushConverter().ConvertFromString(s); b.Freeze(); return b; }
}
