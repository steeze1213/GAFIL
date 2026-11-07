// 간단한 질문은 Claude에게 보내지 않고 PC에서 바로 계산 (토큰 0)
// 계산식, 퍼센트, 단위 바꾸기, 다른 도시 시각, D-day·며칠 뒤, 오늘 날짜
// 확실히 알아들은 모양일 때만 답하고, 조금이라도 애매하면 null → 평소처럼 Claude에게 보냄
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace ClaudeDesk;

public static class QuickAnswer
{
    private const RegexOptions Rx = RegexOptions.IgnoreCase | RegexOptions.CultureInvariant;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    // 숫자: 1,234.5 / .5 / -3 / 3만 / 1.5억 (만·억·천은 퍼센트·계산에서만)
    private const string Num = @"[-+]?(?:\d[\d,]*(?:\.\d+)?|\.\d+)";
    private const string Money = @"\s*(?:만|억|천)?\s*(?:원|달러|불|엔|위안|유로|\$|₩)?";

    public static string TryAnswer(string input, DateTime now)
    {
        if (string.IsNullOrWhiteSpace(input) || input.Length > 60 || input.Contains('\n')) return null;
        string q = Clean(input);
        if (q.Length == 0) return null;
        return Percent(q) ?? Arithmetic(q) ?? Units(q) ?? CityTime(q, now) ?? DDay(q, now) ?? DaysFrom(q, now) ?? Today(q, now);
    }

    // 끝에 붙은 물음표·말꼬리("얼마야", "이야", "는" 등)를 떼어 냄
    private static readonly string[] Tails =
    {
        "얼마예요", "얼마에요", "얼마야", "얼마임", "얼마", "이에요", "인가요", "인가", "이야", "이지", "이냐", "예요", "에요", "일까",
        "정도", "쯤", "냐", "야", "임", "지", "요", "=", "은", "는", "이", "가",
    };
    private static string Clean(string s)
    {
        s = Regex.Replace(s.Trim(), @"\s+", " ");
        bool changed = true;
        while (changed && s.Length > 0)
        {
            changed = false;
            string t = s.TrimEnd('?', '？', '!', '.', '~', ' ');
            if (t != s) { s = t; changed = true; continue; }
            if (s.EndsWith("디데이", StringComparison.Ordinal) || s.EndsWith("하와이", StringComparison.Ordinal)) break;   // 말꼬리가 아님
            foreach (var tail in Tails)
                if (s.Length > tail.Length && s.EndsWith(tail, StringComparison.Ordinal))
                {
                    s = s[..^tail.Length].TrimEnd();
                    changed = true;
                    break;
                }
        }
        return s;
    }

    // ---------------- 숫자 ----------------

    private static bool TryNum(string s, out double v)
    {
        s = s.Replace(",", "").Replace(" ", "");
        double mul = 1;
        foreach (var w in new[] { "원", "달러", "불", "엔", "위안", "유로", "$", "₩" })
            if (s.EndsWith(w, StringComparison.Ordinal)) { s = s[..^w.Length]; break; }
        if (s.StartsWith('$') || s.StartsWith('₩')) s = s[1..];
        if (s.EndsWith('만')) { mul = 1e4; s = s[..^1]; }
        else if (s.EndsWith('억')) { mul = 1e8; s = s[..^1]; }
        else if (s.EndsWith('천')) { mul = 1e3; s = s[..^1]; }
        bool ok = double.TryParse(s, NumberStyles.Float, Inv, out v);
        v *= mul;
        return ok && !double.IsNaN(v) && !double.IsInfinity(v);
    }

    // 사람이 읽기 좋게: 천 단위 쉼표, 소수는 크기에 맞게 반올림
    public static string Fmt(double v)
    {
        if (Math.Abs(v) >= 1e15 || (v != 0 && Math.Abs(v) < 1e-6)) return v.ToString("G6", Inv);
        int dec = Math.Abs(v) >= 1000 ? 2 : Math.Abs(v) >= 1 ? 4 : 6;
        v = Math.Round(v, dec);
        if (v == 0) v = 0;   // -0 → 0
        return v.ToString("#,##0.######", Inv);
    }

    // ---------------- 퍼센트 ----------------

    private static string Percent(string q)
    {
        Match m;
        // 15% of 48000
        m = Regex.Match(q, $@"^(?<p>{Num})\s*%\s*of\s*(?<n>{Num}{Money})$", Rx);
        if (m.Success && TryNum(m.Groups["p"].Value, out double p) && TryNum(m.Groups["n"].Value, out double n))
            return $"{m.Groups["n"].Value.Trim()} × {Fmt(p)}% = {Fmt(n * p / 100)}";
        // 48000의 15% / 48000에서 15%
        m = Regex.Match(q, $@"^(?<n>{Num}{Money})\s*(?:의|에서)\s*(?<p>{Num})\s*(?:%|퍼센트|프로)$", Rx);
        if (m.Success && TryNum(m.Groups["p"].Value, out p) && TryNum(m.Groups["n"].Value, out n))
            return $"{m.Groups["n"].Value.Trim()} × {Fmt(p)}% = {Fmt(n * p / 100)}";
        // 48000원에서 15% 할인 / 48000 15% off
        m = Regex.Match(q, $@"^(?<n>{Num}{Money})\s*(?:에서|의)?\s*(?<p>{Num})\s*(?:%|퍼센트|프로)\s*(?:할인|빼면|빼기|off|discount)(?:하면|한\s*가격|된\s*가격|가격)?$", Rx);
        if (m.Success && TryNum(m.Groups["p"].Value, out p) && TryNum(m.Groups["n"].Value, out n))
            return L.T($"{m.Groups["n"].Value.Trim()}에서 {Fmt(p)}% 할인 = {Fmt(n * (1 - p / 100))} (할인액 {Fmt(n * p / 100)})",
                       $"{m.Groups["n"].Value.Trim()} − {Fmt(p)}% = {Fmt(n * (1 - p / 100))} (you save {Fmt(n * p / 100)})");
        // 12000은 48000의 몇 %
        m = Regex.Match(q, $@"^(?<a>{Num}{Money})\s*(?:은|는|이|가)?\s*(?<b>{Num}{Money})\s*의\s*몇\s*(?:%|퍼센트|프로)$", Rx);
        if (m.Success && TryNum(m.Groups["a"].Value, out double a) && TryNum(m.Groups["b"].Value, out double b) && b != 0)
            return $"{m.Groups["a"].Value.Trim()} / {m.Groups["b"].Value.Trim()} = {Fmt(a / b * 100)}%";
        // what percent of 48000 is 12000
        m = Regex.Match(q, $@"^what\s+percent(?:age)?\s+of\s+(?<b>{Num})\s+is\s+(?<a>{Num})$", Rx);
        if (m.Success && TryNum(m.Groups["a"].Value, out a) && TryNum(m.Groups["b"].Value, out b) && b != 0)
            return $"{m.Groups["a"].Value} / {m.Groups["b"].Value} = {Fmt(a / b * 100)}%";
        return null;
    }

    // ---------------- 계산식 ----------------

    private static string Arithmetic(string q)
    {
        string s = q.Replace('×', '*').Replace('÷', '/').Replace('x', '*').Replace('X', '*').Replace('＊', '*').Replace('－', '-').Replace('＋', '+');
        if (!Regex.IsMatch(s, @"^[\d\s,.+\-*/^()%]+$")) return null;
        if (!Regex.IsMatch(s, @"\d")) return null;
        // 날짜·전화번호처럼 생긴 것은 계산하지 않음
        if (Regex.IsMatch(s, @"^\s*\d{4}\s*[-./]\s*\d{1,2}\s*[-./]\s*\d{1,2}\s*$") || Regex.IsMatch(s, @"^\s*\d{2,4}-\d{3,4}-\d{4}\s*$")) return null;
        // 연산자가 하나는 있어야 함 (숫자 하나만, 음수 하나만은 계산이 아님)
        if (!Regex.IsMatch(s, @"\d\s*[+\-*/^]\s*[\d(.]|\d\s*%|\)\s*[+\-*/^]")) return null;
        var p = new Parser(s.Replace(",", "").Replace(" ", ""));
        double v;
        try { v = p.Parse(); } catch { return null; }
        if (double.IsNaN(v) || double.IsInfinity(v)) return L.T("0으로는 나눌 수 없어냥", "Can't divide by zero, meow");
        string shown = q.Replace('*', '×').Replace('/', '÷');
        return $"{shown} = {Fmt(v)}";
    }

    // 사칙연산·거듭제곱·괄호·퍼센트. "100+10%"는 계산기처럼 110
    private sealed class Parser
    {
        private readonly string _s;
        private int _i;
        public Parser(string s) => _s = s;

        public double Parse()
        {
            double v = Expr();
            if (_i != _s.Length) throw new FormatException();
            return v;
        }
        private char Peek => _i < _s.Length ? _s[_i] : '\0';

        private double Expr()
        {
            double v = Term(out _);
            while (Peek is '+' or '-')
            {
                char op = _s[_i++];
                double r = Term(out bool pct);
                if (pct) v = op == '+' ? v * (1 + r) : v * (1 - r);
                else v = op == '+' ? v + r : v - r;
            }
            return v;
        }
        private double Term(out bool pct)
        {
            double v = Power(out pct);
            while (Peek is '*' or '/')
            {
                char op = _s[_i++];
                double r = Power(out _);
                v = op == '*' ? v * r : v / r;
                pct = false;
            }
            return v;
        }
        private double Power(out bool pct)
        {
            double v = Unary(out pct);
            if (Peek == '^') { _i++; v = Math.Pow(v, Power(out _)); pct = false; }
            return v;
        }
        private double Unary(out bool pct)
        {
            if (Peek == '-') { _i++; return -Unary(out pct); }
            if (Peek == '+') { _i++; return Unary(out pct); }
            double v = Primary();
            pct = false;
            while (Peek == '%') { _i++; v /= 100; pct = true; }
            return v;
        }
        private double Primary()
        {
            if (Peek == '(')
            {
                _i++;
                double v = Expr();
                if (Peek != ')') throw new FormatException();
                _i++;
                return v;
            }
            int start = _i;
            while (char.IsDigit(Peek) || Peek == '.') _i++;
            if (start == _i) throw new FormatException();
            return double.Parse(_s[start.._i], NumberStyles.Float, Inv);
        }
    }

    // ---------------- 단위 바꾸기 ----------------

    private enum Dim { Length, Mass, Area, Volume, Speed, Temp }
    // 이름(소문자, 띄어쓰기 없음) → (종류, 기준 단위로 곱할 값). 온도는 따로 계산
    private static readonly Dictionary<string, (Dim D, double F)> Unit = BuildUnits();
    private static readonly string[] UnitNames = Unit.Keys.OrderByDescending(k => k.Length).ToArray();

    private static Dictionary<string, (Dim, double)> BuildUnits()
    {
        var u = new Dictionary<string, (Dim, double)>();
        void Add(Dim d, double f, params string[] names) { foreach (var n in names) u[n] = (d, f); }
        Add(Dim.Length, 0.001, "mm", "밀리미터", "밀리");
        Add(Dim.Length, 0.01, "cm", "센티미터", "센티", "센치");
        Add(Dim.Length, 1, "m", "미터");
        Add(Dim.Length, 1000, "km", "킬로미터", "키로미터");
        Add(Dim.Length, 0.0254, "in", "inch", "inches", "인치");
        Add(Dim.Length, 0.3048, "ft", "feet", "foot", "피트");
        Add(Dim.Length, 0.9144, "yd", "yard", "yards", "야드");
        Add(Dim.Length, 1609.344, "mi", "mile", "miles", "마일");
        Add(Dim.Mass, 1e-6, "mg", "밀리그램");
        Add(Dim.Mass, 0.001, "g", "그램");
        Add(Dim.Mass, 1, "kg", "킬로그램", "킬로", "키로");
        Add(Dim.Mass, 1000, "t", "ton", "톤");
        Add(Dim.Mass, 0.45359237, "lb", "lbs", "pound", "pounds", "파운드");
        Add(Dim.Mass, 0.028349523125, "oz", "ounce", "ounces", "온스");
        Add(Dim.Mass, 0.6, "근");
        Add(Dim.Mass, 0.00375, "돈");
        Add(Dim.Area, 1, "m2", "m²", "㎡", "제곱미터", "평방미터");
        Add(Dim.Area, 400.0 / 121, "평");
        Add(Dim.Area, 0.09290304, "ft2", "ft²", "sqft", "제곱피트");
        Add(Dim.Area, 4046.8564224, "acre", "acres", "에이커");
        Add(Dim.Area, 10000, "ha", "헥타르");
        Add(Dim.Area, 1e6, "km2", "km²", "㎢", "제곱킬로미터");
        Add(Dim.Volume, 0.001, "ml", "밀리리터", "cc");
        Add(Dim.Volume, 1, "l", "리터");
        Add(Dim.Volume, 3.785411784, "gal", "gallon", "gallons", "갤런");
        Add(Dim.Volume, 0.0295735295625, "floz", "fl.oz");
        Add(Dim.Speed, 1, "km/h", "kmh", "kph", "킬로미터/시");
        Add(Dim.Speed, 1.609344, "mph");
        Add(Dim.Speed, 3.6, "m/s", "mps");
        Add(Dim.Speed, 1.852, "knot", "knots", "kn", "노트");
        Add(Dim.Temp, 0, "c", "°c", "℃", "섭씨", "도");
        Add(Dim.Temp, 1, "f", "°f", "℉", "화씨");
        Add(Dim.Temp, 2, "k", "켈빈");
        return u;
    }

    // 3.5인치 cm / 3.5인치는 몇 cm / 30평을 제곱미터로 / 5 ft in cm / 섭씨 30도 화씨로
    private static string Units(string q)
    {
        string s = q.ToLowerInvariant();
        s = Regex.Replace(s, $@"^섭씨\s*({Num})\s*도?", "$1c");
        s = Regex.Replace(s, $@"^화씨\s*({Num})\s*도?", "$1f");
        var m = Regex.Match(s, $@"^\s*(?<n>{Num})\s*(?<rest>.+)$");
        if (!m.Success || !TryNum(m.Groups["n"].Value, out double v)) return null;
        string rest = m.Groups["rest"].Value.Replace(" ", "");

        foreach (var a in UnitNames)
        {
            if (!rest.StartsWith(a, StringComparison.Ordinal)) continue;
            string b = UnitB(rest[a.Length..]);
            if (b == null) continue;
            var (da, fa) = Unit[a];
            var (db, fb) = Unit[b];
            if (da != db || a == b) continue;
            double r = da == Dim.Temp ? Temp(v, (int)fa, (int)fb) : v * fa / fb;
            return $"{Fmt(v)}{Show(a)} = {Fmt(r)}{Show(b)}";
        }
        return null;
    }

    // 보여줄 단위 이름: 온도는 기호로, 나머지는 쓴 그대로 (영문 단위는 한 칸 띄움)
    private static string Show(string unit)
    {
        if (Unit[unit].D == Dim.Temp) return Unit[unit].F switch { 1 => "℉", 2 => "K", _ => "℃" };
        return Regex.IsMatch(unit, "^[a-z]") ? " " + unit : unit;
    }

    // 두 번째 단위: 사이에 낀 조사("는", "을", "to", "in", "몇")와 끝의 "로/으로"를 떼고 정확히 맞아야 함
    private static string UnitB(string rest)
    {
        foreach (var sep in new[] { "", "은", "는", "을", "를", "이", "가", "=", "→", "->", "to", "into", "in" })
        {
            if (!rest.StartsWith(sep, StringComparison.Ordinal)) continue;
            string r = rest[sep.Length..];
            if (r.StartsWith("몇", StringComparison.Ordinal)) r = r[1..];
            foreach (var end in new[] { "", "로", "으로", "로는", "으로는", "로하면", "으로하면", "단위로" })
            {
                if (!r.EndsWith(end, StringComparison.Ordinal)) continue;
                string name = r[..^end.Length];
                if (name.Length > 0 && Unit.ContainsKey(name)) return name;
            }
        }
        return null;
    }

    // 온도: 0=섭씨, 1=화씨, 2=켈빈
    private static double Temp(double v, int from, int to)
    {
        double c = from switch { 1 => (v - 32) * 5 / 9, 2 => v - 273.15, _ => v };
        return to switch { 1 => c * 9 / 5 + 32, 2 => c + 273.15, _ => c };
    }

    // ---------------- 다른 도시 시각 ----------------

    private static readonly Dictionary<string, (string Ko, string En, string Tz)> Cities = BuildCities();
    private static Dictionary<string, (string, string, string)> BuildCities()
    {
        var c = new Dictionary<string, (string, string, string)>();
        void Add(string ko, string en, string tz, params string[] names)
        {
            c[ko] = (ko, en, tz);
            c[en.ToLowerInvariant().Replace(" ", "")] = (ko, en, tz);
            foreach (var n in names) c[n] = (ko, en, tz);
        }
        Add("서울", "Seoul", "Korea Standard Time", "한국", "korea");
        Add("도쿄", "Tokyo", "Tokyo Standard Time", "일본", "japan", "오사카", "osaka");
        Add("베이징", "Beijing", "China Standard Time", "북경", "중국", "china", "상하이", "shanghai", "홍콩", "hongkong", "타이베이", "taipei", "대만", "taiwan");
        Add("싱가포르", "Singapore", "Singapore Standard Time", "쿠알라룸푸르", "kualalumpur", "마닐라", "manila", "필리핀");
        Add("방콕", "Bangkok", "SE Asia Standard Time", "태국", "thailand", "하노이", "hanoi", "호치민", "hochiminh", "베트남", "vietnam", "다낭", "자카르타", "jakarta");
        Add("뉴델리", "New Delhi", "India Standard Time", "인도", "india", "델리", "delhi", "뭄바이", "mumbai");
        Add("두바이", "Dubai", "Arabian Standard Time", "dubai", "아부다비");
        Add("모스크바", "Moscow", "Russian Standard Time", "러시아");
        Add("이스탄불", "Istanbul", "Turkey Standard Time", "터키", "튀르키예", "turkey");
        Add("카이로", "Cairo", "Egypt Standard Time", "이집트", "egypt");
        Add("런던", "London", "GMT Standard Time", "영국", "uk", "england", "더블린", "dublin", "리스본", "lisbon");
        Add("파리", "Paris", "Romance Standard Time", "프랑스", "france", "마드리드", "madrid", "스페인", "spain", "브뤼셀", "brussels");
        Add("베를린", "Berlin", "W. Europe Standard Time", "독일", "germany", "로마", "rome", "이탈리아", "italy", "암스테르담", "amsterdam", "빈", "vienna", "취리히", "zurich", "스위스", "프라하", "prague");
        Add("뉴욕", "New York", "Eastern Standard Time", "ny", "nyc", "워싱턴", "washington", "보스턴", "boston", "토론토", "toronto", "마이애미", "miami", "애틀랜타", "atlanta", "미국동부");
        Add("시카고", "Chicago", "Central Standard Time", "댈러스", "dallas", "휴스턴", "houston");
        Add("덴버", "Denver", "Mountain Standard Time");
        Add("로스앤젤레스", "Los Angeles", "Pacific Standard Time", "la", "엘에이", "샌프란시스코", "sanfrancisco", "sf", "시애틀", "seattle", "밴쿠버", "vancouver", "라스베이거스", "lasvegas", "실리콘밸리", "미국서부");
        Add("하와이", "Hawaii", "Hawaiian Standard Time", "호놀룰루", "honolulu");
        Add("시드니", "Sydney", "AUS Eastern Standard Time", "멜버른", "melbourne", "호주", "australia");
        Add("오클랜드", "Auckland", "New Zealand Standard Time", "뉴질랜드", "newzealand");
        Add("상파울루", "São Paulo", "E. South America Standard Time", "saopaulo", "브라질", "brazil");
        Add("멕시코시티", "Mexico City", "Central Standard Time (Mexico)", "mexicocity", "멕시코", "mexico");
        Add("괌", "Guam", "West Pacific Standard Time", "guam", "사이판", "saipan");
        return c;
    }

    private static string CityTime(string q, DateTime now)
    {
        string s = q.ToLowerInvariant();
        Match m = Regex.Match(s, @"^(?<c>.+?)\s*(?:은|는|의|에서는|에선)?\s*(?:지금|현재)?\s*(?:몇\s*시|시간|시각)$", Rx);
        if (!m.Success) m = Regex.Match(s, @"^(?:what(?:'s|\s+is)?\s+)?(?:the\s+)?(?:current\s+)?time\s+(?:is\s+it\s+)?(?:now\s+)?in\s+(?<c>.+)$", Rx);
        if (!m.Success) m = Regex.Match(s, @"^(?<c>.+?)\s+time(?:\s+now)?$", Rx);
        string city = m.Success ? m.Groups["c"].Value.Replace(" ", "").Trim() : null;

        if (city == null)
        {
            if (!Regex.IsMatch(s, @"^(?:지금|현재)\s*(?:몇\s*시|시간|시각)$|^what\s+time\s+is\s+it(?:\s+now)?$", Rx)) return null;
            return L.T($"지금 {KoTime(now)}", $"It's {EnTime(now)}");
        }
        if (city is "지금" or "현재" or "now") return L.T($"지금 {KoTime(now)}", $"It's {EnTime(now)}");
        if (!Cities.TryGetValue(city, out var info)) return null;

        TimeZoneInfo tz;
        try { tz = TimeZoneInfo.FindSystemTimeZoneById(info.Tz); } catch { return null; }
        var there = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.Local, tz);
        double diff = Math.Round((there - now).TotalHours * 2) / 2;
        string dKo = diff == 0 ? "여기와 같은 시각" : diff > 0 ? $"여기보다 {Fmt(diff)}시간 빨라" : $"여기보다 {Fmt(-diff)}시간 느려";
        string dEn = diff == 0 ? "same as here" : diff > 0 ? $"{Fmt(diff)}h ahead of here" : $"{Fmt(-diff)}h behind here";
        return L.T($"{info.Ko}: {KoTime(there)}\n({dKo})", $"{info.En}: {EnTime(there)}\n({dEn})");
    }

    private static readonly string[] KoDays = { "일", "월", "화", "수", "목", "금", "토" };
    private static string KoDate(DateTime d) => $"{d.Month}월 {d.Day}일({KoDays[(int)d.DayOfWeek]})";
    private static string KoTime(DateTime d) =>
        $"{KoDate(d)} {(d.Hour < 12 ? "오전" : "오후")} {(d.Hour % 12 == 0 ? 12 : d.Hour % 12)}:{d.Minute:00}";
    private static string EnDate(DateTime d) => d.ToString("ddd, MMM d", Inv);
    private static string EnTime(DateTime d) => d.ToString("h:mm tt", Inv) + ", " + EnDate(d);

    // ---------------- D-day ----------------

    private const string DatePat = @"(?:(?<y>\d{4})\s*(?:[-./]|년)\s*)?(?<m>\d{1,2})\s*(?:[-./]|월)\s*(?<d>\d{1,2})\s*일?";

    private static string DDay(string q, DateTime now)
    {
        string s = q.ToLowerInvariant();
        string named = @"(?<name>크리스마스|성탄절|christmas|xmas|새해|신정|new\s*year|new\s*year's\s*day)";
        string target = $@"(?:{DatePat}|{named})";
        Match m = Regex.Match(s, $@"^(?:d-?day|디데이)\s*{target}$", Rx);
        if (!m.Success) m = Regex.Match(s, $@"^{target}\s*(?:까지|디데이|d-?day)\s*(?:며칠|몇\s*일|몇일|얼마나)?\s*(?:남았어|남았나|남았지|남았|남음|남은\s*날|남은\s*날짜)?$", Rx);
        if (!m.Success) m = Regex.Match(s, $@"^(?:how\s+many\s+)?days\s+(?:left\s+)?(?:until|till|to|before)\s+{target}$", Rx);
        if (!m.Success) return null;

        int y, mo, d;
        bool yearGiven = m.Groups["y"].Success;
        if (m.Groups["name"].Success)
        {
            string n = m.Groups["name"].Value;
            (mo, d) = n is "크리스마스" or "성탄절" or "christmas" or "xmas" ? (12, 25) : (1, 1);
            y = now.Year;
        }
        else
        {
            mo = int.Parse(m.Groups["m"].Value);
            d = int.Parse(m.Groups["d"].Value);
            y = yearGiven ? int.Parse(m.Groups["y"].Value) : now.Year;
        }
        if (mo < 1 || mo > 12 || d < 1 || d > DateTime.DaysInMonth(Math.Clamp(y, 1, 9999), mo)) return null;
        var date = new DateTime(y, mo, d);
        if (!yearGiven && date < now.Date) date = date.AddYears(1);   // 올해 이미 지났으면 내년
        int days = (date - now.Date).Days;

        string ko = days == 0 ? $"{KoDate(date)}: 바로 오늘이야! (D-Day)"
                  : days > 0 ? $"{date.Year}년 {KoDate(date)}까지 {days:#,0}일 남았어 (D-{days})"
                  : $"{date.Year}년 {KoDate(date)}부터 {-days:#,0}일 지났어 (D+{-days})";
        string en = days == 0 ? $"{EnDate(date)}: that's today! (D-Day)"
                  : days > 0 ? $"{days:#,0} days until {EnDate(date)}, {date.Year} (D-{days})"
                  : $"{-days:#,0} days since {EnDate(date)}, {date.Year} (D+{-days})";
        return L.T(ko, en);
    }

    // 100일 뒤 / 30일 전 / 100 days from now
    private static string DaysFrom(string q, DateTime now)
    {
        string s = q.ToLowerInvariant();
        Match m = Regex.Match(s, @"^(?:오늘\s*(?:부터|에서)?\s*)?(?<n>\d{1,5})\s*일\s*(?<w>뒤|후|전)(?:은|는|이|가|에)?\s*(?:며칠|몇\s*일|몇일|언제|무슨\s*요일|날짜|몇\s*월\s*며칠)?$", Rx);
        bool ago;
        if (m.Success) ago = m.Groups["w"].Value == "전";
        else
        {
            m = Regex.Match(s, @"^(?:what(?:'s|\s+is)?\s+(?:the\s+date\s+)?)?(?<n>\d{1,5})\s*days?\s+(?<w>from\s+now|from\s+today|later|after\s+today|ago)$", Rx);
            if (!m.Success) return null;
            ago = m.Groups["w"].Value == "ago";
        }
        int n = int.Parse(m.Groups["n"].Value);
        var date = now.Date.AddDays(ago ? -n : n);
        return L.T($"{n:#,0}일 {(ago ? "전은" : "뒤는")} {date.Year}년 {KoDate(date)}", $"{n:#,0} days {(ago ? "ago" : "from now")}: {EnDate(date)}, {date.Year}");
    }

    // 오늘 며칠 / 무슨 요일
    private static string Today(string q, DateTime now)
    {
        string s = q.ToLowerInvariant();
        bool ko = Regex.IsMatch(s, @"^(?:오늘)\s*(?:은|는|이)?\s*(?:며칠|몇\s*일|몇일|무슨\s*요일|날짜|몇\s*월\s*며칠|몇\s*월\s*몇\s*일)$|^(?:오늘\s*)?무슨\s*요일$", Rx);
        bool en = Regex.IsMatch(s, @"^(?:what(?:'s|\s+is)?\s+)?(?:the\s+)?(?:today's\s+)?(?:date|day)(?:\s+(?:is\s+it\s+)?today)?$|^what\s+day\s+is\s+it(?:\s+today)?$", Rx);
        if (!ko && !en) return null;
        return L.T($"오늘은 {now.Year}년 {KoDate(now)}", $"Today is {now.ToString("dddd, MMMM d, yyyy", Inv)}");
    }
}
