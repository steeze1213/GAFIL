// v1.3 기능: 가끔 '야옹' (말소리 대신 의미 없는 고양이 울음)
// - 녹음 파일 없이 코드로 만든 소리: 성대 떨림(기본음 + 배음)에 입 모양(포먼트)이 '이→아→우'로 바뀌는 것을 흉내
// - 시끄럽지 않게: 알림이 울릴 때, 말풍선이 닫혀 있는 동안 답이 왔을 때, 들어서 옮긴 뒤 내려놓을 때만
//   알림이 아니면 10분에 한 번까지만. 메뉴 '야옹 소리'로 끌 수 있음 (settings.json "Meow")
using System.IO;
using System.Media;

namespace ClaudeDesk;

public static class CatMeow
{
    private const int Rate = 22050;
    private static readonly TimeSpan MinGap = TimeSpan.FromMinutes(10);
    private static DateTime _last = DateTime.MinValue;
    private static readonly Random Rng = new();
    private static SoundPlayer _player;   // 재생이 끝나기 전에 GC가 치우지 않게 붙잡아 둠

    // 울어도 되면 울고 true. force: 알림처럼 간격 제한을 무시 (그래도 설정에서 끄면 안 욺)
    public static bool TryMeow(bool enabled, bool force = false, int times = 1)
    {
        if (!enabled) return false;
        if (!force && DateTime.Now - _last < MinGap) return false;
        _last = DateTime.Now;
        try
        {
            byte[] wav = Build(Math.Clamp(times, 1, 2));
            _player?.Dispose();
            _player = new SoundPlayer(new MemoryStream(wav));
            _player.Play();   // 비동기 재생
            return true;
        }
        catch { return false; }
    }

    // 야옹 1~2번을 이어 붙인 WAV (16bit mono)
    public static byte[] Build(int times)
    {
        var all = new List<float>();
        for (int i = 0; i < times; i++)
        {
            double pitch = 0.92 + Rng.NextDouble() * 0.18;   // 매번 조금씩 다른 높이
            double len = 0.50 + Rng.NextDouble() * 0.18;     // 0.5~0.68초
            all.AddRange(OneMeow(pitch, len, i == times - 1 ? 1.0 : 0.85));
            if (i < times - 1) all.AddRange(new float[(int)(Rate * 0.16)]);
        }
        return ToWav(all);
    }

    private static float[] OneMeow(double pitch, double seconds, double gain)
    {
        int n = (int)(Rate * seconds);
        var buf = new float[n];
        double phase = 0;
        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;                       // 0~1 진행도
            // 기본음 높이: 올라갔다가 끝에서 내려옴 ("먀-오")
            double f0 = pitch * (560 + 260 * Math.Sin(Math.PI * Math.Min(1, t * 1.35)) - 120 * Math.Max(0, t - 0.6) / 0.4);
            f0 *= 1 + 0.012 * Math.Sin(2 * Math.PI * 6.5 * i / Rate);   // 살짝 떨림
            phase += 2 * Math.PI * f0 / Rate;

            // 입 모양(포먼트): 처음엔 닫힘(m) → '이' → '아' → '우'
            double f1 = Lerp3(t, 350, 850, 450), f2 = Lerp3(t, 2300, 1500, 900);
            double s = 0;
            for (int k = 1; k <= 14; k++)
            {
                double fk = f0 * k;
                if (fk > Rate / 2 - 500) break;
                double amp = Res(fk, f1, 160) + 0.7 * Res(fk, f2, 220) + 0.08 / k;   // 포먼트에 가까운 배음이 크게
                s += amp * Math.Sin(k * phase);
            }
            // 숨소리 약간
            s += (Rng.NextDouble() - 0.5) * 0.03;

            // 세기: 부드럽게 시작('m'), 중간에 크고, 천천히 사라짐
            double env = Smooth(t / 0.12) * (1 - Smooth((t - 0.7) / 0.3));
            buf[i] = (float)(s * env * gain);
        }
        // 크기 맞추기
        float peak = buf.Max(Math.Abs);
        if (peak > 0) for (int i = 0; i < n; i++) buf[i] = buf[i] / peak * 0.35f;
        return buf;
    }

    private static double Res(double f, double center, double bw) => Math.Exp(-Math.Pow((f - center) / bw, 2));
    private static double Smooth(double x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }
    // 0→0.45→1 구간에서 a→b→c로 부드럽게
    private static double Lerp3(double t, double a, double b, double c) =>
        t < 0.45 ? a + (b - a) * Smooth(t / 0.45) : b + (c - b) * Smooth((t - 0.45) / 0.55);

    private static byte[] ToWav(List<float> samples)
    {
        using var ms = new MemoryStream();
        using var w = new BinaryWriter(ms);
        int dataLen = samples.Count * 2;
        w.Write("RIFF"u8.ToArray()); w.Write(36 + dataLen); w.Write("WAVE"u8.ToArray());
        w.Write("fmt "u8.ToArray()); w.Write(16); w.Write((short)1); w.Write((short)1);
        w.Write(Rate); w.Write(Rate * 2); w.Write((short)2); w.Write((short)16);
        w.Write("data"u8.ToArray()); w.Write(dataLen);
        foreach (var f in samples) w.Write((short)Math.Clamp(f * 32767, -32768, 32767));
        w.Flush();
        return ms.ToArray();
    }
}
