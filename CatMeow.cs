// v1.3 기능: 가끔 '야옹' (말소리 대신 의미 없는 고양이 울음)
// - 녹음 파일 없이 코드로 만든 소리. 실제 목소리처럼 '성대 떨림(음원) → 입 모양 필터(포먼트)' 순서로 만듦:
//   음원 = 배음이 위로 갈수록 약해지는 떨림 + 숨소리, 필터 = 공명 3개(포먼트)가 'ㅁ→이아→우'로 움직임
//   음 높이에 미세한 흔들림(지터)을 넣어서 기계음 느낌을 줄임. 짧고 작은 '냥' 느낌
// - 시끄럽지 않게: 알림이 울릴 때, 말풍선이 닫혀 있는 동안 답이 왔을 때, 들어서 옮긴 뒤 내려놓을 때만
//   알림이 아니면 10분에 한 번까지만. 메뉴 '야옹 소리'로 끌 수 있음 (settings.json "Meow")
using System.IO;
using System.Media;

namespace ClaudeDesk;

public static class CatMeow
{
    private const int Rate = 44100;
    private static readonly TimeSpan MinGap = TimeSpan.FromMinutes(10);
    private static DateTime _last = DateTime.MinValue;
    private static readonly Random Rng = new();
    private static SoundPlayer _player;   // 재생이 끝나기 전에 GC가 치우지 않게 붙잡아 둠

    // 울음 모양: 길이(초), 음 높이(시작·꼭대기·끝, Hz), 꼭대기 위치(0~1), 숨소리 양, 입을 벌리는 정도
    public sealed record Style(double Seconds, double F0Start, double F0Peak, double F0End, double PeakAt, double Breath, double Open);

    // 기본 모양: 짧은 '냥' (높고 톡 튀는 느낌, 0.24초)
    public static Style Default = new(0.24, 820, 1060, 900, 0.30, 0.12, 0.8);

    // 울어도 되면 울고 true. force: 알림처럼 간격 제한을 무시 (그래도 설정에서 끄면 안 욺)
    public static bool TryMeow(bool enabled, bool force = false, int times = 1)
    {
        if (!enabled) return false;
        if (!force && DateTime.Now - _last < MinGap) return false;
        _last = DateTime.Now;
        try
        {
            byte[] wav = Build(Math.Clamp(times, 1, 2), Default);
            _player?.Dispose();
            _player = new SoundPlayer(new MemoryStream(wav));
            _player.Play();   // 비동기 재생
            return true;
        }
        catch { return false; }
    }

    // 울음 1~2번을 이어 붙인 WAV (16bit mono)
    public static byte[] Build(int times, Style st)
    {
        var all = new List<float>();
        for (int i = 0; i < times; i++)
        {
            double k = 0.95 + Rng.NextDouble() * 0.10;        // 매번 조금씩 다른 높이
            double len = st.Seconds * (0.9 + Rng.NextDouble() * 0.2);
            // 두 번째 울음은 조금 낮고 짧게 (사람이 '야옹 야옹' 할 때처럼 끝이 내려감)
            if (i > 0) { k *= 0.93; len *= 0.9; }
            all.AddRange(OneMew(st with { Seconds = len }, k));
            if (i < times - 1) all.AddRange(new float[(int)(Rate * 0.12)]);
        }
        // 앞뒤로 아주 짧은 무음 (재생 시작·끝에서 '틱' 소리 방지)
        var pad = new float[(int)(Rate * 0.02)];
        all.InsertRange(0, pad);
        all.AddRange(pad);
        return ToWav(all);
    }

    private static float[] OneMew(Style st, double pitch)
    {
        int n = (int)(Rate * st.Seconds);
        var buf = new float[n];
        var f = new Formant[] { new(), new(), new() };
        double phase = 0, jitter = 0, lp = 0, prevSrc = 0;

        for (int i = 0; i < n; i++)
        {
            double t = (double)i / n;

            // 음 높이: 시작 → 꼭대기(부드럽게 올라감) → 끝(천천히 내려감) + 미세한 흔들림
            double f0 = t < st.PeakAt
                ? st.F0Start + (st.F0Peak - st.F0Start) * Smooth(t / st.PeakAt)
                : st.F0Peak + (st.F0End - st.F0Peak) * Smooth((t - st.PeakAt) / (1 - st.PeakAt));
            jitter += (Rng.NextDouble() - 0.5) * 0.002;   // 천천히 떠도는 흔들림 (±1% 안쪽)
            jitter *= 0.999;
            f0 *= pitch * (1 + jitter);
            phase += f0 / Rate;
            if (phase >= 1) phase -= 1;

            // 음원: 성대 떨림 펄스(배음이 위로 갈수록 약해짐) — 각 배음을 직접 더해 깨끗하게
            double src = 0;
            int maxK = (int)(9000 / f0);
            for (int k = 1; k <= maxK; k++)
                src += Math.Sin(2 * Math.PI * k * phase) / Math.Pow(k, 1.35);
            // 숨소리: 높은 쪽만 살짝 (흰 소음의 차분 = 고역)
            double noise = Rng.NextDouble() - 0.5;
            src = src * 0.5 + (noise - prevSrc) * st.Breath;
            prevSrc = noise;

            // 입 모양(포먼트): 처음 'ㅁ'(닫힘) → '이아'(크게 벌림) → '우'(오므림)
            double open = Open(t) * st.Open;
            double f1 = 450 + 650 * open;                   // 450 ~ 1100 Hz
            double f2 = t < 0.5 ? 1800 + 600 * open : 1300 + 1100 * open;   // 앞은 '이' 쪽, 뒤는 '우' 쪽으로
            double f3 = 3000 + 400 * open;
            if ((i & 31) == 0)
            {
                f[0].Set(f1, 110);
                f[1].Set(f2, 180);
                f[2].Set(f3, 300);
            }
            double y = f[0].Run(src) * 1.0 + f[1].Run(src) * 0.55 + f[2].Run(src) * 0.18;

            // 입을 닫고 있을 땐 소리가 코로 나와 먹먹함 ('ㅁ') → 저역만 통과
            lp += (y - lp) * (0.15 + 0.85 * open);
            y = lp;

            // 세기: 'ㅁ'으로 부드럽게 시작해서 벌릴 때 커지고, 끝에서 숨이 빠지듯 작아짐
            double env = Smooth(t / 0.10) * Math.Pow(1 - Smooth((t - 0.55) / 0.45), 1.3);
            buf[i] = (float)(y * env);
        }
        float peak = buf.Max(Math.Abs);
        if (peak > 0) for (int i = 0; i < n; i++) buf[i] = buf[i] / peak * 0.28f;   // 작게
        return buf;
    }

    // 입을 벌린 정도 (0~1): 처음 닫힘 → 30%쯤 가장 크게 → 끝으로 갈수록 오므림
    private static double Open(double t) =>
        t < 0.3 ? Smooth(t / 0.3) : 1 - 0.75 * Smooth((t - 0.3) / 0.7);

    private static double Smooth(double x) { x = Math.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }

    // 공명 하나 (대역 통과 필터, RBJ biquad)
    private sealed class Formant
    {
        private double b0, b2, a1, a2, x1, x2, y1, y2;
        public void Set(double freq, double bw)
        {
            double w = 2 * Math.PI * freq / Rate, q = freq / bw, alpha = Math.Sin(w) / (2 * q), a0 = 1 + alpha;
            b0 = alpha / a0; b2 = -alpha / a0; a1 = -2 * Math.Cos(w) / a0; a2 = (1 - alpha) / a0;
        }
        public double Run(double x)
        {
            double y = b0 * x + b2 * x2 - a1 * y1 - a2 * y2;
            x2 = x1; x1 = x; y2 = y1; y1 = y;
            return y;
        }
    }

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
