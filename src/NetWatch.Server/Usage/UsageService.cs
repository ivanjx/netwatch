using System.Globalization;

using NetWatch.Core.Usage;
using NetWatch.Server.Common;
using NetWatch.Server.Configuration;

namespace NetWatch.Server.Usage;

internal sealed class UsageService(
    UsageRepository _repository,
    NetWatchOptions _options,
    TimeProvider _timeProvider)
{
    private static readonly HashSet<string> _categories = new(StringComparer.OrdinalIgnoreCase)
    {
        "Internet",
        "InterVlan",
        "Vpn",
        "InternalRouted",
        "Unclassified"
    };

    public async Task<ServiceResult> GetSummaryAsync(
        string? timeZoneId,
        string? category,
        string? wanInterface,
        CancellationToken cancellationToken)
    {
        if (!TryCreateContext(timeZoneId, category, wanInterface, out var context))
        {
            return new InvalidUsageQueryServiceErrorResult();
        }

        var nowUtc = _timeProvider.GetUtcNow();
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, context!.TimeZone);
        var todayLocal = new DateTime(localNow.Year, localNow.Month, localNow.Day);
        var daysSinceMonday = ((int)localNow.DayOfWeek + 6) % 7;
        var weekLocal = todayLocal.AddDays(-daysSinceMonday);
        var monthLocal = new DateTime(localNow.Year, localNow.Month, 1);
        var todayFromUtc = ToUtc(todayLocal, context.TimeZone);
        var weekFromUtc = ToUtc(weekLocal, context.TimeZone);
        var monthFromUtc = ToUtc(monthLocal, context.TimeZone);

        var today = await ReadTotalsAsync(todayFromUtc, nowUtc, context, cancellationToken);
        var week = await ReadTotalsAsync(weekFromUtc, nowUtc, context, cancellationToken);
        var month = await ReadTotalsAsync(monthFromUtc, nowUtc, context, cancellationToken);
        if (today is null || week is null || month is null)
        {
            return new UsageUnavailableServiceErrorResult();
        }

        return new UsageSummaryResult(new UsageSummaryResponse(
            nowUtc,
            context.TimeZone.Id,
            context.Category,
            context.WanInterface,
            new UsagePeriodResponse(todayFromUtc, nowUtc, today),
            new UsagePeriodResponse(weekFromUtc, nowUtc, week),
            new UsagePeriodResponse(monthFromUtc, nowUtc, month)));
    }

    public async Task<ServiceResult> GetDevicesAsync(
        string? from,
        string? to,
        string? timeZoneId,
        string? category,
        string? wanInterface,
        CancellationToken cancellationToken)
    {
        if (!TryCreateContext(timeZoneId, category, wanInterface, out var context))
        {
            return new InvalidUsageQueryServiceErrorResult();
        }

        var nowUtc = _timeProvider.GetUtcNow();
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, context!.TimeZone);
        var defaultFrom = ToUtc(new DateTime(localNow.Year, localNow.Month, localNow.Day), context.TimeZone);
        if (!TryParseInstant(from, defaultFrom, out var fromUtc) ||
            !TryParseInstant(to, nowUtc, out var toUtc) ||
            fromUtc >= toUtc)
        {
            return new InvalidUsageQueryServiceErrorResult();
        }

        var result = await _repository.GetDeviceTotalsAsync(
            new UsageQuery(fromUtc, toUtc, context.Category, context.WanInterface),
            cancellationToken);
        return result switch
        {
            RepositoryResult<IReadOnlyList<DeviceUsageResponse>> success => new DeviceUsageListResult(success.Value),
            ErrorRepositoryResult => new UsageUnavailableServiceErrorResult(),
            _ => new UsageUnavailableServiceErrorResult()
        };
    }

    private async Task<UsageTotalsResponse?> ReadTotalsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        QueryContext context,
        CancellationToken cancellationToken)
    {
        var result = await _repository.GetTotalsAsync(
            new UsageQuery(fromUtc, toUtc, context.Category, context.WanInterface),
            cancellationToken);
        return result is RepositoryResult<UsageTotalsResponse> success ? success.Value : null;
    }

    private bool TryCreateContext(
        string? timeZoneId,
        string? category,
        string? wanInterface,
        out QueryContext? context)
    {
        context = null;
        var selectedCategory = string.IsNullOrWhiteSpace(category) ? UsageCategories.Internet : category;
        if (!_categories.TryGetValue(selectedCategory, out selectedCategory))
        {
            return false;
        }

        TimeZoneInfo timeZone;
        try
        {
            timeZone = string.IsNullOrWhiteSpace(timeZoneId) ?
                _options.ReportingTimeZone :
                TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
        }
        catch (TimeZoneNotFoundException)
        {
            return false;
        }
        catch (InvalidTimeZoneException)
        {
            return false;
        }

        context = new QueryContext(
            timeZone,
            selectedCategory,
            string.IsNullOrWhiteSpace(wanInterface) ? null : wanInterface);
        return true;
    }

    private static bool TryParseInstant(string? value, DateTimeOffset defaultValue, out DateTimeOffset result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = defaultValue;
            return true;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out result);
    }

    private static DateTimeOffset ToUtc(DateTime localDateTime, TimeZoneInfo timeZone) =>
        new(TimeZoneInfo.ConvertTimeToUtc(localDateTime, timeZone), TimeSpan.Zero);

    private sealed record QueryContext(TimeZoneInfo TimeZone, string Category, string? WanInterface);
}
