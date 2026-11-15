// 고양이 위 한 줄(CPU/GPU, 알림)을 글자 대신 도형으로 그림
// 투명 창(AllowsTransparency)에서는 절전에서 깨어난 뒤나 다른 그래픽 장치(내장 GPU ↔ NVIDIA)에 연결된 모니터로 옮긴 뒤
// WPF 글꼴 캐시가 깨져 배경만 남고 글자가 사라지는 일이 있음. 도형은 글꼴 캐시를 쓰지 않아서 늘 보임
using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace ClaudeDesk;

public partial class MainWindow
{
    private static readonly Typeface PillFace = new(new FontFamily("Cascadia Mono, Consolas, Malgun Gothic"),
        FontStyles.Normal, FontWeights.Normal, FontStretches.Normal);
    private const double PillFontSize = 10.5;
    private string _pillText = "";

    private string PillText
    {
        get => _pillText;
        set
        {
            if (value == _pillText && StatsText.Data != null) return;   // 1초마다 같은 값이면 다시 만들지 않음
            _pillText = value;
            RebuildPillText();
        }
    }

    private void RebuildPillText()
    {
        var ft = new FormattedText(_pillText, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
            PillFace, PillFontSize, Brushes.White, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        var g = ft.BuildGeometry(new Point(0, 0));
        g.Freeze();
        StatsText.Data = g;
        // 도형은 글자 상자보다 작게 잡히므로 줄 높이와 너비를 글자 기준으로 고정 (위아래 여백이 TextBlock과 같게)
        StatsText.Width = ft.WidthIncludingTrailingWhitespace;
        StatsText.Height = ft.Height;
    }
}
