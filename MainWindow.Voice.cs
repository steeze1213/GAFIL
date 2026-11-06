// v1.2 기능: 음성으로 말 걸기
// 🎤 버튼 또는 Ctrl+Alt+Space(기본. Ctrl+Alt+V는 Office '선택하여 붙여넣기'와 겹쳐서 피함) → 말풍선 입력창에 포커스
// → Windows 음성 입력(Win+H)을 켬 → 말한 글이 입력창에 들어오고, 말이 멈춘 뒤 잠깐(기본 1.8초) 지나면 자동으로 보냄
// Windows 음성 입력을 쓰므로 한국어 포함 여러 언어가 따로 설치 없이 됨 (Windows 설정의 음성 입력 사용)
// (답을 소리로 읽는 기능은 Windows 기본 음성이 어색해서 뺐음. 자연스러운 신경망 음성을 쓸 수 있을 때 다시 검토)
// 코드 검토로 고친 점:
//  - 우리가 보내는 Win 키·단축키의 Ctrl/Alt 같은 수식키는 '사용자가 고쳐 쓰는 중'으로 보지 않음 (듣기가 바로 꺼지던 문제)
//  - 단축키를 누른 손가락(Ctrl/Alt)을 뗀 뒤에 Win+H를 보냄 (안 그러면 Ctrl+Alt+Win+H가 돼서 음성 입력이 안 열림)
//  - 받아 적는 중(조합 중)인 글은 보내지 않음, 보낸 뒤에는 입력창에서 포커스를 빼서 음성 입력이 주변 소리를 더 받아 적지 않게
//  - 말풍선을 닫거나 Enter로 직접 보내면 듣기도 끝냄
using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Threading;

namespace ClaudeDesk;

public partial class MainWindow
{
    private const int VoiceHotkeyId = 0x6AF3;
    private string _voiceHotkeyLabel;
    private uint _voiceHotkeyVk;

    // 듣는 중(음성 입력이 켜져 있고 자동 보내기를 기다림)
    private bool _listening;
    private bool _composing;                  // 음성 입력이 아직 글자를 조합(확정 전) 중
    private string _listenStartText = "";
    private string _listenLastText = "";
    private DateTime _listenStarted, _listenLastChange;
    private DateTime _ignoreKeysUntil;        // 우리가 Win+H를 보내는 동안의 키 입력은 무시
    private bool _keepInputUnfocused;         // 말로 한 질문의 답을 기다리는 동안: 입력창에 포커스를 다시 주지 않음 (음성 입력이 계속 받아 적지 않게)
    private readonly DispatcherTimer _listenTimer = new() { Interval = TimeSpan.FromMilliseconds(250) };
    private static readonly TimeSpan ListenGiveUp = TimeSpan.FromSeconds(20);   // 이만큼 아무 말이 없으면 듣기를 그만둠

    // 사용자가 '고쳐 쓰는' 키로 보지 않을 키: 수식키, Win, 입력기(IME)가 처리한 키
    private static readonly HashSet<Key> NonEditingKeys = new()
    {
        Key.LWin, Key.RWin, Key.LeftCtrl, Key.RightCtrl, Key.LeftAlt, Key.RightAlt, Key.LeftShift, Key.RightShift,
        Key.System, Key.ImeProcessed, Key.DeadCharProcessed, Key.None, Key.HangulMode, Key.HanjaMode, Key.Enter,
    };

    private void InitVoice()
    {
        MicButton.Click += (_, _) => ToggleListening(fromHotkey: false);
        _listenTimer.Tick += (_, _) => OnListenTick();

        // 듣는 중에 사용자가 직접 글자를 고치면(지우기·글자 키) 자동 보내기를 멈춤. 수식키·입력기 키·Enter는 제외
        Input.PreviewKeyDown += (_, e) =>
        {
            if (!_listening || DateTime.Now < _ignoreKeysUntil || e.IsRepeat || NonEditingKeys.Contains(e.Key)) return;
            StopListening(null);
        };

        // 음성 입력이 글자를 조합하는 동안(확정 전)은 보내지 않음: 조합 시작·갱신 = 아직 말하는 중
        TextCompositionManager.AddPreviewTextInputStartHandler(Input, (_, _) => { _composing = true; _listenLastChange = DateTime.Now; });
        TextCompositionManager.AddPreviewTextInputUpdateHandler(Input, (_, _) => { _composing = true; _listenLastChange = DateTime.Now; });
        // 조합이 끝나 글자가 확정되면(PreviewTextInput) 다시 '말이 멈췄는지' 시간을 셈
        TextCompositionManager.AddPreviewTextInputHandler(Input, (_, _) => { _composing = false; _listenLastChange = DateTime.Now; });

        // 전역 단축키 (기본 Ctrl+Alt+Space)
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (TryParseHotkey(_settings.VoiceHotkey, out uint mods, out uint vk, out string label))
            {
                if (RegisterHotKey(hwnd, VoiceHotkeyId, mods | MOD_NOREPEAT, vk)) { _voiceHotkeyLabel = label; _voiceHotkeyVk = vk; }
                else AppSettings.Log($"[단축키] {label} 등록 실패 (다른 프로그램이 쓰는 중일 수 있음)");
            }
            HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
            {
                if (msg == WM_HOTKEY && wp.ToInt32() == VoiceHotkeyId)
                {
                    handled = true;
                    if (!_claude.ScreenActive) ToggleListening(fromHotkey: true);
                }
                return IntPtr.Zero;
            });
            MicButton.ToolTip = L.T("말로 하기", "Speak") + (_voiceHotkeyLabel != null ? $" ({_voiceHotkeyLabel})" : "") +
                L.T(" — Windows 음성 입력 사용", " — uses Windows voice typing");
        };

        Closed += (_, _) =>
        {
            try { UnregisterHotKey(new WindowInteropHelper(this).Handle, VoiceHotkeyId); } catch { }
        };
    }

    // 🎤 버튼·단축키: 듣는 중이면 멈추고, 아니면 시작 (두 번 눌러 Win+H가 꺼지는 일이 없게 같은 동작으로 맞춤)
    private void ToggleListening(bool fromHotkey)
    {
        if (_listening) StopListening(L.T("듣기를 멈췄어", "Stopped listening"));
        else StartListening(fromHotkey);
    }

    private void StartListening(bool fromHotkey)
    {
        if (_listening || _claude.ScreenActive || Thinking) return;
        _keepInputUnfocused = false;
        if (!_bubbleOpen) ToggleBubble();
        Activate();
        Input.Focus();
        Input.CaretIndex = Input.Text.Length;

        _listening = true;
        _composing = false;
        _listenStartText = _listenLastText = Input.Text;
        _listenStarted = _listenLastChange = DateTime.Now;
        _listenTimer.Start();
        MicButton.Foreground = (System.Windows.Media.Brush)FindResource("ErrorInk");
        StatusText.Text = L.T("🎤 듣는 중… 말해줘", "🎤 Listening… go ahead");
        Placeholder.Text = L.T("말하면 여기에 적혀요", "Your words will appear here");
        AppSettings.Log("[음성] 듣기 시작 (Windows 음성 입력)");

        // 단축키로 시작했으면 그 키(Ctrl·Alt 등)를 뗄 때까지 기다린 뒤 Win+H를 보냄 (눌린 채로 보내면 다른 조합이 됨)
        Dispatcher.BeginInvoke(DispatcherPriority.Input, async () =>
        {
            await Task.Delay(120);
            if (fromHotkey && !await WaitKeysReleasedAsync(TimeSpan.FromMilliseconds(1500)))
            {
                StopListening(L.T("단축키에서 손을 뗀 뒤 다시 해 줘", "Release the hotkey and try again"));
                return;
            }
            if (!_listening || !Input.IsKeyboardFocused) return;
            _ignoreKeysUntil = DateTime.Now.AddMilliseconds(600);
            ScreenInput.Combo(new[] { "MetaLeft", "KeyH" });
        });
    }

    // Ctrl·Alt·Shift·Win과 단축키 글자 키가 모두 떨어질 때까지 기다림 (최대 limit)
    private async Task<bool> WaitKeysReleasedAsync(TimeSpan limit)
    {
        int[] keys = { 0x11, 0x12, 0x10, 0x5B, 0x5C, (int)_voiceHotkeyVk };
        var until = DateTime.Now + limit;
        while (DateTime.Now < until)
        {
            if (keys.All(k => k == 0 || (GetAsyncKeyState(k) & 0x8000) == 0)) return true;
            await Task.Delay(25);
        }
        return false;
    }

    private void StopListening(string status)
    {
        if (!_listening) return;
        _listening = false;
        _composing = false;
        _listenTimer.Stop();
        MicButton.ClearValue(ForegroundProperty);
        if (status != null) StatusText.Text = status;
        RefreshAttachments();   // 안내 문구(Placeholder) 원래대로
    }

    // 말이 멈추고 잠깐 지나면 자동으로 보냄. 너무 오래 아무 말이 없으면 그만둠
    private void OnListenTick()
    {
        if (!_listening) return;
        if (!_bubbleOpen) { StopListening(null); return; }   // 말풍선을 닫았으면 보내지 않음

        var now = DateTime.Now;
        string t = Input.Text;
        if (t != _listenLastText) { _listenLastText = t; _listenLastChange = now; }

        bool hasNew = t.Trim().Length > 0 && t != _listenStartText;
        int delay = Math.Clamp(_settings.VoiceAutoSendMs, 0, 10000);
        if (hasNew && !_composing && delay > 0 && now - _listenLastChange >= TimeSpan.FromMilliseconds(delay))
        {
            _ = SubmitInputAsync(fromVoice: true);   // 안에서 듣기도 끝냄
        }
        else if (!hasNew && now - _listenStarted > ListenGiveUp)
        {
            StopListening(L.T("못 들었어. Windows 설정 → 시간 및 언어 → 음성 입력을 확인해 줘",
                "Didn't hear anything. Check Windows Settings → Time & language → Speech"));
        }
    }

    // 입력창 내용을 보냄 (Enter와 음성 자동 보내기가 함께 씀)
    private async Task SubmitInputAsync(bool fromVoice)
    {
        string question = Input.Text.Trim();
        if ((question.Length == 0 && !HasAttachments) || Thinking) return;   // 답을 기다리는 중에는 새 질문을 받지 않음

        bool wasListening = _listening;
        StopListening(null);
        Input.Clear();

        // 말로 했으면 입력창에서 포커스를 빼서 Windows 음성 입력이 주변 소리를 더 받아 적지 않게 함
        _keepInputUnfocused = fromVoice || wasListening;
        if (_keepInputUnfocused) Keyboard.ClearFocus();

        var (toClaude, toShow) = TakeAttachments(question);
        if (fromVoice) toShow = "🎤 " + toShow;
        await AskAsync(toClaude, toShow);
    }

    [DllImport("user32.dll")] private static extern short GetAsyncKeyState(int vk);
}
