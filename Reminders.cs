// v1.1 기능: 알림(리마인더)
// "30분 뒤에 회의 알려줘" → Claude가 gafil_time 도구(set_reminder)를 부름 → 시간이 되면 고양이가 화면 가운데로 뛰어와 말풍선으로 알려줌
// - Claude CLI에 GAFIL 안에서 도는 두 번째 MCP 서버("gafil_time")로 제공 (화면 조작 도구와 따로: 승인 없이 써도 되는 무해한 기능)
// - 알림은 %LOCALAPPDATA%\ClaudeDesk\reminders.json에 저장 → GAFIL을 껐다 켜도 남아 있고, 꺼진 사이 지난 알림은 켤 때 알려줌
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace ClaudeDesk;

public sealed class Reminder
{
    public string Id { get; set; }
    public DateTime Due { get; set; }       // 현지 시각
    public string Message { get; set; }
}

public sealed class ReminderStore
{
    public const string ServerName = "gafil_time";
    private const int MaxReminders = 50;
    private static readonly string FilePath = Path.Combine(AppSettings.Dir, "reminders.json");
    private static readonly JsonSerializerOptions Json = new() { WriteIndented = true, Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

    private readonly List<Reminder> _items = new();
    private readonly object _lock = new();

    public ReminderStore()
    {
        try
        {
            if (File.Exists(FilePath))
                _items.AddRange(JsonSerializer.Deserialize<List<Reminder>>(File.ReadAllText(FilePath), Json) ?? new());
        }
        catch (Exception ex)
        {
            // 깨진 파일을 빈 목록으로 덮어쓰지 않도록 옆에 보관해 둠
            AppSettings.Log($"[알림] reminders.json을 읽지 못함 (reminders.bad.json으로 보관): {ex.Message}");
            try { File.Copy(FilePath, Path.Combine(AppSettings.Dir, "reminders.bad.json"), overwrite: true); } catch { }
        }
    }

    // 때가 된 알림을 돌려줌 (지우지는 않음: 화면에 보여준 뒤 Remove로 지움 → 그 사이 꺼져도 잃어버리지 않게)
    public List<Reminder> PeekDue()
    {
        lock (_lock) return _items.Where(r => r.Due <= DateTime.Now).OrderBy(r => r.Due).ToList();
    }

    public void Remove(IEnumerable<string> ids)
    {
        lock (_lock)
        {
            var set = ids.ToHashSet();
            if (_items.RemoveAll(r => set.Contains(r.Id)) > 0) Save();
        }
    }

    // 테스트·호환용: 때가 된 알림을 꺼내면서 지움
    public List<Reminder> TakeDue()
    {
        lock (_lock)
        {
            var due = PeekDue();
            Remove(due.Select(r => r.Id));
            return due;
        }
    }

    private void Save()
    {
        try
        {
            Directory.CreateDirectory(AppSettings.Dir);
            // 임시 파일에 쓰고 바꿔치기: 저장 중에 꺼져도 알림 파일이 반쯤 깨지지 않게
            string tmp = FilePath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(_items, Json), new UTF8Encoding(false));
            File.Move(tmp, FilePath, overwrite: true);
        }
        catch (Exception ex) { AppSettings.Log($"[알림] 저장 실패: {ex.Message}"); }
    }

    // ---------------- MCP(JSON-RPC) 처리 ----------------

    public JsonObject Handle(JsonObject msg)
    {
        string method = Str(msg["method"]);
        JsonNode result = method switch
        {
            "initialize" => new JsonObject
            {
                ["protocolVersion"] = Str(msg["params"]?["protocolVersion"]) ?? "2024-11-05",
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject() },
                ["serverInfo"] = new JsonObject { ["name"] = ServerName, ["version"] = "1.0" },
            },
            "tools/list" => new JsonObject { ["tools"] = Tools() },
            "tools/call" => Call(Str(msg["params"]?["name"]), msg["params"]?["arguments"] as JsonObject ?? new JsonObject()),
            _ => new JsonObject(),
        };
        return new JsonObject { ["jsonrpc"] = "2.0", ["id"] = msg["id"]?.DeepClone(), ["result"] = result };
    }

    private static JsonArray Tools()
    {
        JsonObject T(string name, string desc, JsonObject props, params string[] req) => new()
        {
            ["name"] = name,
            ["description"] = desc,
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = props,
                ["required"] = new JsonArray(req.Select(r => (JsonNode)r).ToArray()),
            },
        };
        return new JsonArray(
            T("set_reminder",
                "Set a reminder. When it is due, GAFIL (the desktop cat) runs to the middle of the screen and shows the message. " +
                "Give either in_minutes (relative) or at_time (\"HH:mm\", 24h, local time). For a later day add days_from_today. " +
                $"Current local time: see the result of list_reminders. Keep message short, in the user's language.",
                new JsonObject
                {
                    ["message"] = new JsonObject { ["type"] = "string" },
                    ["in_minutes"] = new JsonObject { ["type"] = "number" },
                    ["at_time"] = new JsonObject { ["type"] = "string", ["description"] = "HH:mm (24h local)" },
                    ["days_from_today"] = new JsonObject { ["type"] = "integer", ["description"] = "0 = today (default), 1 = tomorrow" },
                }, "message"),
            T("list_reminders", "List pending reminders and the current local date/time.", new JsonObject()),
            T("cancel_reminder", "Cancel a reminder by id (from list_reminders).",
                new JsonObject { ["id"] = new JsonObject { ["type"] = "string" } }, "id"));
    }

    private JsonObject Call(string name, JsonObject a)
    {
        lock (_lock)
        {
            switch (name)
            {
                case "set_reminder":
                {
                    string message = (Str(a["message"]) ?? "").Trim();
                    if (message.Length == 0) return Error("message가 비어 있어.");
                    if (message.Length > 300) message = message[..300];
                    if (_items.Count >= MaxReminders) return Error($"알림은 {MaxReminders}개까지만 둘 수 있어. 먼저 몇 개 지워.");

                    DateTime due;
                    if (Num(a["in_minutes"]) is double min)
                    {
                        if (min < 0.1 || min > 60 * 24 * 60) return Error("in_minutes는 0.1분 ~ 60일 사이여야 해.");
                        due = DateTime.Now.AddMinutes(min);
                    }
                    else if (Str(a["at_time"]) is string at && TimeSpan.TryParse(at, out var tod) && tod >= TimeSpan.Zero && tod < TimeSpan.FromDays(1))
                    {
                        int days = (int)(Num(a["days_from_today"]) ?? 0);
                        if (days < 0 || days > 365) return Error("days_from_today는 0~365.");
                        due = DateTime.Today.AddDays(days) + tod;
                        if (days == 0 && due <= DateTime.Now) due = due.AddDays(1);   // 오늘 이미 지난 시각이면 내일
                    }
                    else return Error("in_minutes 또는 at_time(\"HH:mm\") 중 하나가 필요해.");

                    var r = new Reminder { Id = Guid.NewGuid().ToString("N")[..6], Due = due, Message = message };
                    _items.Add(r);
                    Save();
                    AppSettings.Log($"[알림] 설정: {due:yyyy-MM-dd HH:mm} — {message}");
                    return Ok($"set reminder {r.Id} for {due:yyyy-MM-dd (ddd) HH:mm} (now {DateTime.Now:yyyy-MM-dd HH:mm}): {message}");
                }
                case "list_reminders":
                {
                    var sb = new StringBuilder($"now: {DateTime.Now:yyyy-MM-dd (ddd) HH:mm}\n");
                    if (_items.Count == 0) sb.Append("no reminders");
                    foreach (var r in _items.OrderBy(r => r.Due)) sb.Append($"- {r.Id}: {r.Due:yyyy-MM-dd (ddd) HH:mm} — {r.Message}\n");
                    return Ok(sb.ToString());
                }
                case "cancel_reminder":
                {
                    string id = Str(a["id"]);
                    int n = _items.RemoveAll(r => r.Id == id);
                    if (n == 0) return Error($"그런 알림이 없어: {id}");
                    Save();
                    AppSettings.Log($"[알림] 취소: {id}");
                    return Ok($"cancelled {id}");
                }
                default:
                    return Error($"모르는 도구: {name}");
            }
        }
    }

    private static JsonObject Ok(string t) => new() { ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = t }) };
    private static JsonObject Error(string t) => new()
    {
        ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = t }),
        ["isError"] = true,
    };
    private static string Str(JsonNode n) => n is JsonValue v && v.TryGetValue(out string s) ? s : null;
    private static double? Num(JsonNode n) => n is JsonValue v && v.TryGetValue(out double d) && !double.IsNaN(d) ? d : null;
}
