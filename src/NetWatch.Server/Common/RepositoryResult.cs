namespace NetWatch.Server.Common;

internal abstract record RepositoryResult;

internal abstract record SuccessRepositoryResult : RepositoryResult;

internal abstract record ErrorRepositoryResult : RepositoryResult;

internal sealed record RepositoryResult<T>(T Value) : SuccessRepositoryResult;
