// v1.1 기능: PC 상태와 시간에 따라 반응하는 고양이
// - CPU가 높으면(85% 이상): 앉아서 혀를 내밀고 헥헥 (PantA/PantB를 번갈아)
// - GPU 온도가 높으면(80°C 이상): 얼굴 옆에 땀방울
// - 밤(23시~7시): 천천히 걷고, 자주 누워 자며 머리 위에 Zz
// 그림은 기존 규칙(문자 격자)을 그대로 쓰고, 땀·Zz는 프레임 위에 덧그리는 점(overlay)으로 처리
namespace ClaudeDesk;

public partial class MainWindow
{
    private const double BusyCpu = 85;     // 헥헥거리기 시작하는 CPU 사용률(%)
    private const double HotGpu = 80;      // 땀 흘리기 시작하는 GPU 온도(°C)

    private int _animTick;
    // 잠잘 때 머리 위 Z (5x5): 윗줄, 대각선 3칸, 아랫줄
    private static readonly (int X, int Y)[] ZGlyph =
    {
        (0, 0), (1, 0), (2, 0), (3, 0), (4, 0),
        (3, 1), (2, 2), (1, 3),
        (0, 4), (1, 4), (2, 4), (3, 4), (4, 4),
    };                 // 혀·Zz 깜빡임용 틱

    // 앉아서 헥헥: 혀(P)가 나왔다 들어갔다
    private static readonly string[] PantA =
    {
        "..........K...K.",
        ".........KPK.KPK",
        ".........KOOOOOK",
        ".........KOEOEOK",
        ".........KLOPOLK",
        "........KKOOPOK.",
        ".......KODOOLLK.",
        "......KODOOLLLK.",
        ".KK..KOOOOOLLLK.",
        "KOOK.KOOOOOOOK..",
        ".KOOKKOOKKOOOK..",
        "..KKKKKKKKKKKK..",
    };

    private static readonly string[] PantB =
    {
        "..........K...K.",
        ".........KPK.KPK",
        ".........KOOOOOK",
        ".........KOKOKOK",
        ".........KLOPOLK",
        "........KKOOPOK.",
        ".......KODOOPLK.",
        "......KODOOLLLK.",
        ".KK..KOOOOOLLLK.",
        "KOOK.KOOOOOOOK..",
        ".KOOKKOOKKOOOK..",
        "..KKKKKKKKKKKK..",
    };

    private bool IsNight
    {
        get
        {
            int h = DateTime.Now.Hour;
            return h >= 23 || h < 7;
        }
    }

    // CPU/GPU 측정을 끄면(메뉴) 마지막 값이 계속 남으므로 측정이 켜져 있을 때만 반응
    private bool IsBusy => _settings.ShowStats && _lastStats?.Cpu is double c && c >= BusyCpu;
    private bool IsHot => _settings.ShowStats && _lastStats?.Gpu.TempC is double t && t >= HotGpu;

    // 걷는 속도: 밤에는 느릿느릿
    private double CurrentWalkSpeed => IsNight ? WalkSpeed * 0.55 : WalkSpeed;

    // 기분에 따라 다음 행동을 먼저 정해 봄. 정했으면 true (아니면 평소 확률대로)
    private bool TryChooseMoodState()
    {
        if (IsNight && _rnd.Next(100) < 60)
        {
            _state = CatState.Lie;
            _stateTicks = _rnd.Next(600, 1500);     // 약 10~25초 동안 쿨쿨
            return true;
        }
        if (IsBusy && _rnd.Next(100) < 50)
        {
            _state = CatState.Pant;
            _stateTicks = _rnd.Next(180, 360);      // 약 3~6초 헥헥
            return true;
        }
        return false;
    }

    // 지금 프레임에 덧그릴 점들: (x, y, 문자). 문자는 팔레트의 S(땀) 또는 Z(잠)
    // 머리 위치는 누운 그림만 3칸 아래라서 그만큼 내려서 그림
    private List<(int X, int Y, char C)> MoodOverlay(string[] frame)
    {
        var dots = new List<(int, int, char)>();
        int headDy = ReferenceEquals(frame, LieFrame) ? 3 : 0;
        bool hanging = ReferenceEquals(frame, HangC) || ReferenceEquals(frame, HangL) || ReferenceEquals(frame, HangR);

        if (IsHot)
        {
            // 얼굴 옆에 땀방울: 위는 작고 아래는 길게 (매달린 정면 그림은 얼굴 왼쪽 바깥)
            if (headDy > 0)
            {
                // 누운 모습: 왼쪽은 몸통이라 머리 위 빈칸(귀 왼쪽)에 땀
                dots.Add((9, 1, 'S'));
                dots.Add((8, 2, 'S'));
            }
            else
            {
                int sx = hanging ? 1 : 8, sx2 = hanging ? 1 : 7;
                dots.Add((sx, 1, 'S'));
                dots.Add((sx2, 3, 'S'));
                dots.Add((sx2, 4, 'S'));
            }
        }

        if (ReferenceEquals(frame, LieFrame) && !_bubbleOpen && IsNight)
        {
            // Z: 몸통 위 빈칸에 5x5 Z가 한 칸씩 오르내림 (3x3은 대각선이 안 보여서 대문자 I처럼 보였음)
            // 왼쪽을 볼 때는 그림 전체가 좌우 반전되므로 Z를 미리 뒤집어 둬야 화면에서 바른 Z가 됨
            int up = (_animTick / 40) % 2;
            bool flipped = _dir < 0 && !_dragging;
            foreach (var (x, y) in ZGlyph)
                dots.Add((4 + (flipped ? 4 - x : x), 1 - up + y, 'Z'));
        }
        AddAlertMark(dots);   // 몸짓 알림 중이면 머리 옆에 '!'
        AddEggOverlay(dots);  // 츄르 하트
        return dots;
    }
}
