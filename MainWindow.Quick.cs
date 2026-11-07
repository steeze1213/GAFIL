// 바로 답하기: 계산·단위·시각·D-day 같은 간단한 질문은 Claude에게 보내지 않고 PC에서 답함 (토큰 0)
// 계산은 QuickAnswer.cs. 같은 말을 바로 한 번 더 보내면 그때는 Claude에게 물어봄
namespace ClaudeDesk;

public partial class MainWindow
{
    private string _lastQuick;   // 방금 PC에서 바로 답한 질문 (똑같이 다시 보내면 Claude에게)

    // 입력창 글이 간단한 질문이면 바로 답하고 true
    private bool TryQuickAnswer()
    {
        if (!_settings.QuickAnswers || HasAttachments) return false;
        string q = Input.Text.Trim();
        if (q.Length == 0) return false;
        if (q == _lastQuick) { _lastQuick = null; return false; }

        string a;
        try { a = QuickAnswer.TryAnswer(q, DateTime.Now); } catch { a = null; }
        if (a == null) { _lastQuick = null; return false; }

        _lastQuick = q;
        Input.Text = "";
        AddUserLine(q);
        AddCatLine(a + "\n" + L.T("(PC에서 바로 계산했어 · 토큰 0. 똑같이 한 번 더 보내면 Claude에게 물어볼게)",
                                  "(Worked out on this PC · zero tokens. Send the same thing again to ask Claude)"));
        ChatScroll.ScrollToEnd();
        AppSettings.Log("[바로 답] Claude에게 보내지 않고 PC에서 답함");
        return true;
    }
}
