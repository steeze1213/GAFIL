// 5단계 GAFIL 화면 조작 도구: Claude CLI에 GAFIL 안에서 도는 MCP 서버(in-process, "sdk" 방식)로 제공
// CLI가 control_request(mcp_message)로 JSON-RPC 메시지를 보내면 여기서 처리해 결과를 돌려줌
// 도구: list_monitors, screenshot, click, drag, move, type, key, scroll, wait
// 좌표는 '마지막 스크린샷 이미지'의 픽셀 기준 → 그 스크린샷을 찍은 모니터의 실제 픽셀로 바꿔서 입력
// v1.3: 여러 모니터 지원 — screenshot(monitor=n)으로 원하는 모니터를 보고, 그 모니터에서 클릭·입력
using System.Text.Json.Nodes;

namespace ClaudeDesk;

public sealed class ScreenTools
{
    public const string ServerName = "gafil";
    private const int ShotMaxWidth = 1280;   // Claude가 좌표를 잘 읽는 크기 (2560x1600 화면 → 1280x800)
    private const long ShotQuality = 70;

    // 마지막 스크린샷의 모니터와 이미지 크기 (클릭 좌표를 그 모니터의 실제 픽셀로 바꿀 때 사용)
    private MonitorRect _mon;
    private int _shotW, _shotH;

    // 동작 하나마다 알림 (빨간 띠 안내·로그). 문자열 예: "클릭 (640, 400)"
    public Action<string> OnAction { get; set; }

    // 지금 화면 조작이 허용된 상태인지 (승인·긴급 정지는 ClaudeClient가 관리). false면 실행하지 않음
    public Func<bool> IsAllowed { get; set; } = () => false;

    // 클릭 예고: 누르기 전에 그 자리에 표시를 띄우고 이만큼(ms) 기다림. 0이면 예고 없음
    public Func<int> PreviewMs { get; set; } = () => 0;
    public Action<int, int> ShowMarker { get; set; }   // 실제 픽셀 (모든 모니터 공통 좌표)
    public Action HideMarker { get; set; }
    // 사용자가 마우스를 직접 움직임 → GAFIL 조작을 멈춤 (ClaudeClient가 긴급 정지와 같게 처리)
    public Action UserTookOver { get; set; }

    private const double TakeoverPx = 10;   // 이만큼(실제 픽셀) 넘게 커서가 움직이면 사용자가 잡은 것으로 봄
    private (int X, int Y)? _lastSet;  // GAFIL이 마지막으로 커서를 놓은 자리 (실제 픽셀)

    // 승인 버튼을 누른 손이 마우스에서 떨어질 시간 (이 동안은 마우스 움직임을 '가로채기'로 보지 않음)
    private static readonly TimeSpan HandOffGrace = TimeSpan.FromSeconds(1.5);
    private DateTime _graceUntil = DateTime.MinValue;

    // 새 화면 조작 작업을 시작할 때 (승인 직후) 기록을 지움
    public void ResetSession()
    {
        _lastSet = null;
        _graceUntil = DateTime.Now + HandOffGrace;
    }

    private bool InGrace => DateTime.Now < _graceUntil;

    // 사용자가 마우스를 움직였는지: GAFIL이 마지막으로 놓은 자리에서 커서가 벗어났으면 true
    private bool UserMovedSinceLastAction()
    {
        if (InGrace || _lastSet is not (int lx, int ly)) return false;
        var c = ScreenInput.CursorPhysical();
        if (!c.Ok) return false;   // 커서를 못 읽음 → 움직임 없음으로 봄
        return Math.Abs(c.X - lx) > TakeoverPx || Math.Abs(c.Y - ly) > TakeoverPx;
    }

    // 긴 동작(끌기·스크롤·더블클릭) 중간에 확인: 멈춤 또는 가로채기
    private bool Interrupted() => !IsAllowed() || UserMovedSinceLastAction();

    // 실행 전 확인 + 예고: 문제 없으면 null, 멈춰야 하면 오류 결과
    private async Task<JsonObject> GuardAsync(int? px = null, int? py = null)
    {
        if (UserMovedSinceLastAction()) return TookOver();
        int ms = PreviewMs();
        if (px is not int x || py is not int y || ms <= 0) return null;

        ShowMarker?.Invoke(x, y);
        try
        {
            var start = ScreenInput.CursorPhysical();
            for (int t = 0; t < ms; t += 50)
            {
                await Task.Delay(50);
                if (!IsAllowed()) return Error("사용자가 멈췄어.");
                var now = ScreenInput.CursorPhysical();
                if (InGrace || !start.Ok) { start = now; continue; }   // 승인 직후엔 손이 떨어지는 움직임을 무시하고 기준점만 갱신
                if (now.Ok && (Math.Abs(now.X - start.X) > TakeoverPx || Math.Abs(now.Y - start.Y) > TakeoverPx))
                    return TookOver();
            }
        }
        finally { HideMarker?.Invoke(); }
        return null;
    }

    private JsonObject TookOver()
    {
        AppSettings.Log("[화면 조작] 사용자가 마우스를 움직여서 멈춤");
        UserTookOver?.Invoke();
        return Error("사용자가 직접 마우스를 움직여서 화면 조작을 멈췄어. 이번 요청에서는 화면 조작을 다시 시도하지 마.");
    }

    // ---------------- MCP(JSON-RPC) 처리 ----------------

    public async Task<JsonObject> HandleAsync(JsonObject msg)
    {
        string method = Str(msg["method"]);
        JsonNode id = msg["id"]?.DeepClone();

        JsonNode result = method switch
        {
            "initialize" => new JsonObject
            {
                ["protocolVersion"] = Str(msg["params"]?["protocolVersion"]) ?? "2024-11-05",
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                ["serverInfo"] = new JsonObject { ["name"] = ServerName, ["version"] = "1.0" },
            },
            "tools/list" => new JsonObject { ["tools"] = ToolDefinitions() },
            "tools/call" => await CallAsync(Str(msg["params"]?["name"]), msg["params"]?["arguments"] as JsonObject ?? new JsonObject()),
            _ => new JsonObject(),   // notifications/initialized 등은 빈 결과
        };
        return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["result"] = result };
    }

    // 도구 설명: Claude가 읽고 언제·어떻게 쓸지 판단하는 글 (영어가 도구 사용 정확도가 높아서 영어로)
    private static JsonArray ToolDefinitions()
    {
        JsonObject Num(string d) => new() { ["type"] = "number", ["description"] = d };
        JsonObject Tool(string name, string desc, JsonObject props, params string[] required) => new()
        {
            ["name"] = name,
            ["description"] = desc,
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = props,
                ["required"] = new JsonArray(required.Select(r => (JsonNode)r).ToArray()),
            },
        };

        return new JsonArray(
            Tool("list_monitors", "List the user's monitors (number, size, position, which is primary, which has the mouse cursor). Use it when a window may be on another monitor.",
                new JsonObject()),
            Tool("screenshot", "Capture one monitor as a JPEG. monitor: number from list_monitors (default: the monitor of the previous screenshot, initially the primary). All x/y coordinates for click/move/drag/scroll are pixels in the LATEST screenshot and act on that same monitor. Take a new screenshot after actions to verify the result.",
                new JsonObject { ["monitor"] = new JsonObject { ["type"] = "integer", ["description"] = "1 = primary" } }),
            Tool("click", "Click at (x, y) in screenshot pixels.",
                new JsonObject
                {
                    ["x"] = Num("x in screenshot pixels"), ["y"] = Num("y in screenshot pixels"),
                    ["button"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("left", "right", "middle"), ["description"] = "default left" },
                    ["double"] = new JsonObject { ["type"] = "boolean", ["description"] = "double-click" },
                }, "x", "y"),
            Tool("move", "Move the mouse to (x, y) without clicking (e.g. to reveal a hover menu).",
                new JsonObject { ["x"] = Num("x"), ["y"] = Num("y") }, "x", "y"),
            Tool("drag", "Press the left button at (x1, y1), drag to (x2, y2), release.",
                new JsonObject { ["x1"] = Num("start x"), ["y1"] = Num("start y"), ["x2"] = Num("end x"), ["y2"] = Num("end y") }, "x1", "y1", "x2", "y2"),
            Tool("type", "Type text into the focused element (any language, including Korean). Use \\n for Enter. Click the target field first.",
                new JsonObject { ["text"] = new JsonObject { ["type"] = "string" } }, "text"),
            Tool("key", "Press a key or shortcut, e.g. \"enter\", \"esc\", \"tab\", \"ctrl+c\", \"ctrl+shift+t\", \"alt+tab\", \"win\", \"win+r\", \"alt+f4\", \"pagedown\", \"f5\".",
                new JsonObject { ["keys"] = new JsonObject { ["type"] = "string" } }, "keys"),
            Tool("scroll", "Scroll the mouse wheel at (x, y). amount = number of notches (default 3).",
                new JsonObject
                {
                    ["x"] = Num("x"), ["y"] = Num("y"),
                    ["direction"] = new JsonObject { ["type"] = "string", ["enum"] = new JsonArray("up", "down") },
                    ["amount"] = Num("notches, 1-15"),
                }, "x", "y", "direction"),
            Tool("wait", "Wait for the screen to update (page loading, app starting). seconds: 0.5-10.",
                new JsonObject { ["seconds"] = Num("seconds") }, "seconds"));
    }

    // ---------------- 도구 실행 ----------------

    private async Task<JsonObject> CallAsync(string name, JsonObject a)
    {
        // 마지막 안전장치: 승인되지 않았거나 긴급 정지된 상태면 아무것도 하지 않음
        if (!IsAllowed())
            return Error("화면 조작이 허용되지 않은 상태야 (사용자가 거부했거나 멈춤). 다시 시도하지 마.");

        try
        {
            switch (name)
            {
                case "list_monitors":
                {
                    var mons = ScreenInput.Monitors();
                    var c = ScreenInput.CursorPhysical();
                    var sb = new System.Text.StringBuilder();
                    foreach (var m in mons)
                    {
                        sb.Append($"monitor {m.Index}: {m.Width}x{m.Height} px at ({m.Left},{m.Top})");
                        if (m.Primary) sb.Append(" [primary]");
                        if (c.Ok && m.Contains(c.X, c.Y)) sb.Append(" [mouse cursor here]");
                        sb.Append('\n');
                    }
                    return Ok(sb.ToString().TrimEnd());
                }
                case "screenshot":
                {
                    var mons = ScreenInput.Monitors();
                    MonitorRect target;
                    double req = Num(a, "monitor");
                    if (!double.IsNaN(req))
                    {
                        target = mons.FirstOrDefault(m => m.Index == (int)req);
                        if (target == null) return Error($"그런 모니터가 없어: {req}. 모니터는 1~{mons.Count}번이야 (list_monitors 참고).");
                    }
                    else target = mons.FirstOrDefault(m => _mon != null && m.Left == _mon.Left && m.Top == _mon.Top) ?? mons.First();

                    int sw = 0, sh = 0;
                    byte[] jpg = await Task.Run(() => ScreenInput.CaptureJpeg(target, ShotMaxWidth, ShotQuality, out sw, out sh));
                    if (jpg == null) return Error("화면을 캡처하지 못했어 (잠금 화면이나 관리자 권한 창일 수 있음).");
                    // 캡처하는 사이에 사용자가 멈췄으면 화면을 보내지 않음
                    if (!IsAllowed()) return Error("사용자가 멈췄어.");
                    (_mon, _shotW, _shotH) = (target, sw, sh);
                    Report(L.T("화면 보기", "Looking at the screen") + (mons.Count > 1 ? L.T($" · 모니터 {target.Index}", $" · monitor {target.Index}") : ""));
                    string about = $"screenshot of monitor {target.Index}{(target.Primary ? " (primary)" : "")} of {mons.Count}: {_shotW}x{_shotH} px. " +
                                   "click/move/drag/scroll coordinates are pixels in this image and act on this monitor.";
                    return new JsonObject
                    {
                        ["content"] = new JsonArray(
                            new JsonObject { ["type"] = "image", ["data"] = Convert.ToBase64String(jpg), ["mimeType"] = "image/jpeg" },
                            new JsonObject { ["type"] = "text", ["text"] = about }),
                    };
                }
                case "click":
                {
                    var (nx, ny) = ToPhysical(a, "x", "y");
                    string button = Str(a["button"]) ?? "left";
                    int b = button == "right" ? 2 : button == "middle" ? 1 : 0;
                    bool dbl = a["double"]?.GetValueKind() == System.Text.Json.JsonValueKind.True;
                    Report($"{(dbl ? L.T("더블클릭", "Double-click") : button == "right" ? L.T("오른쪽 클릭", "Right-click") : L.T("클릭", "Click"))} ({Num(a, "x"):0}, {Num(a, "y"):0})");
                    if (await GuardAsync(nx, ny) is JsonObject stopC) return stopC;
                    bool sent = ScreenInput.MoveTo(nx, ny);
                    _lastSet = (nx, ny);
                    await Task.Delay(40);
                    for (int i = 0; i < (dbl ? 2 : 1); i++)
                    {
                        if (i > 0 && Interrupted()) return !IsAllowed() ? Error("사용자가 멈췄어.") : TookOver();
                        sent &= ScreenInput.MouseButton(b, true);
                        await Task.Delay(30);
                        sent &= ScreenInput.MouseButton(b, false);   // 누른 버튼은 무슨 일이 있어도 뗌
                        if (dbl) await Task.Delay(60);
                    }
                    if (!sent) return Blocked();
                    await Task.Delay(250);   // 화면이 반응할 시간
                    return Ok($"clicked {button}{(dbl ? " (double)" : "")} at ({Num(a, "x"):0}, {Num(a, "y"):0})");
                }
                case "move":
                {
                    var (nx, ny) = ToPhysical(a, "x", "y");
                    Report(L.T("마우스 이동", "Move mouse") + $" ({Num(a, "x"):0}, {Num(a, "y"):0})");
                    if (await GuardAsync() is JsonObject stopM) return stopM;
                    if (!ScreenInput.MoveTo(nx, ny)) return Blocked();
                    _lastSet = (nx, ny);
                    await Task.Delay(200);
                    return Ok("moved");
                }
                case "drag":
                {
                    var (x1, y1) = ToPhysical(a, "x1", "y1");
                    var (x2, y2) = ToPhysical(a, "x2", "y2");
                    Report(L.T("끌기", "Drag") + $" ({Num(a, "x1"):0}, {Num(a, "y1"):0}) → ({Num(a, "x2"):0}, {Num(a, "y2"):0})");
                    if (await GuardAsync(x1, y1) is JsonObject stopD) return stopD;
                    ScreenInput.MoveTo(x1, y1);
                    await Task.Delay(50);
                    ScreenInput.MouseButton(0, true);
                    try
                    {
                        // 앱이 끌기로 인식하도록 여러 단계로 나눠 움직임
                        _lastSet = (x1, y1);
                        for (int i = 1; i <= 15; i++)
                        {
                            if (Interrupted()) break;   // 멈춤·가로채기: 바로 그만두고 아래 finally에서 버튼을 뗌
                            int px = x1 + (x2 - x1) * i / 15, py = y1 + (y2 - y1) * i / 15;
                            ScreenInput.MoveTo(px, py);
                            _lastSet = (px, py);
                            await Task.Delay(15);
                        }
                    }
                    finally { ScreenInput.MouseButton(0, false); }   // 무슨 일이 있어도 버튼은 뗌
                    if (!IsAllowed()) return Error("사용자가 멈췄어.");
                    if (UserMovedSinceLastAction()) return TookOver();
                    await Task.Delay(250);
                    return Ok("dragged");
                }
                case "type":
                {
                    string text = Str(a["text"]) ?? "";
                    if (text.Length > 2000) return Error("한 번에 2000자까지만 입력할 수 있어. 나눠서 입력해.");
                    Report(L.T($"글자 입력 ({text.Length}자)", $"Typing ({text.Length} chars)"));
                    if (await GuardAsync() is JsonObject stopT) return stopT;
                    // 긴 글은 나눠서 넣어 앱이 따라올 수 있게. 이모지 같은 두 칸짜리 글자는 반으로 자르지 않음
                    for (int i = 0; i < text.Length;)
                    {
                        if (!IsAllowed()) return Error("사용자가 멈췄어.");
                        int len = Math.Min(200, text.Length - i);
                        if (i + len < text.Length && char.IsHighSurrogate(text[i + len - 1])) len--;
                        if (!ScreenInput.Text(text.Substring(i, len))) return Blocked();
                        i += len;
                        await Task.Delay(60);
                    }
                    await Task.Delay(150);
                    return Ok($"typed {text.Length} chars");
                }
                case "key":
                {
                    string keys = Str(a["keys"]) ?? "";
                    var codes = ParseKeys(keys);
                    if (codes == null) return Error($"모르는 키 이름: {keys}. 예: enter, esc, tab, ctrl+c, alt+tab, win+r, f5");
                    Report(L.T("키 ", "Key ") + keys);
                    if (await GuardAsync() is JsonObject stopK) return stopK;
                    if (!ScreenInput.Combo(codes)) return Blocked();
                    await Task.Delay(250);
                    return Ok($"pressed {keys}");
                }
                case "scroll":
                {
                    var (nx, ny) = ToPhysical(a, "x", "y");
                    double rawAmount = Num(a, "amount");
                    int amount = double.IsNaN(rawAmount) ? 3 : (int)Math.Clamp(rawAmount, 1, 15);   // 없거나 숫자가 아니면 3칸
                    bool up = Str(a["direction"]) == "up";
                    Report(L.T($"스크롤 {(up ? "위" : "아래")} {amount}칸", $"Scroll {(up ? "up" : "down")} {amount}"));
                    if (await GuardAsync(nx, ny) is JsonObject stopS) return stopS;
                    ScreenInput.MoveTo(nx, ny);
                    _lastSet = (nx, ny);
                    await Task.Delay(40);
                    for (int i = 0; i < amount; i++)
                    {
                        if (Interrupted()) return !IsAllowed() ? Error("사용자가 멈췄어.") : TookOver();
                        if (!ScreenInput.MouseWheel(up ? 120 : -120)) return Blocked();
                        await Task.Delay(30);
                    }
                    await Task.Delay(250);
                    return Ok($"scrolled {(up ? "up" : "down")} {amount}");
                }
                case "wait":
                {
                    double raw = Num(a, "seconds");
                    double s = double.IsNaN(raw) ? 1 : Math.Clamp(raw, 0.5, 10);
                    Report(L.T($"기다리는 중 {s:0.#}초", $"Waiting {s:0.#}s"));
                    // 0.1초마다 멈춤 여부 확인 (긴급 정지가 바로 먹히게)
                    for (double t = 0; t < s; t += 0.1)
                    {
                        if (!IsAllowed()) return Error("사용자가 멈췄어.");
                        await Task.Delay(100);
                    }
                    return Ok($"waited {s:0.#}s");
                }
                default:
                    return Error($"모르는 도구: {name}");
            }
        }
        catch (Exception ex)
        {
            AppSettings.Log($"[화면 조작 오류] {name}: {ex.Message}");
            return Error($"{name} 실패: {ex.Message}");
        }
    }

    // ---------------- 도우미 ----------------

    private void Report(string action)
    {
        AppSettings.Log($"[화면 조작] {action}");
        OnAction?.Invoke(action);
    }

    // 스크린샷 픽셀 좌표 → 그 스크린샷을 찍은 모니터의 실제 픽셀 (모든 모니터 공통 좌표)
    // 스크린샷을 아직 안 찍었으면 주 모니터 기준으로 같은 규칙의 크기를 씀
    private (int, int) ToPhysical(JsonObject a, string xKey, string yKey)
    {
        if (_mon == null || _shotW <= 0 || _shotH <= 0)
        {
            _mon = ScreenInput.Monitors().First();
            (_shotW, _shotH) = ScreenInput.ShotSize(_mon, ShotMaxWidth);
        }
        double x = Num(a, xKey), y = Num(a, yKey);
        if (double.IsNaN(x) || double.IsNaN(y)) throw new ArgumentException($"{xKey}/{yKey} 좌표가 필요해");
        int px = _mon.Left + (int)Math.Round((Math.Clamp(x, 0, _shotW - 1) + 0.5) * _mon.Width / _shotW);
        int py = _mon.Top + (int)Math.Round((Math.Clamp(y, 0, _shotH - 1) + 0.5) * _mon.Height / _shotH);
        return (Math.Clamp(px, _mon.Left, _mon.Left + _mon.Width - 1), Math.Clamp(py, _mon.Top, _mon.Top + _mon.Height - 1));
    }

    // "ctrl+shift+t" → 브라우저 키 코드 목록 (ScreenInput의 키 표와 같은 이름)
    private static List<string> ParseKeys(string keys)
    {
        var list = new List<string>();
        // "ctrl++"처럼 빈 칸이 생기면 무엇을 누를지 모호하므로 오류로 돌려보냄 (몰래 Ctrl만 누르지 않게)
        foreach (string raw in keys.ToLowerInvariant().Split('+', StringSplitOptions.TrimEntries))
        {
            if (raw.Length == 0) return null;
            string code = raw switch
            {
                "ctrl" or "control" => "ControlLeft",
                "alt" => "AltLeft",
                "shift" => "ShiftLeft",
                "win" or "windows" or "meta" or "cmd" or "super" => "MetaLeft",
                "enter" or "return" => "Enter",
                "esc" or "escape" => "Escape",
                "tab" => "Tab",
                "space" => "Space",
                "backspace" => "Backspace",
                "delete" or "del" => "Delete",
                "insert" => "Insert",
                "home" => "Home",
                "end" => "End",
                "pageup" or "pgup" => "PageUp",
                "pagedown" or "pgdn" => "PageDown",
                "up" => "ArrowUp",
                "down" => "ArrowDown",
                "left" => "ArrowLeft",
                "right" => "ArrowRight",
                "hangul" or "한영" => "Lang1",
                "printscreen" => "PrintScreen",
                "-" or "minus" => "Minus",
                "=" or "equal" or "plus" => "Equal",
                "," or "comma" => "Comma",
                "." or "period" => "Period",
                "/" or "slash" => "Slash",
                _ when raw.Length == 1 && raw[0] is >= 'a' and <= 'z' => "Key" + char.ToUpperInvariant(raw[0]),
                _ when raw.Length == 1 && raw[0] is >= '0' and <= '9' => "Digit" + raw,
                _ when raw.Length >= 2 && raw[0] == 'f' && int.TryParse(raw[1..], out int f) && f is >= 1 and <= 24 => "F" + f,
                _ => null,
            };
            if (code == null) return null;
            list.Add(code);
        }
        return list.Count is > 0 and <= 4 ? list : null;
    }

    // Windows가 입력을 막았을 때 (관리자 권한 창·잠금 화면이 앞에 있음)
    private static JsonObject Blocked() =>
        Error("Windows가 입력을 막았어. 관리자 권한 창(UAC 등)이나 잠금 화면이 앞에 있을 수 있어. 사용자에게 직접 해 달라고 해.");

    private static JsonObject Ok(string text) => new()
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
    };

    private static JsonObject Error(string text) => new()
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = text }),
        ["isError"] = true,
    };

    private static double Num(JsonObject a, string key) =>
        a[key] is JsonValue v && v.TryGetValue(out double d) ? d : double.NaN;

    private static string Str(JsonNode n) => n is JsonValue v && v.TryGetValue(out string s) ? s : null;
}
