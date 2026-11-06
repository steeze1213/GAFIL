// 5단계 GAFIL 화면 조작: 화면 캡처(JPEG)와 마우스·키보드 입력 넣기(SendInput)
// 주 모니터만 다룸. 좌표는 0~1로 정규화(normalize)해서 주고받음 → 해상도·배율과 무관
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;

namespace ClaudeDesk;

public static class ScreenInput
{
    // ---------------- 화면 캡처 ----------------

    private static readonly ImageCodecInfo JpegCodec =
        ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);

    // 주 모니터를 캡처해서 폭 maxWidth 이하로 줄인 JPEG 바이트로 돌려줌. 마우스 커서도 그려 넣음
    // outW/outH: 만들어진 이미지 크기 (GAFIL이 이 크기 기준으로 클릭 좌표를 말함)
    public static byte[] CaptureJpeg(int maxWidth, long quality, out int outW, out int outH)
    {
        // 이 스레드만 '모니터별 DPI 인식(Per-Monitor V2)'으로 바꿈 → 배율이 다른 모니터를 오가도 실제 픽셀로 캡처
        // (앱 전체는 WPF 기본값인 '시스템 DPI 인식'이라, 주 모니터 배율이 로그인 때와 다르면 캡처가 어긋날 수 있음)
        IntPtr prev = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        try { return CaptureJpegCore(maxWidth, quality, out outW, out outH); }
        finally { if (prev != IntPtr.Zero) SetThreadDpiAwarenessContext(prev); }
    }

    // 지금 스크린샷을 찍으면 나올 이미지 크기 (스크린샷 없이 좌표를 받았을 때 사용)
    public static (int W, int H) ShotSize(int maxWidth)
    {
        IntPtr prev = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        try
        {
            var (w, h) = (GetSystemMetrics(SM_CXSCREEN), GetSystemMetrics(SM_CYSCREEN));
            return Scaled(w, h, maxWidth);
        }
        finally { if (prev != IntPtr.Zero) SetThreadDpiAwarenessContext(prev); }
    }

    // 긴 변은 maxEdge 이하, 넓이는 115만 픽셀 이하로 줄임.
    // 이유: Claude API는 이보다 큰 이미지를 몰래 더 줄여서 보여줌 → 모델이 말하는 좌표와 우리가 나누는 크기가 어긋남
    // (세로 모니터·4:3 모니터에서 클릭이 왼쪽 위로 밀리는 문제)
    private static (int, int) Scaled(int w, int h, int maxEdge)
    {
        double scale = Math.Min(1.0, Math.Min((double)maxEdge / Math.Max(w, h), Math.Sqrt(1_150_000.0 / ((double)w * h))));
        return (Math.Max(1, (int)(w * scale)), Math.Max(1, (int)(h * scale)));
    }

    private static byte[] CaptureJpegCore(int maxWidth, long quality, out int outW, out int outH)
    {
        outW = outH = 0;
        int w = GetSystemMetrics(SM_CXSCREEN);   // 주 모니터의 실제 픽셀 크기
        int h = GetSystemMetrics(SM_CYSCREEN);
        if (w <= 0 || h <= 0) return null;

        using var full = new Bitmap(w, h, PixelFormat.Format24bppRgb);
        using (var g = Graphics.FromImage(full))
        {
            g.CopyFromScreen(0, 0, 0, 0, new Size(w, h), CopyPixelOperation.SourceCopy);
            DrawCursor(g);
        }

        // Claude에게 보내기 좋은 크기로 줄임 (전송량을 줄이고, 좌표를 읽기 쉬운 크기로)
        var (tw, th) = Scaled(w, h, maxWidth);
        outW = tw;
        outH = th;
        using var small = tw < w || th < h ? new Bitmap(tw, th, PixelFormat.Format24bppRgb) : null;
        if (small != null)
        {
            using var g2 = Graphics.FromImage(small);
            g2.InterpolationMode = InterpolationMode.HighQualityBilinear;
            g2.DrawImage(full, 0, 0, tw, th);
        }

        using var ms = new MemoryStream();
        using var ep = new EncoderParameters(1);
        ep.Param[0] = new EncoderParameter(Encoder.Quality, quality);
        (small ?? full).Save(ms, JpegCodec, ep);
        return ms.ToArray();
    }

    // CopyFromScreen은 커서를 찍지 않아서 직접 그림 (GAFIL이 커서 위치를 알 수 있게)
    private static void DrawCursor(Graphics g)
    {
        var ci = new CURSORINFO { cbSize = Marshal.SizeOf<CURSORINFO>() };
        if (!GetCursorInfo(ref ci) || (ci.flags & CURSOR_SHOWING) == 0) return;

        int hotX = 0, hotY = 0;
        if (GetIconInfo(ci.hCursor, out var ii))
        {
            hotX = ii.xHotspot;
            hotY = ii.yHotspot;
            if (ii.hbmMask != IntPtr.Zero) DeleteObject(ii.hbmMask);   // GetIconInfo가 만든 비트맵은 직접 지워야 함
            if (ii.hbmColor != IntPtr.Zero) DeleteObject(ii.hbmColor);
        }

        IntPtr hdc = g.GetHdc();
        try { DrawIconEx(hdc, ci.ptScreenPos.X - hotX, ci.ptScreenPos.Y - hotY, ci.hCursor, 0, 0, 0, IntPtr.Zero, DI_NORMAL); }
        finally { g.ReleaseHdc(hdc); }
    }

    // ---------------- 커서 위치 ----------------

    // 지금 마우스 커서 위치를 주 모니터 비율(0~1)로. 다른 모니터에 있으면 0~1 밖의 값
    public static (double X, double Y, int W, int H) CursorNormalized()
    {
        IntPtr prev = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        try
        {
            int w = GetSystemMetrics(SM_CXSCREEN), h = GetSystemMetrics(SM_CYSCREEN);
            // 커서를 못 읽으면(보안 화면 등) 크기 0을 돌려줌 → 부르는 쪽에서 '움직임 없음'으로 처리 (0,0으로 오해하지 않게)
            if (!GetCursorPos(out var p) || w <= 0 || h <= 0) return (0, 0, 0, 0);
            return ((p.X + 0.5) / w, (p.Y + 0.5) / h, w, h);
        }
        finally { if (prev != IntPtr.Zero) SetThreadDpiAwarenessContext(prev); }
    }

    // ---------------- 마우스 ----------------

    // x, y: 0~1 (주 모니터 기준)
    public static bool MouseMove(double x, double y)
    {
        // 절대 좌표는 0~65535 범위로 표현함 (SendInput 규칙)
        int ax = (int)Math.Round(Math.Clamp(x, 0, 1) * 65535);
        int ay = (int)Math.Round(Math.Clamp(y, 0, 1) * 65535);
        return Send(MouseInput(ax, ay, 0, MOUSEEVENTF_MOVE | MOUSEEVENTF_ABSOLUTE));
    }

    // button: 0=주 버튼(보통 왼쪽), 1=가운데, 2=보조 버튼(보통 오른쪽)
    public static bool MouseButton(int button, bool down)
    {
        // 왼손잡이 설정(버튼 바꾸기)이면 SendInput의 '왼쪽'이 보조 버튼이 됨 → 바꿔서 보내야 '클릭'이 클릭이 됨
        // 사용자가 실행 중에 설정을 바꿀 수 있어 매번 확인
        if (button != 1 && GetSystemMetrics(SM_SWAPBUTTON) != 0) button = 2 - button;
        uint flag = button switch
        {
            1 => down ? MOUSEEVENTF_MIDDLEDOWN : MOUSEEVENTF_MIDDLEUP,
            2 => down ? MOUSEEVENTF_RIGHTDOWN : MOUSEEVENTF_RIGHTUP,
            _ => down ? MOUSEEVENTF_LEFTDOWN : MOUSEEVENTF_LEFTUP,
        };
        return Send(MouseInput(0, 0, 0, flag));
    }

    // delta: 위로 굴리면 양수 (한 칸 = 120)
    public static bool MouseWheel(int delta) =>
        Send(MouseInput(0, 0, unchecked((uint)Math.Clamp(delta, -1200, 1200)), MOUSEEVENTF_WHEEL));

    // ---------------- 키보드 ----------------

    // 브라우저 KeyboardEvent.code → Windows 가상 키 코드(virtual-key code). 확장 키(extended key) 여부도 함께
    private static readonly Dictionary<string, (ushort Vk, bool Ext)> KeyMap = BuildKeyMap();

    private static Dictionary<string, (ushort, bool)> BuildKeyMap()
    {
        var m = new Dictionary<string, (ushort, bool)>(StringComparer.Ordinal);
        for (char c = 'A'; c <= 'Z'; c++) m["Key" + c] = (c, false);
        for (char d = '0'; d <= '9'; d++) m["Digit" + d] = (d, false);
        for (int i = 1; i <= 24; i++) m["F" + i] = ((ushort)(0x6F + i), false);
        for (int i = 0; i <= 9; i++) m["Numpad" + i] = ((ushort)(0x60 + i), false);

        (string, ushort, bool)[] keys =
        {
            ("Enter", 0x0D, false), ("NumpadEnter", 0x0D, true), ("Escape", 0x1B, false), ("Backspace", 0x08, false),
            ("Tab", 0x09, false), ("Space", 0x20, false), ("CapsLock", 0x14, false),
            ("ArrowLeft", 0x25, true), ("ArrowUp", 0x26, true), ("ArrowRight", 0x27, true), ("ArrowDown", 0x28, true),
            ("Insert", 0x2D, true), ("Delete", 0x2E, true), ("Home", 0x24, true), ("End", 0x23, true),
            ("PageUp", 0x21, true), ("PageDown", 0x22, true),
            ("ShiftLeft", 0xA0, false), ("ShiftRight", 0xA1, false),
            ("ControlLeft", 0xA2, false), ("ControlRight", 0xA3, true),
            ("AltLeft", 0xA4, false), ("AltRight", 0xA5, true),
            ("MetaLeft", 0x5B, true), ("MetaRight", 0x5C, true), ("ContextMenu", 0x5D, true),
            ("Minus", 0xBD, false), ("Equal", 0xBB, false), ("BracketLeft", 0xDB, false), ("BracketRight", 0xDD, false),
            ("Backslash", 0xDC, false), ("Semicolon", 0xBA, false), ("Quote", 0xDE, false), ("Comma", 0xBC, false),
            ("Period", 0xBE, false), ("Slash", 0xBF, false), ("Backquote", 0xC0, false),
            ("Lang1", 0x15, false), ("Lang2", 0x19, false),   // 한/영, 한자
            ("PrintScreen", 0x2C, true), ("NumLock", 0x90, true), ("ScrollLock", 0x91, false), ("Pause", 0x13, false),
            ("IntlBackslash", 0xE2, false), ("IntlRo", 0xC1, false), ("IntlYen", 0xDC, false),
            ("NumpadAdd", 0x6B, false), ("NumpadSubtract", 0x6D, false), ("NumpadMultiply", 0x6A, false),
            ("NumpadDivide", 0x6F, true), ("NumpadDecimal", 0x6E, false),
        };
        foreach (var (code, vk, ext) in keys) m[code] = (vk, ext);
        return m;
    }

    // 키 하나 누르기/떼기. 모르는 키는 무시하고 false
    public static bool Key(string code, bool down)
    {
        if (code == null || !KeyMap.TryGetValue(code, out var k)) return false;
        uint flags = (k.Ext ? KEYEVENTF_EXTENDEDKEY : 0) | (down ? 0 : KEYEVENTF_KEYUP);
        return Send(KeyInput(k.Vk, 0, flags));
    }

    // 조합 키(예: Ctrl+C): 순서대로 누르고 거꾸로 뗌
    public static bool Combo(IReadOnlyList<string> codes)
    {
        if (codes == null || codes.Count == 0 || codes.Count > 4 || codes.Any(c => c == null || !KeyMap.ContainsKey(c))) return false;
        bool ok = true;
        foreach (string c in codes) ok &= Key(c, true);
        for (int i = codes.Count - 1; i >= 0; i--) ok &= Key(codes[i], false);   // 실패해도 떼기는 끝까지
        return ok;
    }

    // 글자 입력: 휴대폰 키보드의 한글 등 아무 글자나 유니코드(Unicode)로 그대로 넣음
    public static bool Text(string s)
    {
        if (string.IsNullOrEmpty(s)) return true;
        if (s.Length > 500)
        {
            // 한 번에 너무 많이 넣지 않게 자르되, 이모지처럼 두 칸짜리 글자(서로게이트 쌍)를 반으로 자르지 않음
            int cut = char.IsHighSurrogate(s[499]) ? 499 : 500;
            s = s[..cut];
        }
        var inputs = new List<INPUT>();
        foreach (char ch in s)
        {
            if (ch == '\r') continue;
            if (ch == '\n')
            {
                inputs.Add(KeyInput(0x0D, 0, 0));
                inputs.Add(KeyInput(0x0D, 0, KEYEVENTF_KEYUP));
                continue;
            }
            inputs.Add(KeyInput(0, ch, KEYEVENTF_UNICODE));
            inputs.Add(KeyInput(0, ch, KEYEVENTF_UNICODE | KEYEVENTF_KEYUP));
        }
        return Send(inputs.ToArray());
    }

    // ---------------- Win32 ----------------

    // 보낸 입력이 모두 들어갔으면 true. 관리자 권한 창(UAC 등)이 앞에 있으면 Windows가 막아서(UIPI) false
    private static bool Send(params INPUT[] inputs)
    {
        if (inputs.Length == 0) return true;
        // 마우스 절대 좌표(0~65535)는 프로세스의 DPI 인식에 따라 해석이 달라짐:
        // DPI 비인식이면 150% 배율 화면에서 2/3 위치로 가버림 (실험으로 확인).
        // 앱 전체를 app.manifest에서 Per-Monitor V2로 선언했고(이게 핵심), 여기서도 스레드를 한 번 더 맞춤
        IntPtr prev = SetThreadDpiAwarenessContext(DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2);
        try
        {
            return SendInput((uint)inputs.Length, inputs, Marshal.SizeOf<INPUT>()) == inputs.Length;
        }
        finally { if (prev != IntPtr.Zero) SetThreadDpiAwarenessContext(prev); }
    }

    private static INPUT MouseInput(int dx, int dy, uint data, uint flags) => new()
    {
        type = INPUT_MOUSE,
        u = new InputUnion { mi = new MOUSEINPUT { dx = dx, dy = dy, mouseData = data, dwFlags = flags } },
    };

    private static INPUT KeyInput(ushort vk, ushort scan, uint flags) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion { ki = new KEYBDINPUT { wVk = vk, wScan = scan, dwFlags = flags } },
    };

    private const uint INPUT_MOUSE = 0, INPUT_KEYBOARD = 1;
    private const uint MOUSEEVENTF_MOVE = 0x0001, MOUSEEVENTF_LEFTDOWN = 0x0002, MOUSEEVENTF_LEFTUP = 0x0004,
        MOUSEEVENTF_RIGHTDOWN = 0x0008, MOUSEEVENTF_RIGHTUP = 0x0010, MOUSEEVENTF_MIDDLEDOWN = 0x0020,
        MOUSEEVENTF_MIDDLEUP = 0x0040, MOUSEEVENTF_WHEEL = 0x0800, MOUSEEVENTF_ABSOLUTE = 0x8000;
    private const uint KEYEVENTF_EXTENDEDKEY = 0x1, KEYEVENTF_KEYUP = 0x2, KEYEVENTF_UNICODE = 0x4;
    private const int SM_CXSCREEN = 0, SM_CYSCREEN = 1, SM_SWAPBUTTON = 23;
    private const int CURSOR_SHOWING = 0x1, DI_NORMAL = 0x3;

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT { public uint type; public InputUnion u; }

    // 마우스·키보드 입력 구조체는 같은 자리를 나눠 씀 (C의 union)
    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT { public ushort wVk, wScan; public uint dwFlags, time; public IntPtr dwExtraInfo; }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    [StructLayout(LayoutKind.Sequential)]
    private struct CURSORINFO { public int cbSize, flags; public IntPtr hCursor; public POINT ptScreenPos; }

    [StructLayout(LayoutKind.Sequential)]
    private struct ICONINFO { public bool fIcon; public int xHotspot, yHotspot; public IntPtr hbmMask, hbmColor; }

    [DllImport("user32.dll", SetLastError = true)] private static extern uint SendInput(uint n, INPUT[] inputs, int size);
    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);
    [DllImport("user32.dll")] private static extern bool GetCursorInfo(ref CURSORINFO ci);
    [DllImport("user32.dll")] private static extern bool GetCursorPos(out POINT p);
    [DllImport("user32.dll")] private static extern bool GetIconInfo(IntPtr hIcon, out ICONINFO info);
    [DllImport("user32.dll")] private static extern bool DrawIconEx(IntPtr hdc, int x, int y, IntPtr hIcon, int cx, int cy, int step, IntPtr brush, int flags);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr obj);
    private static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);
    [DllImport("user32.dll")] private static extern IntPtr SetThreadDpiAwarenessContext(IntPtr context);
}
