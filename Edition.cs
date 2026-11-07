// 배포판 구분
// - 기본(GitHub·개인용): 그 PC의 Claude Code에 로그인된 계정(구독 또는 API 키)을 그대로 씀
// - API 키 전용 배포판: dotnet publish ... -p:ApiKeyOnly=true 로 빌드. Steam 같은 곳에 무료로 배포할 때 사용
//   Anthropic 정책상 다른 개발자가 배포하는 앱은 Claude 구독(claude.ai) 로그인을 제공하면 안 되고 API 키를 써야 함
//   → 구독 로그인 버튼 없음, API 키가 없으면 Claude를 실행하지 않음, GitHub 업데이트 알림 없음(배포처가 업데이트 담당)
namespace ClaudeDesk;

public static class Edition
{
#if API_KEY_ONLY
    public static readonly bool ApiKeyOnly = true;
#else
    public static readonly bool ApiKeyOnly = false;
#endif

    // ANTHROPIC_API_KEY가 있는지. GAFIL이 켜진 뒤 사용자 환경 변수에 넣은 경우도 찾아서 지금 프로세스에 반영 (claude가 물려받게)
    public static bool HasApiKey()
    {
        if (!string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY"))) return true;
        string user = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY", EnvironmentVariableTarget.User);
        if (string.IsNullOrWhiteSpace(user)) return false;
        Environment.SetEnvironmentVariable("ANTHROPIC_API_KEY", user);
        return true;
    }
}
