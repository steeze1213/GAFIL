// v1.2 기능: GAFIL 목소리 (답을 소리 내어 읽기)
// Windows에 깔린 음성(SAPI)으로 읽음. 한국어 글이면 한국어 음성, 아니면 영어 음성을 고름
// 고양이 목소리 만들기: Windows 기본 음성은 SSML의 음 높이(pitch) 지시를 무시함(실험으로 확인) →
//   소리를 메모리에 먼저 만든 뒤, WAV 머리글의 재생 속도(샘플레이트)만 18% 올려서 재생 → 음이 확실히 높아짐
//   (빨라지는 만큼 말하기 속도는 조금 늦춰서 만들어 둠)
// 읽기 전에 이모지·마크다운 기호·주소(URL)를 정리해서 이상한 소리가 나지 않게 함
using System.Globalization;
using System.IO;
using System.Media;
using System.Security;
using System.Speech.AudioFormat;
using System.Speech.Synthesis;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeDesk;

public sealed class CatVoice : IDisposable
{
    private const int MaxChars = 700;          // 너무 긴 답은 앞부분만 읽음 (끝까지 기다리기 지루하지 않게)
    private const int SynthRate = 22050;       // 만들 때의 샘플레이트
    private const double PitchUp = 1.18;       // 재생할 때 이만큼 빠르게 → 음이 높아짐 (고양이 목소리)

    private readonly object _lock = new();
    private CancellationTokenSource _cts;
    private SoundPlayer _player;

    // 읽기 시작/끝 (고양이 입 모양 애니메이션용). 백그라운드 스레드에서 오므로 받는 쪽에서 UI 스레드로 넘김
    public event Action<bool> SpeakingChanged;

    public bool Available { get; }

    public CatVoice()
    {
        try
        {
            using var s = new SpeechSynthesizer();
            Available = s.GetInstalledVoices().Any(v => v.Enabled);
        }
        catch (Exception ex)
        {
            AppSettings.Log($"[목소리] 음성 출력 준비 실패: {ex.Message}");
            Available = false;
        }
    }

    // 글을 읽음. 이미 읽던 것은 멈추고 새로 읽음
    public void Speak(string text)
    {
        if (!Available) return;
        Stop();
        string clean = CleanForSpeech(text);
        if (clean.Length == 0) return;

        var cts = new CancellationTokenSource();
        lock (_lock) _cts = cts;
        Task.Run(() =>
        {
            try
            {
                byte[] wav = Render(clean);
                if (cts.IsCancellationRequested || wav == null) return;

                var player = new SoundPlayer(new MemoryStream(wav));
                player.Load();   // 소리 데이터 복사는 잠금 밖에서 미리 (느릴 수 있음)

                // 재생 시작은 잠금 안에서: Stop()도 같은 잠금을 쓰므로 '시작 전에 취소'나 '시작 후 멈춤' 중 하나가 반드시 됨
                lock (_lock)
                {
                    if (cts.IsCancellationRequested) { player.Dispose(); return; }
                    _player = player;
                    player.Play();   // 비동기 재생
                }
                SpeakingChanged?.Invoke(true);
                try
                {
                    // 재생 길이만큼 기다림 (Stop()이 취소하면 바로 끝)
                    double seconds = (wav.Length - 44) / 2.0 / BitConverter.ToInt32(wav, 24);
                    Task.Delay(TimeSpan.FromSeconds(seconds + 0.2), cts.Token).Wait();
                }
                catch (AggregateException) { }   // 취소됨
                finally
                {
                    lock (_lock)
                    {
                        if (ReferenceEquals(_player, player)) { try { player.Stop(); } catch { } _player = null; }
                    }
                    player.Dispose();
                    SpeakingChanged?.Invoke(false);
                }
            }
            catch (Exception ex)
            {
                AppSettings.Log($"[목소리] 읽기 실패: {ex.Message}");
                SpeakingChanged?.Invoke(false);
            }
        });
    }

    public void Stop()
    {
        lock (_lock)
        {
            try { _cts?.Cancel(); } catch { }
            try { _player?.Stop(); } catch { }
        }
    }

    // 글 → 고양이 목소리 WAV (메모리)
    internal byte[] Render(string clean)
    {
        using var synth = new SpeechSynthesizer();
        using var pcm = new MemoryStream();
        synth.SetOutputToAudioStream(pcm, new SpeechAudioFormatInfo(SynthRate, AudioBitsPerSample.Sixteen, AudioChannel.Mono));
        synth.SpeakSsml(BuildSsml(clean, synth));
        synth.SetOutputToNull();
        if (pcm.Length == 0) return null;
        return Wav(pcm.ToArray(), (int)(SynthRate * PitchUp));
    }

    // SSML(음성 표기 언어): 글 언어에 맞는 음성 + 재생을 빠르게 할 만큼 말하기는 조금 느리게
    internal static string BuildSsml(string clean, SpeechSynthesizer synth)
    {
        bool korean = clean.Any(c => c >= '가' && c <= '힣');
        var voice = PickVoice(synth, korean ? "ko" : "en");
        string lang = voice?.Culture.Name ?? (korean ? "ko-KR" : "en-US");
        string inner = $"<prosody rate=\"-12%\">{SecurityElement.Escape(clean)}</prosody>";
        if (voice != null) inner = $"<voice name=\"{SecurityElement.Escape(voice.Name)}\">{inner}</voice>";
        return $"<speak version=\"1.0\" xmlns=\"http://www.w3.org/2001/10/synthesis\" xml:lang=\"{lang}\">{inner}</speak>";
    }

    // 원하는 언어의 음성: 여자 목소리(더 고양이 같음)를 먼저, 없으면 그 언어의 아무 음성
    private static VoiceInfo PickVoice(SpeechSynthesizer synth, string twoLetter)
    {
        try
        {
            var voices = synth.GetInstalledVoices().Where(v => v.Enabled).Select(v => v.VoiceInfo)
                .Where(v => v.Culture.TwoLetterISOLanguageName == twoLetter).ToList();
            return voices.FirstOrDefault(v => v.Gender == VoiceGender.Female) ?? voices.FirstOrDefault();
        }
        catch { return null; }
    }

    // 16비트 모노 PCM에 WAV 머리글을 붙임. sampleRate를 실제보다 높게 적으면 빠르고 높게 재생됨
    internal static byte[] Wav(byte[] pcm, int sampleRate)
    {
        using var ms = new MemoryStream(44 + pcm.Length);
        using var w = new BinaryWriter(ms);
        w.Write("RIFF"u8.ToArray()); w.Write(36 + pcm.Length); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(sampleRate); w.Write(sampleRate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(pcm.Length); w.Write(pcm);
        return ms.ToArray();
    }

    // 읽기 좋게 정리: 주소·마크다운·이모지 빼기, 공백 줄이기, 너무 길면 문장 경계에서 자르기
    internal static string CleanForSpeech(string s)
    {
        if (string.IsNullOrWhiteSpace(s)) return "";
        s = Regex.Replace(s, @"https?://\S+", L.T("링크", "link"));
        s = Regex.Replace(s, @"[`*_#>|~\[\]{}<>]", " ");
        s = Regex.Replace(s, @"^\s*-\s+", "", RegexOptions.Multiline);   // 목록 기호

        // 이모지·기호(그림 문자) 빼기: 서로게이트 쌍(대부분의 이모지)과 기타 기호 범주, 이모지 변형 선택자
        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (char.IsSurrogate(c) || c == '\uFE0F' || c == '\u200D') continue;   // 이모지·이모지 변형 선택자·결합자
            if (char.IsControl(c) && c != '\n' && c != '\t') continue;   // SSML(XML)에 쓸 수 없는 제어 문자
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.OtherSymbol) continue;
            sb.Append(c);
        }
        s = Regex.Replace(sb.ToString(), @"\s+", " ").Trim();

        if (s.Length > MaxChars)
        {
            int cut = s.LastIndexOfAny(new[] { '.', '!', '?', '。' }, MaxChars);
            s = s[..(cut > MaxChars / 2 ? cut + 1 : MaxChars)];
        }
        return s;
    }

    public void Dispose() => Stop();
}
