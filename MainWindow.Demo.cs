#if DEBUG
// 스토어 스크린샷용 데모 (Debug 빌드에서만, 배포 exe에는 들어가지 않음)
// GAFIL_DEMO=chat|quick|intro → Claude를 부르지 않고 예시 대화를 채워 말풍선을 엶. GAFIL_LANG=ko|en 으로 언어 고정
// 바로 답하기 줄은 QuickAnswer가 실제로 계산한 결과
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace ClaudeDesk;

public partial class MainWindow
{
    private void InitDemo()
    {
        string demo = Environment.GetEnvironmentVariable("GAFIL_DEMO");
        if (string.IsNullOrEmpty(demo)) return;
        _settings.SkillIntros = false;   // 데모 중엔 진짜 소개가 끼어들지 않게
        Loaded += (_, _) =>
        {
            var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
            t.Tick += (_, _) => { t.Stop(); RunDemo(demo); };
            t.Start();
        };
    }

    private void RunDemo(string demo)
    {
        ChatPanel.Children.Clear();
        string Quick(string q) => QuickAnswer.TryAnswer(q, DateTime.Now) + "\n" +
            L.T("(PC에서 바로 계산했어 · 토큰 0. 똑같이 한 번 더 보내면 Claude에게 물어볼게)", "(Worked out on this PC · zero tokens. Send the same thing again to ask Claude)");
        switch (demo)
        {
            case "chat":
                AddUserLine(L.T("지금 CPU 누가 제일 많이 써?", "What's using my CPU right now?"));
                AddActivityLine("PowerShell: Get-Process | Sort-Object CPU -Descending | Select-Object -First 5");
                AddCatLine(L.T("브라우저가 18% 정도로 제일 많이 써. 대부분 영상 탭이야.\n그다음은 Steam 4%, 나머지는 거의 쉬고 있어. 걱정할 거 없다냥.",
                               "Your browser, at about 18% — mostly a video tab.\nSteam is next at 4%, everything else is idle. Nothing to worry about. Meow."));
                AddUserLine(L.T("다운로드 폴더 정리해줘", "Tidy up my Downloads folder"));
                AddCatLine(L.T("파일을 종류별 폴더(문서·사진·설치 파일)로 옮길게.\n옮기기 전에 승인 창으로 먼저 보여 줄게냥.",
                               "I'll sort the files into folders (Documents, Images, Installers).\nI'll show you an approval window before moving anything."));
                break;
            case "quick":
                string q1 = L.T("3.5인치 cm", "3.5 inch to cm"), q2 = L.T("뉴욕 지금 몇 시", "time in New York");
                AddUserLine(q1); AddCatLine(Quick(q1));
                AddUserLine(q2); AddCatLine(Quick(q2));
                break;
            case "churu":
                AddUserLine(L.T("츄르", "churu"));
                AddCatLine(L.T("츄르다!! 냠냠냠… 골골골골 ♥\n(이건 Claude한테 안 보냈어. 토큰 0)", "Churu!! Nom nom nom… purrrr ♥\n(Not sent to Claude. Zero tokens.)"));
                _heartTicks = 1_000_000;
                KneadNow();
                break;
            case "intro":
                AddCatLine(L.T("뭐 궁금한 거 있어? 귀찮지만… 물어봐냥.", "Need something? Fine… I'm listening. Meow."));
                AddCatLine(L.T("있잖아, 나 이런 것도 할 수 있어냥.\n", "Hey, did you know I can do this?\n") +
                           L.T("\"30분 뒤에 회의 알려줘\" 하면 시간 됐을 때 화면 가운데로 뛰어가서 알려 줄게.",
                               "Say \"remind me about the meeting in 30 minutes\" and I'll run to the middle of the screen when it's time."));
                AddEggHintLine(L.T("✦ 비밀인데 너한테만 알려 줄게.\n내 앞에서 마우스를 좌우로 빠르게 흔들어 봐. 못 참아…",
                                   "✦ A secret, just for you.\nWiggle your mouse left and right fast in front of me. I can't resist…"));
                break;
        }
        if (!_bubbleOpen) ToggleBubble(activate: false);
        if (demo == "chat") ChatScroll.MaxHeight = 400;   // 대화가 스크롤 없이 다 보이게
        ChatScroll.ScrollToEnd();

        // GAFIL_DEMO_OUT가 있으면 잠시 뒤 창 내용을 투명 배경 PNG로 저장 (화면을 찍지 않음)
        string outPath = Environment.GetEnvironmentVariable("GAFIL_DEMO_OUT");
        if (string.IsNullOrEmpty(outPath)) return;
        var t = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
        t.Tick += (_, _) =>
        {
            t.Stop();
            var root = (FrameworkElement)Content;
            const double s = 3.0;
            var rtb = new RenderTargetBitmap((int)Math.Ceiling(root.ActualWidth * s), (int)Math.Ceiling(root.ActualHeight * s), 96 * s, 96 * s, PixelFormats.Pbgra32);
            rtb.Render(root);
            var enc = new PngBitmapEncoder();
            enc.Frames.Add(BitmapFrame.Create(rtb));
            using (var fs = File.Create(outPath)) enc.Save(fs);
            AppSettings.Log("[데모] 창 그림 저장");
        };
        t.Start();
    }
}
#endif
