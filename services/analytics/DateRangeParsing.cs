namespace Telumera.Services.Analytics.Api;

public readonly record struct DateRange(DateOnly From, DateOnly To);

/// <summary>Shared `from`/`to` query-string handling for every query endpoint in AnalyticsQueryEndpoints.cs, plus the "Implement comparison periods" subtask's core helper.</summary>
public static class DateRangeParsing
{
    private const int DefaultSpanDays = 7;
    private const int MaxSpanDays = 366;

    /// <summary>Defaults to the last 7 UTC days when `from`/`to` are omitted. Returns null (caller should return a 400) if the range is invalid or exceeds MaxSpanDays — bounds query cost, part of this epic's "protect ClickHouse" goal.</summary>
    public static DateRange? Parse(HttpContext httpContext)
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        var fromRaw = httpContext.Request.Query["from"].ToString();
        var toRaw = httpContext.Request.Query["to"].ToString();

        var to = string.IsNullOrEmpty(toRaw) ? today : ParseOrNull(toRaw);
        var from = string.IsNullOrEmpty(fromRaw) ? to?.AddDays(-(DefaultSpanDays - 1)) : ParseOrNull(fromRaw);

        if (from is null || to is null || from > to)
        {
            return null;
        }

        var spanDays = to.Value.DayNumber - from.Value.DayNumber + 1;
        return spanDays > MaxSpanDays ? null : new DateRange(from.Value, to.Value);
    }

    /// <summary>The immediately-preceding period of equal length — e.g. Mar 8-14 -> Mar 1-7. Used by ?compare=true on the overview endpoint (definitions doc has no comparison-period rule of its own; "immediately preceding, same length" is the least surprising default).</summary>
    public static DateRange GetPreviousPeriod(DateRange range)
    {
        var spanDays = range.To.DayNumber - range.From.DayNumber + 1;
        return new DateRange(range.From.AddDays(-spanDays), range.From.AddDays(-1));
    }

    private static DateOnly? ParseOrNull(string value) => DateOnly.TryParse(value, out var parsed) ? parsed : null;
}
