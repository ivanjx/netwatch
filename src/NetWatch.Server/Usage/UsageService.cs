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

        var todayResult = await ReadTotalsAsync(todayFromUtc, nowUtc, context, cancellationToken);
        if (todayResult is not UsageTotalsResult today)
        {
            return todayResult;
        }

        var weekResult = await ReadTotalsAsync(weekFromUtc, nowUtc, context, cancellationToken);
        if (weekResult is not UsageTotalsResult week)
        {
            return weekResult;
        }

        var monthResult = await ReadTotalsAsync(monthFromUtc, nowUtc, context, cancellationToken);
        if (monthResult is not UsageTotalsResult month)
        {
            return monthResult;
        }

        return new UsageSummaryResult(new UsageSummaryResponse(
            nowUtc,
            context.TimeZone.Id,
            context.Category,
            context.WanInterface,
            new UsagePeriodResponse(todayFromUtc, nowUtc, today.Value),
            new UsagePeriodResponse(weekFromUtc, nowUtc, week.Value),
            new UsagePeriodResponse(monthFromUtc, nowUtc, month.Value)));
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
        if (!TryParseInstant(from, defaultFrom, context.TimeZone, out var fromUtc) ||
            !TryParseInstant(to, nowUtc, context.TimeZone, out var toUtc) ||
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
            CanceledRepositoryErrorResult => new CanceledServiceErrorResult(),
            ErrorRepositoryResult => new UsageUnavailableServiceErrorResult(),
            _ => new UsageUnavailableServiceErrorResult()
        };
    }

    public async Task<ServiceResult> GetHistoryAsync(
        string? from,
        string? to,
        string? timeZoneId,
        string? grouping,
        string? deviceId,
        string? category,
        string? wanInterface,
        CancellationToken cancellationToken)
    {
        if (!TryCreateContext(timeZoneId, category, wanInterface, out var context) ||
            !TryNormalizeGrouping(grouping, out var selectedGrouping))
        {
            return new InvalidUsageQueryServiceErrorResult();
        }

        var nowUtc = _timeProvider.GetUtcNow();
        var localNow = TimeZoneInfo.ConvertTime(nowUtc, context!.TimeZone);
        var defaultFrom = ToUtc(new DateTime(localNow.Year, localNow.Month, 1), context.TimeZone);
        if (!TryParseInstant(from, defaultFrom, context.TimeZone, out var fromUtc) ||
            !TryParseInstant(to, nowUtc, context.TimeZone, out var toUtc) ||
            fromUtc >= toUtc ||
            toUtc - fromUtc > TimeSpan.FromDays(370))
        {
            return new InvalidUsageQueryServiceErrorResult();
        }

        var normalizedDeviceId = string.IsNullOrWhiteSpace(deviceId) ? null : deviceId.Trim();
        var result = await _repository.GetHourlySamplesAsync(
            new UsageQuery(fromUtc, toUtc, context.Category, context.WanInterface),
            normalizedDeviceId,
            cancellationToken);
        if (result is CanceledRepositoryErrorResult)
        {
            return new CanceledServiceErrorResult();
        }

        if (result is not RepositoryResult<IReadOnlyList<UsageHourlySample>> success)
        {
            return new UsageUnavailableServiceErrorResult();
        }

        return new UsageHistoryResult(new UsageHistoryResponse(
            fromUtc,
            toUtc,
            context.TimeZone.Id,
            selectedGrouping,
            normalizedDeviceId,
            GroupSamples(success.Value, selectedGrouping, context.TimeZone, fromUtc, toUtc)));
    }

    private async Task<ServiceResult> ReadTotalsAsync(
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc,
        QueryContext context,
        CancellationToken cancellationToken)
    {
        var result = await _repository.GetTotalsAsync(
            new UsageQuery(fromUtc, toUtc, context.Category, context.WanInterface),
            cancellationToken);
        return result switch
        {
            RepositoryResult<UsageTotalsResponse> success => new UsageTotalsResult(success.Value),
            CanceledRepositoryErrorResult => new CanceledServiceErrorResult(),
            _ => new UsageUnavailableServiceErrorResult()
        };
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

    private static bool TryParseInstant(
        string? value,
        DateTimeOffset defaultValue,
        TimeZoneInfo timeZone,
        out DateTimeOffset result)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            result = defaultValue;
            return true;
        }

        if (DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            result = ToUtc(date.ToDateTime(TimeOnly.MinValue), timeZone);
            return true;
        }

        return DateTimeOffset.TryParse(
            value,
            CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
            out result);
    }

    private static bool TryNormalizeGrouping(string? grouping, out string result)
    {
        result = string.IsNullOrWhiteSpace(grouping) ? "day" : grouping.Trim().ToLowerInvariant();
        return result is "hour" or "day" or "month";
    }

    private static IReadOnlyList<UsageHistoryPointResponse> GroupSamples(
        IReadOnlyList<UsageHourlySample> samples,
        string grouping,
        TimeZoneInfo timeZone,
        DateTimeOffset fromUtc,
        DateTimeOffset toUtc)
    {
        if (grouping == "hour")
        {
            var includeDate = toUtc - fromUtc > TimeSpan.FromDays(1);
            return samples.Select(sample =>
            {
                var local = TimeZoneInfo.ConvertTime(sample.HourStartUtc, timeZone);
                return new UsageHistoryPointResponse(
                    sample.HourStartUtc,
                    sample.HourStartUtc.AddHours(1),
                    local.ToString(includeDate ? "d MMM HH:mm" : "HH:mm", CultureInfo.InvariantCulture),
                    sample.Totals);
            }).ToArray();
        }

        return samples
            .GroupBy(sample => GetLocalPeriodStart(sample.HourStartUtc, grouping, timeZone))
            .OrderBy(group => group.Key)
            .Select(group =>
            {
                var localEnd = grouping == "day" ? group.Key.AddDays(1) : group.Key.AddMonths(1);
                return new UsageHistoryPointResponse(
                    ToUtc(group.Key, timeZone),
                    ToUtc(localEnd, timeZone),
                    group.Key.ToString(grouping == "day" ? "d MMM" : "MMM yyyy", CultureInfo.InvariantCulture),
                    Sum(group.Select(sample => sample.Totals)));
            })
            .ToArray();
    }

    private static DateTime GetLocalPeriodStart(
        DateTimeOffset hourStartUtc,
        string grouping,
        TimeZoneInfo timeZone)
    {
        var local = TimeZoneInfo.ConvertTime(hourStartUtc, timeZone);
        return grouping == "day" ?
            new DateTime(local.Year, local.Month, local.Day) :
            new DateTime(local.Year, local.Month, 1);
    }

    private static UsageTotalsResponse Sum(IEnumerable<UsageTotalsResponse> totals)
    {
        long uploadBytes = 0;
        long downloadBytes = 0;
        long uploadPackets = 0;
        long downloadPackets = 0;
        foreach (var total in totals)
        {
            uploadBytes += total.UploadBytes;
            downloadBytes += total.DownloadBytes;
            uploadPackets += total.UploadPackets;
            downloadPackets += total.DownloadPackets;
        }

        return new UsageTotalsResponse(uploadBytes, downloadBytes, uploadPackets, downloadPackets);
    }

    private static DateTimeOffset ToUtc(DateTime localDateTime, TimeZoneInfo timeZone) =>
        new(TimeZoneInfo.ConvertTimeToUtc(localDateTime, timeZone), TimeSpan.Zero);

    private sealed record QueryContext(TimeZoneInfo TimeZone, string Category, string? WanInterface);

    private sealed record UsageTotalsResult(UsageTotalsResponse Value) : SuccessServiceResult;
}
