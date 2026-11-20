namespace Amadban.Data;

public static class PersianWords
{
    private static readonly string[] Units = ["", "یک", "دو", "سه", "چهار", "پنج", "شش", "هفت", "هشت", "نه",
        "ده", "یازده", "دوازده", "سیزده", "چهارده", "پانزده", "شانزده", "هفده", "هجده", "نوزده"];
    private static readonly string[] Tens = ["", "", "بیست", "سی", "چهل", "پنجاه", "شصت", "هفتاد", "هشتاد", "نود"];
    private static readonly string[] Hundreds = ["", "صد", "دویست", "سیصد", "چهارصد", "پانصد", "ششصد", "هفتصد", "هشتصد", "نهصد"];
    private static readonly string[] Levels = ["", "هزار", "میلیون", "میلیارد", "تریلیون", "کوادریلیون", "کوینتیلیون"];

    public static string Number(long value)
    {
        if (value <= 0) return "صفر";
        var sections = new List<string>();
        for (var level = 0; value > 0; level++, value /= 1000)
        {
            var group = (int)(value % 1000);
            if (group == 0) continue;
            var rest = group % 100;
            var phrases = new List<string>();
            if (group >= 100) phrases.Add(Hundreds[group / 100]);
            if (rest >= 20) phrases.Add(Tens[rest / 10]);
            var unit = rest < 20 ? Units[rest] : Units[rest % 10];
            if (unit.Length > 0) phrases.Add(unit);
            sections.Insert(0, string.Join(" و ", phrases) + (level > 0 ? " " + Levels[level] : ""));
        }
        return string.Join(" و ", sections);
    }
}
