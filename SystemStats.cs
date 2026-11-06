// 3단계: CPU·GPU 사용량 측정
// GPU 측정은 인터페이스(IGpuSensor)로 나눔 → NVIDIA(NVML)와 내장 GPU(Intel·AMD, 성능 카운터) PC에서 같은 코드가 돎
using System.Diagnostics;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace ClaudeDesk;

// GPU 한 번 측정한 값. 읽을 수 없는 항목은 null → 화면에 "N/A"
public sealed record GpuReading(double? Util, double? TempC, double? VramUsedGB, double? VramTotalGB);

// 화면에 보여줄 한 번의 측정 결과
public sealed record StatsSnapshot(double? Cpu, string GpuName, GpuReading Gpu);

public interface IGpuSensor : IDisposable
{
    string Name { get; }
    GpuReading Read();
}

// 2초마다 백그라운드 스레드(background thread)에서 측정하고 Updated 이벤트로 알림
// 왜 백그라운드인가: 성능 카운터·WMI 호출은 수십 ms 걸릴 수 있어 UI(고양이 애니메이션)가 끊기지 않게 함
public sealed class SystemStats : IDisposable
{
    public event Action<StatsSnapshot> Updated;

    private readonly Timer _timer;
    private PerformanceCounter _cpu;
    private IGpuSensor _gpu;
    private bool _initialized;
    private int _busy;   // 이전 측정이 끝나기 전에 다음 측정이 겹치지 않도록

    public SystemStats()
    {
        _timer = new Timer(_ => Tick(), null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start() => _timer.Change(0, 2000);
    public void Stop() => _timer.Change(Timeout.Infinite, Timeout.Infinite);

    private void Tick()
    {
        if (Interlocked.Exchange(ref _busy, 1) == 1) return;
        try
        {
            if (!_initialized)
            {
                // 처음 한 번만 센서 준비 (느린 작업이라 UI 스레드에서 하지 않음)
                _cpu ??= CreateCpuCounter();
                _gpu ??= NvmlGpuSensor.TryCreate() ?? (IGpuSensor)new GenericGpuSensor();
                _initialized = true;   // 준비가 끝난 뒤에 표시 → 중간에 실패하면 다음 틱에 다시 시도
            }

            double? cpu = null;
            try { if (_cpu != null) cpu = Math.Clamp(_cpu.NextValue(), 0, 100); } catch { }

            GpuReading gpu;
            try { gpu = _gpu.Read(); } catch { gpu = new GpuReading(null, null, null, null); }

            Updated?.Invoke(new StatsSnapshot(cpu, _gpu.Name, gpu));
        }
        catch (Exception ex)
        {
            AppSettings.Log($"[시스템 정보] 측정 오류: {ex.Message}");
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    // CPU: 작업 관리자와 같은 값인 "% Processor Utility"를 먼저 쓰고, 없으면 "% Processor Time" 사용
    private static PerformanceCounter CreateCpuCounter()
    {
        foreach (var (cat, counter) in new[] { ("Processor Information", "% Processor Utility"), ("Processor", "% Processor Time") })
        {
            try
            {
                var pc = new PerformanceCounter(cat, counter, "_Total", readOnly: true);
                pc.NextValue();   // 첫 값은 항상 0이라 한 번 버림
                return pc;
            }
            catch { }
        }
        return null;
    }

    public void Dispose()
    {
        _timer.Dispose();
        _cpu?.Dispose();
        _gpu?.Dispose();
    }
}

// NVIDIA GPU: 드라이버에 포함된 NVML(nvml.dll)로 이름·사용률·온도·VRAM을 직접 읽음
// nvidia-smi를 매번 실행하는 것보다 훨씬 가벼움
public sealed class NvmlGpuSensor : IGpuSensor
{
    private const int Success = 0;
    private readonly IntPtr _device;

    public string Name { get; }

    private NvmlGpuSensor(IntPtr device, string name)
    {
        _device = device;
        Name = name;
    }

    // NVIDIA가 없는 PC(내장 GPU 노트북 등)에서는 nvml.dll이 없어서 null을 돌려줌 → 일반 센서로 대체
    public static NvmlGpuSensor TryCreate()
    {
        try
        {
            if (nvmlInit_v2() != Success) return null;
            if (nvmlDeviceGetHandleByIndex_v2(0, out IntPtr dev) != Success) { nvmlShutdown(); return null; }

            var buf = new byte[96];
            string name = nvmlDeviceGetName(dev, buf, (uint)buf.Length) == Success
                ? Encoding.ASCII.GetString(buf).TrimEnd('\0')
                : "NVIDIA GPU";
            // 이름은 실행 시 읽은 값을 그대로 씀 (모델명을 코드에 하드코딩하지 않음)
            return new NvmlGpuSensor(dev, name.Replace("NVIDIA GeForce ", ""));
        }
        catch (Exception)
        {
            // nvml.dll이 없거나(DllNotFound) 버전이 맞지 않거나 드라이버 오류 → 일반 센서로 대체
            return null;
        }
    }

    public GpuReading Read()
    {
        double? util = nvmlDeviceGetUtilizationRates(_device, out var u) == Success ? u.Gpu : null;
        double? temp = nvmlDeviceGetTemperature(_device, 0, out uint t) == Success ? t : null;   // 0 = GPU 코어 센서
        double? used = null, total = null;
        if (nvmlDeviceGetMemoryInfo(_device, out var m) == Success)
        {
            used = m.Used / 1073741824.0;    // 바이트 → GB
            total = m.Total / 1073741824.0;
        }
        return new GpuReading(util, temp, used, total);
    }

    public void Dispose()
    {
        try { nvmlShutdown(); } catch { }
    }

    [StructLayout(LayoutKind.Sequential)] private struct Utilization { public uint Gpu; public uint Memory; }
    [StructLayout(LayoutKind.Sequential)] private struct Memory { public ulong Total; public ulong Free; public ulong Used; }

    [DllImport("nvml.dll")] private static extern int nvmlInit_v2();
    [DllImport("nvml.dll")] private static extern int nvmlShutdown();
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetHandleByIndex_v2(uint index, out IntPtr device);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetName(IntPtr device, byte[] name, uint length);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetTemperature(IntPtr device, int sensor, out uint temp);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetUtilizationRates(IntPtr device, out Utilization util);
    [DllImport("nvml.dll")] private static extern int nvmlDeviceGetMemoryInfo(IntPtr device, out Memory memory);
}

// 모든 벤더 공통: Windows 성능 카운터 "GPU Engine"의 3D 엔진 사용률 합계, "GPU Adapter Memory"의 전용 메모리 사용량
// 온도는 벤더 공통 방법이 없어 N/A
public sealed class GenericGpuSensor : IGpuSensor
{
    // 인스턴스 이름 → 카운터. 목록을 새로 만들 때 계속 있는 인스턴스는 그대로 써서 이전 측정값을 유지함
    // (새로 만든 카운터의 첫 값은 항상 0이라, 전부 새로 만들면 10초마다 0%로 떨어짐)
    private readonly Dictionary<string, PerformanceCounter> _engines = new();
    private readonly Dictionary<string, PerformanceCounter> _memory = new();
    private DateTime _lastRefresh = DateTime.MinValue;

    public string Name { get; } = ReadGpuName();

    public GpuReading Read()
    {
        // GPU Engine 인스턴스는 프로세스마다 생기고 사라지므로 10초마다 목록을 새로 만듦
        if (DateTime.Now - _lastRefresh > TimeSpan.FromSeconds(10)) Refresh();

        double? util = null;
        if (_engines.Count > 0)
        {
            double sum = 0;
            foreach (var pc in _engines.Values) { try { sum += pc.NextValue(); } catch { } }
            util = Math.Clamp(sum, 0, 100);
        }

        double? used = null;
        if (_memory.Count > 0)
        {
            double bytes = 0;
            foreach (var pc in _memory.Values) { try { bytes += pc.NextValue(); } catch { } }
            used = bytes / 1073741824.0;
        }
        return new GpuReading(util, null, used, null);
    }

    private void Refresh()
    {
        _lastRefresh = DateTime.Now;
        Sync(_engines, "GPU Engine", "Utilization Percentage", inst => inst.EndsWith("engtype_3D", StringComparison.OrdinalIgnoreCase));
        Sync(_memory, "GPU Adapter Memory", "Dedicated Usage", _ => true);
    }

    // 현재 인스턴스 목록에 맞춰 카운터를 추가·삭제 (계속 있는 것은 유지)
    private static void Sync(Dictionary<string, PerformanceCounter> map, string category, string counter, Func<string, bool> keep)
    {
        string[] names;
        try { names = new PerformanceCounterCategory(category).GetInstanceNames(); }
        catch { return; }

        var current = new HashSet<string>(names.Where(keep));
        foreach (string gone in map.Keys.Where(k => !current.Contains(k)).ToList())
        {
            map[gone].Dispose();
            map.Remove(gone);
        }
        foreach (string inst in current)
        {
            if (map.ContainsKey(inst)) continue;
            try
            {
                var pc = new PerformanceCounter(category, counter, inst, true);
                pc.NextValue();   // 첫 값(항상 0)은 버림
                map[inst] = pc;
            }
            catch { }   // 그 사이 사라진 인스턴스
        }
    }

    // GPU 이름: WMI(Win32_VideoController)에서 첫 번째 실제 어댑터 이름을 읽음
    private static string ReadGpuName()
    {
        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name FROM Win32_VideoController");
            foreach (ManagementObject mo in searcher.Get())
            {
                string name = mo["Name"] as string;
                if (!string.IsNullOrWhiteSpace(name) && !name.Contains("Basic", StringComparison.OrdinalIgnoreCase))
                    return name.Trim();
            }
        }
        catch { }
        return "GPU";
    }

    private void DisposeCounters()
    {
        foreach (var pc in _engines.Values) pc.Dispose();
        foreach (var pc in _memory.Values) pc.Dispose();
        _engines.Clear();
        _memory.Clear();
    }

    public void Dispose() => DisposeCounters();
}
