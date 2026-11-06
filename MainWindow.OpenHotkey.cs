// v1.1 기능: 전역 단축키로 GAFIL 부르기 (기본 Ctrl+Alt+G)
// 어떤 프로그램을 쓰고 있든 단축키를 누르면 말풍선이 열리고 입력창에 바로 글을 쓸 수 있음
// settings.json의 "OpenHotkey"로 바꾸거나 ""(빈 값)으로 끌 수 있음
using System.Windows.Interop;

namespace ClaudeDesk;

public partial class MainWindow
{
    private const int OpenHotkeyId = 0x6AF2;
    private string _openHotkeyLabel;   // 메뉴에 보여줄 단축키 이름 (등록 실패·꺼짐이면 null)

    private void InitOpenHotkey()
    {
        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            if (TryParseHotkey(_settings.OpenHotkey, out uint mods, out uint vk, out string label))
            {
                if (RegisterHotKey(hwnd, OpenHotkeyId, mods | MOD_NOREPEAT, vk)) _openHotkeyLabel = label;
                else AppSettings.Log($"[단축키] {label} 등록 실패 (다른 프로그램이 쓰는 중일 수 있음)");
            }
            else if (!string.IsNullOrWhiteSpace(_settings.OpenHotkey))
            {
                AppSettings.Log($"[단축키] OpenHotkey 값을 이해하지 못함: {_settings.OpenHotkey}");
            }

            HwndSource.FromHwnd(hwnd)?.AddHook((IntPtr h, int msg, IntPtr wp, IntPtr lp, ref bool handled) =>
            {
                if (msg == WM_HOTKEY && wp.ToInt32() == OpenHotkeyId)
                {
                    handled = true;
                    OnOpenHotkey();
                }
                return IntPtr.Zero;
            });
            UpdateTalkMenuHeader();
        };

        Closed += (_, _) =>
        {
            try { UnregisterHotKey(new WindowInteropHelper(this).Handle, OpenHotkeyId); } catch { }
        };
    }

    // 단축키를 누름: 닫혀 있으면 열고, 열려 있으면 입력창으로 포커스만 가져옴
    // (화면 조작 중에는 무시: GAFIL이 쓰는 창의 포커스를 뺏지 않게)
    private void OnOpenHotkey()
    {
        if (_claude.ScreenActive) return;
        if (!_bubbleOpen) ToggleBubble();
        else
        {
            Activate();
            Input.Focus();
        }
    }

    private void UpdateTalkMenuHeader() =>
        MenuTalk.Header = L.T("GAFIL과 대화", "Talk to GAFIL") + (_openHotkeyLabel != null ? $"   ({_openHotkeyLabel})" : "");

    // "Ctrl+Alt+G", "Ctrl+Shift+Space", "Win+F9" 같은 글자를 RegisterHotKey 값으로 바꿈
    // 수식키(Ctrl/Alt/Shift/Win) 없이 한 키만 쓰는 것은 막음 (평소 타이핑을 가로채지 않게)
    internal static bool TryParseHotkey(string text, out uint mods, out uint vk, out string label)
    {
        mods = 0;
        vk = 0;
        label = null;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var names = new List<string>();
        foreach (string raw in text.Split('+', StringSplitOptions.TrimEntries))
        {
            string p = raw.ToLowerInvariant();
            switch (p)
            {
                case "ctrl" or "control": mods |= MOD_CONTROL; names.Add("Ctrl"); continue;
                case "alt": mods |= MOD_ALT; names.Add("Alt"); continue;
                case "shift": mods |= MOD_SHIFT; names.Add("Shift"); continue;
                case "win" or "windows": mods |= MOD_WIN; names.Add("Win"); continue;
            }
            if (vk != 0) return false;   // 일반 키는 하나만
            if (p.Length == 1 && p[0] is >= 'a' and <= 'z') vk = (uint)char.ToUpperInvariant(p[0]);
            else if (p.Length == 1 && p[0] is >= '0' and <= '9') vk = p[0];
            else if (p == "space") vk = 0x20;
            else if (p.Length >= 2 && p[0] == 'f' && int.TryParse(p[1..], out int f) && f is >= 1 and <= 24) vk = (uint)(0x6F + f);
            else return false;
            names.Add(raw.Length == 1 ? raw.ToUpperInvariant() : char.ToUpperInvariant(raw[0]) + raw[1..].ToLowerInvariant());
        }
        if (vk == 0 || mods == 0) return false;
        label = string.Join("+", names);
        return true;
    }

    private const uint MOD_WIN = 0x8;
}
