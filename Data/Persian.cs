using System.Globalization;

namespace Amadban.Data;

public static class Persian
{
    private static readonly TimeZoneInfo Tehran = TimeZoneInfo.FindSystemTimeZoneById("Asia/Tehran");
    public static DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Tehran));
    public static DateOnly LocalDate(DateTime utc) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(utc.ToUniversalTime(), Tehran));

    public static string Latin(string? value) => string.Concat((value ?? "").Trim().Select(c =>
        c is >= '۰' and <= '۹' ? (char)('0' + c - '۰') :
        c is >= '٠' and <= '٩' ? (char)('0' + c - '٠') : c));

    public static DateOnly? ParseDate(string? value)
    {
        var parts = Latin(value).Split('/');
        if (parts.Length != 3 || !int.TryParse(parts[0], out var year) ||
            !int.TryParse(parts[1], out var month) || !int.TryParse(parts[2], out var day) || year is < 1300 or > 1550)
            return null;
        try { return DateOnly.FromDateTime(new PersianCalendar().ToDateTime(year, month, day, 0, 0, 0, 0)); }
        catch (ArgumentOutOfRangeException) { return null; }
    }

    public static string Date(DateOnly value)
    {
        var date = value.ToDateTime(TimeOnly.MinValue);
        var calendar = new PersianCalendar();
        return FormattableString.Invariant($"{calendar.GetYear(date):0000}/{calendar.GetMonth(date):00}/{calendar.GetDayOfMonth(date):00}");
    }

    public static DateOnly MonthEnd(DateOnly value)
    {
        var date = value.ToDateTime(TimeOnly.MinValue);
        var calendar = new PersianCalendar();
        var year = calendar.GetYear(date);
        var month = calendar.GetMonth(date);
        return DateOnly.FromDateTime(calendar.ToDateTime(year, month, calendar.GetDaysInMonth(year, month), 0, 0, 0, 0));
    }

    public static string Time(DateTime utc)
    {
        var local = TimeZoneInfo.ConvertTimeFromUtc(utc, Tehran);
        return $"{Date(DateOnly.FromDateTime(local))} {local:HH:mm}";
    }

    public static string Money(long value) => value.ToString("N0", CultureInfo.InvariantCulture);
}
