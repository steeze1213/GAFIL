// v1.1 기능: 고양이 털 색과 옷·소품 고르기 (오른쪽 클릭 → 모습 바꾸기)
// - 기본은 지금 디자인(주황 털, 옷 없음). 고른 것은 settings.json의 "Skin", "Outfit"에 저장
// - 털 색: 그림 문자 O(몸통)·D(줄무늬)·L(배)·E(눈)·P(분홍)의 색만 바꿈 → 모든 동작 그림에 그대로 적용
// - 옷·소품: 머리 위치에 맞춰 덧그리는 점(overlay). 옆모습(걷기·앉기·점프·헥헥), 누운 모습(머리가 3칸 아래),
//   매달린 정면 모습(목덜미 잡힘)마다 위치를 따로 정함
using System.Windows.Controls;
using System.Windows.Media;

namespace ClaudeDesk;

public partial class MainWindow
{
    // 털 색 (이름, 한국어, 영어, O, D, L, E, P)
    private static readonly (string Id, string Ko, string En, uint O, uint D, uint L, uint E, uint P)[] Skins =
    {
        ("orange", "주황 (기본)", "Orange (default)", 0xE07B4F, 0xB95A36, 0xFBE3D0, 0x1A1A1A, 0xF39C9C),
        ("black",  "검정",       "Black",            0x34302F, 0x221F1F, 0x56504E, 0xC8E05A, 0xE79AA4),
        ("white",  "흰색",       "White",            0xF4F0EA, 0xDDD4C8, 0xFFFFFF, 0x2A5BA8, 0xF3A7B0),
        ("gray",   "회색 줄무늬", "Gray tabby",       0x9DA1A8, 0x6B6F77, 0xE3E5E8, 0x1A1A1A, 0xEFA3A8),
        ("cream",  "치즈 (크림)", "Cream",            0xF2C57C, 0xD69D4C, 0xFFF3DC, 0x1A1A1A, 0xF3A0A0),
    };

    // 옷·소품 (이름, 한국어, 영어)
    private static readonly (string Id, string Ko, string En)[] Outfits =
    {
        ("none",       "없음 (기본)", "None (default)"),
        ("crown",      "왕관",        "Crown"),
        ("sunglasses", "선글라스",    "Sunglasses"),
        ("scarf",      "빨간 목도리", "Red scarf"),
        ("bowtie",     "나비넥타이",  "Bow tie"),
    };

    // 옷·소품 그림: 옆모습 기준 좌표 (머리: 귀 10·14열, 눈 11·13열 3행, 목 5행)
    private static readonly Dictionary<string, (int X, int Y, char C)[]> OutfitSide = new()
    {
        ["crown"] = new[] { (11, 0, 'Y'), (13, 0, 'Y'), (11, 1, 'Y'), (12, 1, 'Y'), (13, 1, 'Y') },
        ["party"] = new[] { (12, 0, 'Y'), (12, 1, 'R'), (11, 2, 'B'), (12, 2, 'R'), (13, 2, 'B') },   // 고양이의 날 파티 모자
        ["sunglasses"] = new[] { (10, 3, 'G'), (11, 3, 'G'), (12, 3, 'G'), (13, 3, 'G'), (14, 3, 'G'), (11, 2, 'G'), (13, 2, 'G'), (13, 3, 'Z') },   // Z = 렌즈 반짝임(흰색)
        ["scarf"] = new[] { (9, 5, 'R'), (10, 5, 'R'), (11, 5, 'R'), (12, 5, 'R'), (13, 5, 'R'), (14, 5, 'R'), (10, 6, 'R') },
        ["bowtie"] = new[] { (11, 5, 'B'), (12, 5, 'K'), (13, 5, 'B') },
    };

    // 매달린 정면 모습 기준 좌표 (머리: 귀 4·11열, 눈 6·9열 3행, 목 5행)
    private static readonly Dictionary<string, (int X, int Y, char C)[]> OutfitFront = new()
    {
        ["crown"] = new[] { (6, 0, 'Y'), (9, 0, 'Y'), (6, 1, 'Y'), (7, 1, 'Y'), (8, 1, 'Y'), (9, 1, 'Y') },
        ["party"] = new[] { (7, 0, 'Y'), (8, 0, 'Y'), (7, 1, 'R'), (8, 1, 'B'), (6, 2, 'B'), (7, 2, 'R'), (8, 2, 'B'), (9, 2, 'R') },
        ["sunglasses"] = new[] { (4, 3, 'G'), (5, 3, 'G'), (6, 3, 'G'), (7, 3, 'G'), (8, 3, 'G'), (9, 3, 'G'), (10, 3, 'G'), (11, 3, 'G'), (5, 2, 'G'), (6, 2, 'G'), (9, 2, 'G'), (10, 2, 'G'), (5, 3, 'Z'), (9, 3, 'Z') },
        ["scarf"] = new[] { (5, 5, 'R'), (6, 5, 'R'), (7, 5, 'R'), (8, 5, 'R'), (9, 5, 'R'), (10, 5, 'R'), (5, 6, 'R') },
        ["bowtie"] = new[] { (6, 6, 'B'), (7, 6, 'K'), (8, 6, 'K'), (9, 6, 'B') },
    };

    // 벽 타기(등이 보이는 모습) 기준 좌표 (머리: 귀 5·10열 0행, 목 5행). 선글라스·나비넥타이는 앞쪽이라 안 보임
    private static readonly Dictionary<string, (int X, int Y, char C)[]> OutfitBack = new()
    {
        ["crown"] = new[] { (6, 0, 'Y'), (9, 0, 'Y'), (6, 1, 'Y'), (7, 1, 'Y'), (8, 1, 'Y'), (9, 1, 'Y') },
        ["party"] = new[] { (7, 0, 'Y'), (8, 0, 'Y'), (7, 1, 'R'), (8, 1, 'B'), (6, 2, 'B'), (7, 2, 'R'), (8, 2, 'B'), (9, 2, 'R') },
        ["scarf"] = new[] { (5, 5, 'R'), (6, 5, 'R'), (7, 5, 'R'), (8, 5, 'R'), (9, 5, 'R'), (10, 5, 'R') },
    };

    // 지금 털 색으로 만든 팔레트 (문자 → 색)
    private Dictionary<char, SolidColorBrush> _palette;

    private void InitLook()
    {
        _palette = BuildPalette(_settings.Skin);
        BuildLookMenu();
    }

    private static Dictionary<char, SolidColorBrush> BuildPalette(string skinId)
    {
        var s = Skins.FirstOrDefault(k => k.Id == skinId);
        if (s.Id == null) s = Skins[0];   // 모르는 값이면 기본(주황)
        return new Dictionary<char, SolidColorBrush>
        {
            ['K'] = Hex(0x3A2218),   // 외곽선은 모든 털 색에서 같음
            ['O'] = Hex(s.O),
            ['D'] = Hex(s.D),
            ['L'] = Hex(s.L),
            ['P'] = Hex(s.P),
            ['E'] = Hex(s.E),
            ['S'] = Hex(0x8EC9F0),   // 땀방울
            ['Z'] = Hex(0xFFFFFF),   // 잠 Zz
            ['Y'] = Hex(0xF2C230),   // 왕관 금색
            ['G'] = Hex(0x1C2433),   // 선글라스
            ['R'] = Hex(0xD8352A),   // 목도리 빨강
            ['B'] = Hex(0x3E6FD1),   // 나비넥타이 파랑
            ['H'] = Hex(0xFF6996),   // 츄르 하트
            ['X'] = Hex(0xFF5A3C),   // 몸짓 알림 '!'
        };
    }

    private static SolidColorBrush Hex(uint rgb) => Brush((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    // 지금 프레임에 입힐 옷·소품 점들
    private IEnumerable<(int X, int Y, char C)> OutfitOverlay(string[] frame)
    {
        string id = IsCatDay ? "party" : _settings.Outfit;   // 고양이의 날엔 파티 모자 (MainWindow.Eggs.cs)
        if (string.IsNullOrEmpty(id) || id == "none" || ReferenceEquals(frame, HissFrame)) yield break;

        bool hanging = ReferenceEquals(frame, HangC) || ReferenceEquals(frame, HangL) || ReferenceEquals(frame, HangR);
        bool back = ReferenceEquals(frame, ClimbA) || ReferenceEquals(frame, ClimbB);   // 벽 타기는 등이 보이는 모습
        var table = hanging ? OutfitFront : back ? OutfitBack : OutfitSide;
        if (!table.TryGetValue(id, out var dots)) yield break;

        // 옆모습마다 머리가 내려온 칸 수 (누운 그림 3칸, 웅크리기 3칸, 착지 4칸, 창 위 엎드림 1칸, 배 보이기 3칸)
        int dy = ReferenceEquals(frame, LieFrame) || ReferenceEquals(frame, CrouchFrame) || ReferenceEquals(frame, BellyFrame) ? 3
               : ReferenceEquals(frame, LandFrame) ? 4
               : ReferenceEquals(frame, LedgeA) || ReferenceEquals(frame, LedgeB) ? 1
               : 0;
        // 내려다보기: 머리가 윗변 아래로 5칸 내려가고 이마가 한 칸 길어서 눈은 6칸. 목은 안 보임
        bool peek = ReferenceEquals(frame, PeekFrame);
        // 흔들린 매달림 그림은 6행부터 몸이 옆으로 밀려 있음(SwingRows) → 장식도 같은 만큼 밀어 줌
        int swing = ReferenceEquals(frame, HangL) ? -1 : ReferenceEquals(frame, HangR) ? 1 : 0;
        bool mirror = ReferenceEquals(frame, ClimbB);   // 벽 타기 B는 A를 좌우로 뒤집은 그림
        int tapDx = ReferenceEquals(frame, TapA) || ReferenceEquals(frame, TapB) ? -1 : 0;   // 툭툭 그림은 한 칸 왼쪽
        foreach (var (x, y, c) in dots)
        {
            if (peek)
            {
                if (y >= 5) continue;
                yield return (x, y + (y >= 2 ? 6 : 5), c);
                continue;
            }
            int sx = y < 6 ? 0 : y < 9 ? swing : swing * 2;
            yield return (mirror ? 15 - x : x + sx + tapDx, y + dy, c);
        }
    }

    // 오른쪽 클릭 메뉴에 "모습 바꾸기" 추가: 털 색 / 옷·소품 (라디오처럼 하나만 체크)
    private void BuildLookMenu()
    {
        var look = new MenuItem { Header = L.T("모습 바꾸기", "Change look") };
        var fur = new MenuItem { Header = L.T("털 색", "Fur color") };
        var wear = new MenuItem { Header = L.T("옷·소품", "Outfit") };

        foreach (var s in Skins)
        {
            var item = new MenuItem { Header = L.T(s.Ko, s.En), IsCheckable = true, Tag = s.Id };
            item.Click += (_, _) => { _settings.Skin = s.Id; SaveLook(); };
            fur.Items.Add(item);
        }
        foreach (var o in Outfits)
        {
            var item = new MenuItem { Header = L.T(o.Ko, o.En), IsCheckable = true, Tag = o.Id };
            item.Click += (_, _) => { _settings.Outfit = o.Id; SaveLook(); };
            wear.Items.Add(item);
        }
        look.Items.Add(fur);
        look.Items.Add(wear);

        // 메뉴를 열 때마다 지금 고른 것에 체크
        CatMenu.Opened += (_, _) =>
        {
            foreach (MenuItem i in fur.Items) i.IsChecked = (string)i.Tag == (_settings.Skin ?? "orange");
            foreach (MenuItem i in wear.Items) i.IsChecked = (string)i.Tag == (_settings.Outfit ?? "none");
        };

        // "CPU/GPU 표시" 바로 위에 넣음
        CatMenu.Items.Insert(CatMenu.Items.IndexOf(MenuStats), look);

        // 야옹 소리 켜기/끄기
        var meow = new MenuItem { Header = L.T("야옹 소리 (가끔)", "Meow sounds (rarely)"), IsCheckable = true, IsChecked = _settings.Meow };
        meow.Click += (_, _) =>
        {
            _settings.Meow = meow.IsChecked;
            try { _settings.Save(); } catch { }
            if (meow.IsChecked) CatMeow.TryMeow(true, force: true);   // 켜면 한 번 들려줌
        };
        CatMenu.Items.Insert(CatMenu.Items.IndexOf(MenuStats), meow);

        // 창 위로 올라가기 켜기/끄기 (끄면 올라가 있던 고양이도 내려옴)
        var climb = new MenuItem { Header = L.T("창 위로 올라가기", "Climb onto windows"), IsCheckable = true, IsChecked = _settings.ClimbWindows };
        climb.Click += (_, _) =>
        {
            _settings.ClimbWindows = climb.IsChecked;
            try { _settings.Save(); } catch { }
            if (!climb.IsChecked) { if (OnTrip) AbortTrip(); if (Perched) StepOffPerch(); }
            else _nextExplore = DateTime.Now.AddSeconds(5);   // 켜면 곧 한 번 올라가 봄
        };
        CatMenu.Items.Insert(CatMenu.Items.IndexOf(MenuStats), climb);

        // 몸짓 알림 켜기/끄기 (알림 창·응답 없는 앱·오래 일함. 토큰 0)
        var alerts = new MenuItem { Header = L.T("몸짓 알림 (오류 창·멈춘 앱·휴식)", "Body-language alerts (dialogs, frozen apps, breaks)"), IsCheckable = true, IsChecked = _settings.BodyAlerts };
        alerts.Click += (_, _) =>
        {
            _settings.BodyAlerts = alerts.IsChecked;
            try { _settings.Save(); } catch { }
            if (!alerts.IsChecked && AlertActive) EndAlert(markDone: false);
        };
        CatMenu.Items.Insert(CatMenu.Items.IndexOf(MenuStats), alerts);
    }

    private void SaveLook()
    {
        _palette = BuildPalette(_settings.Skin);
        try { _settings.Save(); } catch { }
        AppSettings.Log($"[모습] 털 색 {_settings.Skin}, 옷 {_settings.Outfit}");
        _drawnFrame = null;   // 같은 프레임이라도 새 색으로 다시 그리게
        UpdateSprite();
    }
}
