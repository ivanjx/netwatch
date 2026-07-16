namespace NetWatch.Server.Common;

internal abstract record RepositoryResult;

internal abstract record SuccessRepositoryResult : RepositoryResult;

internal abstract record ErrorRepositoryResult : RepositoryResult;

internal record CanceledRepositoryErrorResult : ErrorRepositoryResult;

internal sealed record RepositoryResult<T>(T Value) : SuccessRepositoryResult;
