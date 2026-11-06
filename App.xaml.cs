// 앱 진입점 코드: 고양이가 두 마리 뜨지 않도록 한 번만 실행되게 하고, 예기치 못한 오류는 로그에 남김
using System.Windows;

namespace ClaudeDesk;

public partial class App : Application
{
    private Mutex _single;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 자동 실행과 직접 실행이 겹쳐도 한 마리만: 이미 떠 있으면 조용히 종료
        _single = new Mutex(true, @"Local\GAFIL-DesktopCat", out bool first);
        if (!first)
        {
            Shutdown();
            return;
        }

        DispatcherUnhandledException += (_, ex) =>
        {
            AppSettings.Log($"[치명적 오류] {ex.Exception}");
            ex.Handled = true;   // 한 번의 오류로 고양이가 사라지지 않게 함
        };

        base.OnStartup(e);

        // 창을 만들다 실패하면 보이지 않는 프로세스로 남지 않도록 알리고 종료 (뮤텍스도 풀어서 다시 실행 가능하게)
        try
        {
            var win = new MainWindow();
            MainWindow = win;
            win.Show();
        }
        catch (Exception ex)
        {
            AppSettings.Log($"[치명적 오류] 시작 실패: {ex}");
            MessageBox.Show($"GAFIL을 시작하지 못했어요.\n{ex.Message}\n\n자세한 내용: %LOCALAPPDATA%\\ClaudeDesk\\logs\\commands.log",
                "GAFIL", MessageBoxButton.OK, MessageBoxImage.Error);
            _single.Dispose();
            _single = null;
            Shutdown(1);
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _single?.Dispose();
        base.OnExit(e);
    }
}
