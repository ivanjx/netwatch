using NetWatch.Core.Usage;
using NetWatch.Server.Common;

namespace NetWatch.Server.Usage;

internal sealed record UsageWriteRepositoryResult : SuccessRepositoryResult;

internal sealed record UsageRepositoryErrorResult : ErrorRepositoryResult;

internal sealed record UsageSummaryResult(UsageSummaryResponse Value) : SuccessServiceResult;

internal sealed record DeviceUsageListResult(IReadOnlyList<DeviceUsageResponse> Value) : SuccessServiceResult;

internal sealed record UsageHistoryResult(UsageHistoryResponse Value) : SuccessServiceResult;

internal sealed record InvalidUsageQueryServiceErrorResult : ErrorServiceResult;

internal sealed record UsageUnavailableServiceErrorResult : ErrorServiceResult;
