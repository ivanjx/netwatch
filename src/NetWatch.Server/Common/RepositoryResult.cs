namespace NetWatch.Server.Common;

internal abstract record RepositoryResult;

internal abstract record SuccessRepositoryResult : RepositoryResult;

internal abstract record ErrorRepositoryResult(string Code, string Message) : RepositoryResult;

internal sealed record RepositoryResult<T>(T Value) : SuccessRepositoryResult;
