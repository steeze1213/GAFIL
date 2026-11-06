// 설정 파일, 자동 실행(autostart), 명령 로그를 한곳에서 관리
// 설정 파일 위치: %LOCALAPPDATA%\ClaudeDesk\settings.json (메모장으로 직접 고칠 수 있음)
using System.IO;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using Microsoft.Win32;

namespace ClaudeDesk;

public sealed class AppSettings
{
    // ---- 저장되는 값 ----

    // 4단계: 승인 없이 읽기를 허용할 폴더 목록 (기본은 빈 목록 → 앱 폴더 밖 읽기는 모두 승인 창)
    public List<string> AllowedFolders { get; set; } = new();

    // 3단계: 대화하지 않을 때 CPU/GPU 표시
    public bool ShowStats { get; set; } = true;

    // 윈도우 로그인 시 자동 실행
    public bool AutoStart { get; set; } = true;

    // 사용할 모델. 비우면 Claude Code 기본 모델을 따름 (예: "opus", "sonnet", "haiku")
    public string Model { get; set; } = "";

    // 화면 문구 언어: "auto"(Windows 표시 언어를 따름), "ko", "en"
    public string Language { get; set; } = "auto";

    // 어디서든 GAFIL을 부르는 단축키. 빈 값이면 끔 (예: "Ctrl+Alt+G", "Ctrl+Shift+Space")
    public string OpenHotkey { get; set; } = "Ctrl+Alt+G";

    // 고양이 모습: 털 색("orange", "black", "white", "gray", "cream"), 옷·소품("none", "crown", "sunglasses", "scarf", "bowtie")
    public string Skin { get; set; } = "orange";
    public string Outfit { get; set; } = "none";

    // 화면 조작 때 클릭 전에 그 자리를 표시하고 기다리는 시간(ms). 이 사이에 마우스를 움직이면 멈춤. 0이면 예고 없음
    public int ScreenPreviewMs { get; set; } = 800;

    // 새 버전 확인 (GitHub Releases, 하루 한 번)
    public bool CheckUpdates { get; set; } = true;
    // 이미 알려 준 새 버전 (같은 버전은 한 번만 알림)
    public string NotifiedVersion { get; set; }

    // 대화 기록을 history.json에 저장해서 다시 켜도 이어가기
    public bool SaveHistory { get; set; } = true;

    // 고양이를 마지막으로 놓은 화면 좌표(실제 픽셀). 다시 켜면 그 모니터에서 시작. 없으면 주 모니터
    public int? CatScreenX { get; set; }
    public int? CatScreenY { get; set; }

    // ---- 경로 ----
    public static readonly string Dir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "ClaudeDesk");
    public static readonly string FilePath = Path.Combine(Dir, "settings.json");
    public static readonly string LogPath = Path.Combine(Dir, "logs", "commands.log");

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,   // 한글이 \uXXXX로 바뀌지 않게
    };

    // 설정 읽기: 파일이 없거나 깨졌으면 기본값으로 새로 만듦
    public static AppSettings Load()
    {
        Directory.CreateDirectory(Dir);
        try
        {
            if (File.Exists(FilePath))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(FilePath), JsonOpts) ?? new AppSettings();
        }
        catch (Exception ex)
        {
            Log($"[설정] settings.json을 읽지 못해 기본값 사용: {ex.Message}");
            return new AppSettings();   // 깨진 파일은 덮어쓰지 않음 (사용자가 고치던 중일 수 있음)
        }

        var created = new AppSettings();
        created.Save();
        return created;
    }

    public void Save()
    {
        Directory.CreateDirectory(Dir);
        File.WriteAllText(FilePath, JsonSerializer.Serialize(this, JsonOpts), new UTF8Encoding(false));
    }

    // 경로가 허용 폴더 안에 있는지 확인. 상대 경로는 CLI와 같은 기준 폴더(baseDir)로 풂
    public bool IsInAllowedFolder(string path, string baseDir)
    {
        foreach (string folder in AllowedFolders)
            if (!string.IsNullOrWhiteSpace(folder) && IsUnder(path, folder, baseDir)) return true;
        return false;
    }

    // path가 root 폴더 안(또는 root 자체)인지. ".."로 빠져나가는 경로도 정규화해서 막음
    // UNC 경로(\\서버\...)와 장치 경로(\\?\...)는 항상 밖으로 봄
    public static bool IsUnder(string path, string root, string baseDir)
    {
        if (string.IsNullOrWhiteSpace(path) || string.IsNullOrWhiteSpace(root)) return false;
        try
        {
            if (path.StartsWith(@"\\") || path.StartsWith("//")) return false;
            string full = Path.GetFullPath(path, baseDir).TrimEnd('\\', '/');
            string r = Path.GetFullPath(root, baseDir).TrimEnd('\\', '/');
            return full.Equals(r, StringComparison.OrdinalIgnoreCase)
                || full.StartsWith(r + "\\", StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    // ---- 자동 실행: 현재 사용자 레지스트리 Run 키에 등록 (관리자 권한 불필요) ----
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string RunName = "GAFIL";

    // 설치 위치: 자동 실행은 이 위치의 exe만 등록함 (개발 중 빌드한 exe가 자동 실행을 가로채지 않게)
    public static readonly string InstallDir =
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "GAFIL");

    public static bool IsInstalledCopy =>
        Environment.ProcessPath is string p && IsUnder(p, InstallDir, InstallDir);

    // 설정값에 맞게 Run 키를 등록하거나 지움. 설치 위치에서 실행 중일 때만 손댐
    public void ApplyAutoStart()
    {
        if (!IsInstalledCopy) return;
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey, writable: true);
            if (key == null) return;
            if (AutoStart)
            {
                string exe = Environment.ProcessPath;
                string value = $"\"{exe}\"";
                if (!Equals(key.GetValue(RunName), value)) key.SetValue(RunName, value);
            }
            else if (key.GetValue(RunName) != null)
            {
                key.DeleteValue(RunName);
            }
        }
        catch (Exception ex)
        {
            Log($"[자동 실행] 레지스트리 등록 실패: {ex.Message}");
        }
    }

    // ---- 명령 로그: 승인 요청, 결정, 실행 결과를 한 줄씩 남김 ----
    private static readonly object LogLock = new();

    public static void Log(string line)
    {
        try
        {
            lock (LogLock)
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                File.AppendAllText(LogPath, $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {line}{Environment.NewLine}", new UTF8Encoding(false));
            }
        }
        catch { }   // 로그 실패로 앱이 멈추면 안 됨
    }
}
