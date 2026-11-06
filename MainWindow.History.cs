// v1.1 기능: 대화 기록 저장·복원
// - 답이 끝날 때마다 %LOCALAPPDATA%\ClaudeDesk\history.json에 저장 → GAFIL을 다시 켜면 말풍선에 그대로 보이고,
//   Claude 세션 ID도 저장해서 앞 대화 맥락까지 이어감 (--resume)
// - "새 대화 시작"을 누르면 파일도 비움. settings.json의 "SaveHistory": false면 저장하지 않고 파일을 지움
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ClaudeDesk;

public partial class MainWindow
{
    private static readonly string HistoryPath = Path.Combine(AppSettings.Dir, "history.json");
    private const int MaxSavedItems = 200;   // 파일이 끝없이 커지지 않게 최근 200줄만

    private sealed class HistoryFile
    {
        public string SessionId { get; set; }
        public List<HistoryItem> Items { get; set; } = new();
    }

    private sealed class HistoryItem
    {
        public bool User { get; set; }
        public string Text { get; set; }
        public DateTime At { get; set; }
    }

    private static readonly JsonSerializerOptions HistoryJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    // 시작할 때 지난 대화를 말풍선에 다시 그림. 복원했으면 true (그러면 첫 인사는 생략)
    private bool RestoreHistory()
    {
        // 지난 대화를 이어가지 못하면(--resume 실패) 말풍선에 한 줄 안내 (기록 복원 여부와 상관없이)
        _claude.ResumeFailed += () =>
            AddActivityLine(L.T("지난 대화의 맥락은 이어가지 못해서 새로 시작해 (위 기록은 화면에만 남아 있어)",
                "Couldn't continue the previous conversation, so starting fresh (the lines above are display only)"));

        if (!_settings.SaveHistory)
        {
            TryDelete(HistoryPath);
            return false;
        }
        try
        {
            if (!File.Exists(HistoryPath)) return false;
            var h = JsonSerializer.Deserialize<HistoryFile>(File.ReadAllText(HistoryPath), HistoryJson);
            if (h?.Items == null || h.Items.Count == 0) return false;

            foreach (var it in h.Items)
            {
                if (string.IsNullOrEmpty(it.Text)) continue;
                if (it.User) AddUserLine(it.Text); else AddCatLine(it.Text);
                _history.Add((it.User, it.Text));
            }
            if (!string.IsNullOrEmpty(h.SessionId)) _claude.RestoreSession(h.SessionId);
            AppSettings.Log($"[대화 기록] {h.Items.Count}줄 복원");
            return true;
        }
        catch (Exception ex)
        {
            // 깨진 기록 파일 때문에 GAFIL이 안 켜지면 안 됨 → 무시하고 새로 시작
            AppSettings.Log($"[대화 기록] 복원 실패, 새로 시작: {ex.Message}");
            return false;
        }
    }

    // 답이 끝날 때마다 저장 (최근 MaxSavedItems줄)
    private void SaveHistory()
    {
        if (!_settings.SaveHistory) return;
        try
        {
            var h = new HistoryFile { SessionId = _claude.SessionId };
            foreach (var (isUser, text) in _history.TakeLast(MaxSavedItems))
                h.Items.Add(new HistoryItem { User = isUser, Text = text, At = DateTime.Now });
            Directory.CreateDirectory(AppSettings.Dir);
            // 임시 파일에 먼저 쓰고 바꿔치기: 저장 중에 꺼져도 기록 파일이 반쯤 깨지지 않게
            string tmp = HistoryPath + ".tmp";
            File.WriteAllText(tmp, JsonSerializer.Serialize(h, HistoryJson), new UTF8Encoding(false));
            File.Move(tmp, HistoryPath, overwrite: true);
        }
        catch (Exception ex)
        {
            AppSettings.Log($"[대화 기록] 저장 실패: {ex.Message}");
        }
    }

    private void ClearHistoryFile() => TryDelete(HistoryPath);

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
