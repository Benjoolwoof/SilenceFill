namespace SilenceFill;

internal static class InsightRanges
{
    public static DateTime StyleCutoff(int index, DateTime nowUtc) => index switch
    {
        0 => nowUtc - TimeSpan.FromSeconds(5),
        1 => nowUtc - TimeSpan.FromSeconds(10),
        2 => nowUtc - TimeSpan.FromSeconds(15),
        3 => nowUtc - TimeSpan.FromSeconds(30),
        4 => nowUtc - TimeSpan.FromMinutes(1),
        5 => nowUtc - TimeSpan.FromMinutes(2),
        6 => nowUtc - TimeSpan.FromMinutes(5),
        7 => nowUtc - TimeSpan.FromMinutes(10),
        8 => nowUtc - TimeSpan.FromMinutes(20),
        _ => DateTime.MinValue
    };

    public static DateTime HistoryCutoff(int index, DateTime nowUtc) => index switch
    {
        0 => nowUtc - TimeSpan.FromMinutes(15),
        1 => nowUtc - TimeSpan.FromHours(1),
        2 => nowUtc - TimeSpan.FromHours(6),
        3 => nowUtc.ToLocalTime().Date.ToUniversalTime(),
        4 => nowUtc - TimeSpan.FromHours(24),
        5 => nowUtc - TimeSpan.FromDays(7),
        6 => nowUtc - TimeSpan.FromDays(30),
        7 => nowUtc - TimeSpan.FromDays(90),
        _ => DateTime.MinValue
    };
}
