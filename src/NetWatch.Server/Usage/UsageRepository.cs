using System.Net;

using Microsoft.Data.Sqlite;

using NetWatch.Core.Usage;
using NetWatch.Server.Common;
using NetWatch.Server.Data;
using NetWatch.Server.FlowCollection;

namespace NetWatch.Server.Usage;

internal sealed class UsageRepository(
    SqliteConnectionFactory _connectionFactory,
    ILogger<UsageRepository> _logger)
{
    public async Task<RepositoryResult> ReadAssignmentsAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT device_id, ip_address, valid_from_utc, valid_to_utc
                FROM ip_assignments
                ORDER BY CASE WHEN instr(ip_address, '/') = 0 THEN 0 ELSE 1 END,
                         length(ip_address) DESC;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var assignments = new List<DeviceAssignment>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var value = reader.GetString(1);
                IPAddress? address = null;
                IPNetwork? network = null;
                if (IPAddress.TryParse(value, out var parsedAddress))
                {
                    address = parsedAddress;
                }
                else if (IPNetwork.TryParse(value, out var parsedNetwork))
                {
                    network = parsedNetwork;
                }
                else
                {
                    continue;
                }

                assignments.Add(new DeviceAssignment(
                    reader.GetString(0),
                    address,
                    network,
                    DateTimeOffset.Parse(reader.GetString(2)),
                    reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3))));
            }

            return new RepositoryResult<IReadOnlyList<DeviceAssignment>>(assignments);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return LogError(exception, "Failed to read device assignments for usage attribution");
        }
    }

    public async Task<RepositoryResult> FlushAsync(
        UsageBatch batch,
        NetFlowDiagnosticSnapshot collector,
        DateTimeOffset flushedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            foreach (var delta in batch.Attributed)
            {
                await UpsertUsageAsync(connection, (SqliteTransaction)transaction, delta, cancellationToken);
            }

            foreach (var delta in batch.Unresolved)
            {
                await UpsertUnresolvedAsync(connection, (SqliteTransaction)transaction, delta, cancellationToken);
            }

            await ExecuteAsync(
                connection,
                (SqliteTransaction)transaction,
                """
                UPDATE collector_state SET
                    exporter_address = $exporter,
                    router_uptime_milliseconds = $uptime,
                    netflow_sequence = $sequence,
                    last_flow_at_utc = $lastFlow,
                    last_flush_at_utc = $now
                WHERE singleton_id = 1;
                """,
                [
                    new("$exporter", (object?)collector.ExporterAddress ?? DBNull.Value),
                    new("$uptime", collector.RouterUptimeMilliseconds is { } uptime ? (long)uptime : DBNull.Value),
                    new("$sequence", collector.NetFlowSequence is { } sequence ? (long)sequence : DBNull.Value),
                    new("$lastFlow", (object?)collector.LastFlowAtUtc?.ToString("O") ?? DBNull.Value),
                    new("$now", flushedAtUtc.ToString("O"))
                ],
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new UsageWriteRepositoryResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return LogError(exception, "Failed to flush usage aggregates");
        }
    }

    public async Task<RepositoryResult> ReadUnresolvedAsync(
        int limit,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id, ip_address, hour_start_utc, first_seen_at_utc, last_seen_at_utc,
                       category, wan_interface, upload_bytes, download_bytes,
                       upload_packets, download_packets
                FROM unresolved_usage
                ORDER BY last_seen_at_utc
                LIMIT $limit;
                """;
            command.Parameters.AddWithValue("$limit", limit);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var records = new List<UnresolvedUsageRecord>();
            while (await reader.ReadAsync(cancellationToken))
            {
                records.Add(new UnresolvedUsageRecord(
                    reader.GetInt64(0),
                    IPAddress.Parse(reader.GetString(1)),
                    DateTimeOffset.Parse(reader.GetString(2)),
                    DateTimeOffset.Parse(reader.GetString(3)),
                    DateTimeOffset.Parse(reader.GetString(4)),
                    reader.GetString(5),
                    reader.GetString(6),
                    reader.GetInt64(7),
                    reader.GetInt64(8),
                    reader.GetInt64(9),
                    reader.GetInt64(10)));
            }

            return new RepositoryResult<IReadOnlyList<UnresolvedUsageRecord>>(records);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return LogError(exception, "Failed to read unresolved usage");
        }
    }

    public async Task<RepositoryResult> StoreReconciledAsync(
        IReadOnlyList<ReconciledUsage> reconciled,
        DateTimeOffset reconciledAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            foreach (var item in reconciled)
            {
                await UpsertUsageAsync(connection, (SqliteTransaction)transaction, item.Delta, cancellationToken);
                await ExecuteAsync(
                    connection,
                    (SqliteTransaction)transaction,
                    "DELETE FROM unresolved_usage WHERE id = $id;",
                    [new("$id", item.UnresolvedId)],
                    cancellationToken);
            }

            await ExecuteAsync(
                connection,
                (SqliteTransaction)transaction,
                "UPDATE collector_state SET last_reconciliation_at_utc = $now WHERE singleton_id = 1;",
                [new("$now", reconciledAtUtc.ToString("O"))],
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new UsageWriteRepositoryResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return LogError(exception, "Failed to reconcile unresolved usage");
        }
    }

    public async Task<RepositoryResult> GetTotalsAsync(
        UsageQuery query,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"""
                SELECT COALESCE(SUM(upload_bytes), 0), COALESCE(SUM(download_bytes), 0),
                       COALESCE(SUM(upload_packets), 0), COALESCE(SUM(download_packets), 0)
                FROM usage_hourly
                WHERE hour_start_utc >= $from AND hour_start_utc < $to
                  AND category = $category
                  {(query.WanInterface is null ? string.Empty : "AND wan_interface = $wanInterface")};
                """;
            AddQueryParameters(command, query);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            return new RepositoryResult<UsageTotalsResponse>(ReadTotals(reader, 0));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return LogError(exception, "Failed to read usage totals");
        }
    }

    public async Task<RepositoryResult> GetDeviceTotalsAsync(
        UsageQuery query,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                $"""
                SELECT d.id,
                       COALESCE(d.friendly_name, d.discovered_name, d.mac_address, d.current_ip_address, 'Unnamed device'),
                       COALESCE(SUM(u.upload_bytes), 0), COALESCE(SUM(u.download_bytes), 0),
                       COALESCE(SUM(u.upload_packets), 0), COALESCE(SUM(u.download_packets), 0)
                FROM devices d
                LEFT JOIN usage_hourly u ON u.device_id = d.id
                    AND u.hour_start_utc >= $from AND u.hour_start_utc < $to
                    AND u.category = $category
                    {(query.WanInterface is null ? string.Empty : "AND u.wan_interface = $wanInterface")}
                WHERE d.archived_at_utc IS NULL
                GROUP BY d.id
                ORDER BY SUM(COALESCE(u.upload_bytes, 0) + COALESCE(u.download_bytes, 0)) DESC,
                         COALESCE(d.friendly_name, d.discovered_name, d.mac_address, d.current_ip_address);
                """;
            AddQueryParameters(command, query);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var devices = new List<DeviceUsageResponse>();
            while (await reader.ReadAsync(cancellationToken))
            {
                devices.Add(new DeviceUsageResponse(
                    reader.GetString(0),
                    reader.GetString(1),
                    ReadTotals(reader, 2)));
            }

            return new RepositoryResult<IReadOnlyList<DeviceUsageResponse>>(devices);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return LogError(exception, "Failed to read per-device usage totals");
        }
    }

    public async Task<RepositoryResult> GetDiagnosticStateAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT COUNT(u.id), COALESCE(SUM(upload_bytes + download_bytes), 0),
                       c.last_flush_at_utc, c.last_reconciliation_at_utc
                FROM collector_state c
                LEFT JOIN unresolved_usage u ON 1 = 1
                WHERE c.singleton_id = 1;
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            return new RepositoryResult<UsageDiagnosticState>(new(
                reader.GetInt64(0),
                reader.GetInt64(1),
                reader.IsDBNull(2) ? null : DateTimeOffset.Parse(reader.GetString(2)),
                reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3))));
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception exception)
        {
            return LogError(exception, "Failed to read usage diagnostics");
        }
    }

    private async Task UpsertUsageAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        UsageDelta delta,
        CancellationToken cancellationToken)
    {
        var parameters = CreateUsageParameters(delta);
        await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO usage_hourly (
                device_id, hour_start_utc, category, wan_interface,
                upload_bytes, download_bytes, upload_packets, download_packets)
            VALUES (
                $deviceId, $hour, $category, $wanInterface,
                $uploadBytes, $downloadBytes, $uploadPackets, $downloadPackets)
            ON CONFLICT(device_id, hour_start_utc, category, wan_interface) DO UPDATE SET
                upload_bytes = usage_hourly.upload_bytes + excluded.upload_bytes,
                download_bytes = usage_hourly.download_bytes + excluded.download_bytes,
                upload_packets = usage_hourly.upload_packets + excluded.upload_packets,
                download_packets = usage_hourly.download_packets + excluded.download_packets;
            """,
            parameters,
            cancellationToken);

    }

    private static async Task UpsertUnresolvedAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        UnresolvedUsageDelta delta,
        CancellationToken cancellationToken) =>
        await ExecuteAsync(
            connection,
            transaction,
            """
            INSERT INTO unresolved_usage (
                ip_address, hour_start_utc, first_seen_at_utc, last_seen_at_utc,
                category, wan_interface, upload_bytes, download_bytes,
                upload_packets, download_packets)
            VALUES (
                $address, $hour, $firstSeen, $lastSeen, $category, $wanInterface,
                $uploadBytes, $downloadBytes, $uploadPackets, $downloadPackets)
            ON CONFLICT(ip_address, hour_start_utc, category, wan_interface) DO UPDATE SET
                first_seen_at_utc = MIN(unresolved_usage.first_seen_at_utc, excluded.first_seen_at_utc),
                last_seen_at_utc = MAX(unresolved_usage.last_seen_at_utc, excluded.last_seen_at_utc),
                upload_bytes = unresolved_usage.upload_bytes + excluded.upload_bytes,
                download_bytes = unresolved_usage.download_bytes + excluded.download_bytes,
                upload_packets = unresolved_usage.upload_packets + excluded.upload_packets,
                download_packets = unresolved_usage.download_packets + excluded.download_packets;
            """,
            [
                new("$address", delta.Address.ToString()), new("$hour", delta.HourStartUtc.ToString("O")),
                new("$firstSeen", delta.FirstSeenAtUtc.ToString("O")), new("$lastSeen", delta.LastSeenAtUtc.ToString("O")),
                new("$category", delta.Category), new("$wanInterface", delta.WanInterface),
                new("$uploadBytes", delta.UploadBytes), new("$downloadBytes", delta.DownloadBytes),
                new("$uploadPackets", delta.UploadPackets), new("$downloadPackets", delta.DownloadPackets)
            ],
            cancellationToken);

    private static IReadOnlyList<SqliteParameter> CreateUsageParameters(UsageDelta delta) =>
    [
        new("$deviceId", delta.DeviceId), new("$hour", delta.HourStartUtc.ToString("O")),
        new("$category", delta.Category), new("$wanInterface", delta.WanInterface),
        new("$uploadBytes", delta.UploadBytes), new("$downloadBytes", delta.DownloadBytes),
        new("$uploadPackets", delta.UploadPackets), new("$downloadPackets", delta.DownloadPackets)
    ];

    private static void AddQueryParameters(SqliteCommand command, UsageQuery query)
    {
        command.Parameters.AddWithValue("$from", query.FromUtc.ToString("O"));
        command.Parameters.AddWithValue("$to", query.ToUtc.ToString("O"));
        command.Parameters.AddWithValue("$category", query.Category);
        if (query.WanInterface is not null)
        {
            command.Parameters.AddWithValue("$wanInterface", query.WanInterface);
        }
    }

    private static UsageTotalsResponse ReadTotals(SqliteDataReader reader, int ordinal) => new(
        reader.GetInt64(ordinal),
        reader.GetInt64(ordinal + 1),
        reader.GetInt64(ordinal + 2),
        reader.GetInt64(ordinal + 3));

    private static async Task ExecuteAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string sql,
        IReadOnlyList<SqliteParameter> parameters,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = sql;
        foreach (var parameter in parameters)
        {
            command.Parameters.Add(parameter);
        }

        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private UsageRepositoryErrorResult LogError(Exception exception, string message)
    {
        _logger.LogError(exception, message);
        return new UsageRepositoryErrorResult();
    }
}
