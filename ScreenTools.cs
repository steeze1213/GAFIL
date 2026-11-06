// 5단계 GAFIL 화면 조작 도구: Claude CLI에 GAFIL 안에서 도는 MCP 서버(in-process, "sdk" 방식)로 제공
// CLI가 control_request(mcp_message)로 JSON-RPC 메시지를 보내면 여기서 처리해 결과를 돌려줌
// 도구: screenshot, click, drag, move, type, key, scroll, wait
// 좌표는 '마지막 스크린샷 이미지'의 픽셀 기준 → 실제 화면 비율(0~1)로 바꿔서 입력
using System.Text.Json.Nodes;

namespace ClaudeDesk;

public sealed class ScreenTools
{
    public const string ServerName = "gafil";
    private const int ShotMaxWidth = 1280;   // Claude가 좌표를 잘 읽는 크기 (2560x1600 화면 → 1280x800)
    private const long ShotQuality = 70;

    // 마지막 스크린샷 크기 (클릭 좌표를 화면 비율로 바꿀 때 사용)
    private int _shotW, _shotH;

    // 동작 하나마다 알림 (빨간 띠 안내·로그). 문자열 예: "클릭 (640, 400)"
    public Action<string> OnAction { get; set; }

    // 지금 화면 조작이 허용된 상태인지 (승인·긴급 정지는 ClaudeClient가 관리). false면 실행하지 않음
    public Func<bool> IsAllowed { get; set; } = () => false;

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
            Tool("screenshot", "Capture the user's primary monitor. Returns a JPEG image. All x/y coordinates for other tools are pixels in THIS image (origin top-left). Take a new screenshot after actions to verify the result.",
                new JsonObject()),
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
                case "screenshot":
                {
                    int sw = 0, sh = 0;
                    byte[] jpg = await Task.Run(() => ScreenInput.CaptureJpeg(ShotMaxWidth, ShotQuality, out sw, out sh));
                    if (jpg == null) return Error("화면을 캡처하지 못했어 (잠금 화면이나 관리자 권한 창일 수 있음).");
                    // 캡처하는 사이에 사용자가 멈췄으면 화면을 보내지 않음
                    if (!IsAllowed()) return Error("사용자가 멈췄어.");
                    (_shotW, _shotH) = (sw, sh);
                    Report("화면 보기");
                    return new JsonObject
                    {
                        ["content"] = new JsonArray(
                            new JsonObject { ["type"] = "image", ["data"] = Convert.ToBase64String(jpg), ["mimeType"] = "image/jpeg" },
                            new JsonObject { ["type"] = "text", ["text"] = $"screenshot {_shotW}x{_shotH} px (primary monitor). Use these pixel coordinates." }),
                    };
                }
                case "click":
                {
                    var (nx, ny) = Norm(a, "x", "y");
                    string button = Str(a["button"]) ?? "left";
                    int b = button == "right" ? 2 : button == "middle" ? 1 : 0;
                    bool dbl = a["double"]?.GetValueKind() == System.Text.Json.JsonValueKind.True;
                    Report($"{(dbl ? "더블클릭" : button == "right" ? "오른쪽 클릭" : "클릭")} ({Num(a, "x"):0}, {Num(a, "y"):0})");
                    bool sent = ScreenInput.MouseMove(nx, ny);
                    await Task.Delay(40);
                    for (int i = 0; i < (dbl ? 2 : 1); i++)
                    {
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
                    var (nx, ny) = Norm(a, "x", "y");
                    Report($"마우스 이동 ({Num(a, "x"):0}, {Num(a, "y"):0})");
                    if (!ScreenInput.MouseMove(nx, ny)) return Blocked();
                    await Task.Delay(200);
                    return Ok("moved");
                }
                case "drag":
                {
                    var (x1, y1) = Norm(a, "x1", "y1");
                    var (x2, y2) = Norm(a, "x2", "y2");
                    Report($"끌기 ({Num(a, "x1"):0}, {Num(a, "y1"):0}) → ({Num(a, "x2"):0}, {Num(a, "y2"):0})");
                    ScreenInput.MouseMove(x1, y1);
                    await Task.Delay(50);
                    ScreenInput.MouseButton(0, true);
                    try
                    {
                        // 앱이 끌기로 인식하도록 여러 단계로 나눠 움직임
                        for (int i = 1; i <= 15; i++)
                        {
                            ScreenInput.MouseMove(x1 + (x2 - x1) * i / 15, y1 + (y2 - y1) * i / 15);
                            await Task.Delay(15);
                        }
                    }
                    finally { ScreenInput.MouseButton(0, false); }   // 무슨 일이 있어도 버튼은 뗌
                    await Task.Delay(250);
                    return Ok("dragged");
                }
                case "type":
                {
                    string text = Str(a["text"]) ?? "";
                    if (text.Length > 2000) return Error("한 번에 2000자까지만 입력할 수 있어. 나눠서 입력해.");
                    Report($"글자 입력 ({text.Length}자)");
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
                    Report($"키 {keys}");
                    if (!ScreenInput.Combo(codes)) return Blocked();
                    await Task.Delay(250);
                    return Ok($"pressed {keys}");
                }
                case "scroll":
                {
                    var (nx, ny) = Norm(a, "x", "y");
                    double rawAmount = Num(a, "amount");
                    int amount = double.IsNaN(rawAmount) ? 3 : (int)Math.Clamp(rawAmount, 1, 15);   // 없거나 숫자가 아니면 3칸
                    bool up = Str(a["direction"]) == "up";
                    Report($"스크롤 {(up ? "위" : "아래")} {amount}칸");
                    ScreenInput.MouseMove(nx, ny);
                    await Task.Delay(40);
                    for (int i = 0; i < amount; i++)
                    {
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
                    Report($"기다리는 중 {s:0.#}초");
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

    // 스크린샷 픽셀 좌표 → 화면 비율(0~1). 스크린샷을 아직 안 찍었으면 같은 규칙으로 크기를 계산
    private (double, double) Norm(JsonObject a, string xKey, string yKey)
    {
        if (_shotW <= 0 || _shotH <= 0) (_shotW, _shotH) = ScreenInput.ShotSize(ShotMaxWidth);
        double x = Num(a, xKey), y = Num(a, yKey);
        if (double.IsNaN(x) || double.IsNaN(y)) throw new ArgumentException($"{xKey}/{yKey} 좌표가 필요해");
        return (Math.Clamp((x + 0.5) / _shotW, 0, 1), Math.Clamp((y + 0.5) / _shotH, 0, 1));
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
