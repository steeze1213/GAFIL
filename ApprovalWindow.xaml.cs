// 4단계 승인 창 코드: 허용/거부 결과를 돌려줌. 아무것도 누르지 않으면 5분 뒤 자동 거부
using System.Windows;
using System.Windows.Threading;

namespace ClaudeDesk;

public partial class ApprovalWindow : Window
{
    private static readonly TimeSpan Limit = TimeSpan.FromMinutes(5);
    private readonly DispatcherTimer _countdown = new() { Interval = TimeSpan.FromSeconds(1) };
    private readonly DateTime _deadline = DateTime.Now + Limit;

    public bool Approved { get; private set; }

    public ApprovalWindow(ApprovalRequest req)
    {
        InitializeComponent();

        SummaryText.Text = req.Summary;
        DetailText.Text = req.Detail;
        if (!string.IsNullOrEmpty(req.Warning))
        {
            WarningText.Text = "⚠ " + req.Warning;
            WarningText.Visibility = Visibility.Visible;
        }

        AllowButton.Click += (_, _) => { Approved = true; Close(); };
        DenyButton.Click += (_, _) => { Approved = false; Close(); };

        // 창 아무 곳이나 끌어서 옮길 수 있게
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == System.Windows.Input.MouseButtonState.Pressed) DragMove(); };

        // 자리를 비운 사이 계속 기다리지 않도록 제한 시간 뒤 자동 거부
        _countdown.Tick += (_, _) =>
        {
            var left = _deadline - DateTime.Now;
            if (left <= TimeSpan.Zero) { Approved = false; Close(); return; }
            CountdownText.Text = $"{(int)left.TotalMinutes}:{left.Seconds:00} 안에 고르지 않으면 자동으로 거부해요";
        };
        _countdown.Start();
        Closed += (_, _) => _countdown.Stop();

        // 기본 포커스는 거부 버튼 (Enter 실수로 실행되는 것을 막음)
        Loaded += (_, _) => { Activate(); DenyButton.Focus(); };
    }
}

// 승인 창에 보여줄 내용
public sealed record ApprovalRequest(string ToolName, string Summary, string Detail, string Warning);
