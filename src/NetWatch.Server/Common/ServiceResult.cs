namespace NetWatch.Server.Common;

internal abstract record ServiceResult;

internal abstract record SuccessServiceResult : ServiceResult;

internal abstract record ErrorServiceResult : ServiceResult;
