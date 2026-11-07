// 처음 시작 도우미 열기 (SetupWindow.cs): 시작할 때 준비가 안 됐으면 자동으로, 메뉴 "Claude 연결 설정"으로 언제든
using System.Windows;
using System.Windows.Controls;

namespace ClaudeDesk;

public partial class MainWindow
{
    private SetupWindow _setup;

    private void InitSetup()
    {
        var item = new MenuItem { Header = L.T("Claude 연결 설정", "Connect to Claude") };
        item.Click += (_, _) => OpenSetup();
        CatMenu.Items.Insert(CatMenu.Items.IndexOf(MenuSettings), item);

        // 파일만 확인 (claude를 실행하지 않음). 고양이가 자리 잡은 뒤에 띄움
        Loaded += (_, _) =>
        {
            if (!SetupWindow.NeedsSetup()) return;
            AppSettings.Log("[시작 도우미] Claude Code가 없거나 로그인 흔적이 없어서 엶");
            Dispatcher.BeginInvoke(OpenSetup, System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        };
    }

    private void OpenSetup()
    {
        if (_setup != null) { _setup.Activate(); return; }
        _setup = new SetupWindow();
        // 고양이가 있는 모니터 가운데 위쪽에 띄움 (모니터가 여러 개여도 고양이 옆에서)
        var wa = WorkArea;
        _setup.WindowStartupLocation = WindowStartupLocation.Manual;
        _setup.Left = wa.Left + (wa.Width - _setup.Width) / 2;
        _setup.Top = wa.Top + wa.Height * 0.12;
        _setup.Closed += (_, _) =>
        {
            _setup = null;
            _claude.Sleep();   // 새 로그인·API 키로 다음 질문 때 다시 켜지게
        };
        _setup.Show();
    }
}
