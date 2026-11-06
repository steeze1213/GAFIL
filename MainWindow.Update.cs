// v1.1 기능: 새 버전 알림
// 켜고 10초 뒤, 그리고 하루에 한 번 GitHub Releases의 최신 버전을 확인 → 지금보다 새 버전이면
// 고양이 위에 "⬆ 새 버전" 표시(버전마다 한 번, 1분 동안)와 오른쪽 클릭 메뉴 맨 위에 "새 버전 받기" 항목을 보여줌
// 보내는 정보: GitHub 공개 API에 익명 요청 1번 (개인정보 없음). settings.json의 "CheckUpdates": false로 끌 수 있음
using System.Diagnostics;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;

namespace ClaudeDesk;

public partial class MainWindow
{
    private const string RepoSlug = "steeze1213/GAFIL";
    private static readonly HttpClient Http = CreateHttp();
    private readonly DispatcherTimer _updateTimer = new() { Interval = TimeSpan.FromHours(24) };
    private MenuItem _updateMenu;
    private string _newVersionUrl;
    private DateTime _updateNoticeUntil = DateTime.MinValue;   // 이 시각까지 고양이 위에 "새 버전" 표시

    private static HttpClient CreateHttp()
    {
        var h = new HttpClient { Timeout = TimeSpan.FromSeconds(15) };
        h.DefaultRequestHeaders.UserAgent.ParseAdd("GAFIL-update-check");   // GitHub API는 User-Agent가 필요
        h.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
        return h;
    }

    public static Version CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0);

    private void InitUpdateCheck()
    {
        _updateMenu = new MenuItem { Visibility = Visibility.Collapsed, FontWeight = FontWeights.SemiBold };
        _updateMenu.Click += (_, _) =>
        {
            if (_newVersionUrl == null) return;
            try { Process.Start(new ProcessStartInfo(_newVersionUrl) { UseShellExecute = true }); } catch { }
        };
        CatMenu.Items.Insert(0, _updateMenu);

        if (!_settings.CheckUpdates) return;
        _updateTimer.Tick += async (_, _) => await CheckForUpdateAsync();
        Loaded += async (_, _) =>
        {
            await Task.Delay(TimeSpan.FromSeconds(10));   // 켜자마자 네트워크를 쓰지 않게 잠깐 기다림
            await CheckForUpdateAsync();
            _updateTimer.Start();
        };
    }

    private async Task CheckForUpdateAsync()
    {
        try
        {
            using var resp = await Http.GetAsync($"https://api.github.com/repos/{RepoSlug}/releases/latest");
            if (!resp.IsSuccessStatusCode) { AppSettings.Log($"[업데이트] 확인 실패: HTTP {(int)resp.StatusCode}"); return; }
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            string tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
            string url = doc.RootElement.TryGetProperty("html_url", out var u) ? u.GetString() : $"https://github.com/{RepoSlug}/releases/latest";

            if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return;
            var current = CurrentVersion;
            // 비교는 앞 세 자리(1.2.3)만: 어셈블리 버전의 네 번째 자리(빌드 번호)는 무시
            if (new Version(latest.Major, latest.Minor, Math.Max(0, latest.Build)) <=
                new Version(current.Major, current.Minor, Math.Max(0, current.Build))) return;

            // 링크는 우리 저장소 주소일 때만 씀 (응답이 이상해도 엉뚱한 사이트를 열지 않게)
            _newVersionUrl = url != null && url.StartsWith($"https://github.com/{RepoSlug}/", StringComparison.OrdinalIgnoreCase)
                ? url : $"https://github.com/{RepoSlug}/releases/latest";
            _updateMenu.Header = L.T($"⬆ 새 버전 받기 ({tag})", $"⬆ Get the new version ({tag})");
            _updateMenu.Visibility = Visibility.Visible;

            // 같은 버전은 한 번만 알림 (매일 귀찮게 하지 않게)
            if (_settings.NotifiedVersion != tag)
            {
                _settings.NotifiedVersion = tag;
                try { _settings.Save(); } catch { }
                _updateNoticeLabel = L.T($"⬆ 새 버전 {tag}", $"⬆ New version {tag}");
                _updateNoticeUntil = DateTime.Now.AddMinutes(1);
                UpdatePill();
                AppSettings.Log($"[업데이트] 새 버전 있음: {tag} (지금 {current})");
                await Task.Delay(TimeSpan.FromMinutes(1.05));
                UpdatePill();
            }
        }
        catch (Exception ex)
        {
            AppSettings.Log($"[업데이트] 확인 실패: {ex.Message}");   // 오프라인 등: 조용히 넘어감
        }
    }

    private string _updateNoticeLabel;
    private bool ShowUpdateNotice => _updateNoticeLabel != null && DateTime.Now < _updateNoticeUntil;
}
