namespace NetWatch.Core.Diagnostics;

public sealed record SystemStatusResponse(
    string Product,
    string State,
    string Database,
    long SchemaVersion,
    string NetFlowEndpoint,
    bool MikroTikConfigured,
    string ReportingTimezone,
    DateTimeOffset StartedAtUtc);
