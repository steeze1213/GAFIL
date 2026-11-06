// 화면 문구 언어 선택 (한국어 / 영어)
// 사용법: L.T("한국어", "English") → 지금 언어에 맞는 쪽을 돌려줌
// 언어는 settings.json의 "Language"로 정함: "auto"(Windows 표시 언어가 한국어면 한국어, 아니면 영어), "ko", "en"
// 로그 파일은 한국어로 남김 (문제를 찾을 때 한 언어로 보기 쉽게)
using System.Globalization;

namespace ClaudeDesk;

public static class L
{
    public static bool En { get; private set; }

    public static void Init(string language)
    {
        string lang = (language ?? "auto").Trim().ToLowerInvariant();
        En = lang switch
        {
            "ko" => false,
            "en" => true,
            // 표시 언어 또는 지역 형식 중 하나라도 한국어면 한국어
            // (일부 환경에서는 표시 언어가 en-US로 잡히기도 해서 형식(날짜·숫자) 쪽도 함께 봄)
            _ => !(CultureInfo.CurrentUICulture.TwoLetterISOLanguageName == "ko"
                   || CultureInfo.CurrentCulture.TwoLetterISOLanguageName == "ko"
                   || CultureInfo.InstalledUICulture.TwoLetterISOLanguageName == "ko"),
        };
    }

    public static string T(string ko, string en) => En ? en : ko;
}
