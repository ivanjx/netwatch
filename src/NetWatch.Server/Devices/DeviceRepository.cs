using Microsoft.Data.Sqlite;

using NetWatch.Core.Devices;
using NetWatch.Server.Common;
using NetWatch.Server.Data;

namespace NetWatch.Server.Devices;

internal sealed class DeviceRepository(
    SqliteConnectionFactory _connectionFactory,
    ILogger<DeviceRepository> _logger)
{
    public async Task<RepositoryResult> StoreRouterVersionAsync(
        string version,
        bool isSupported,
        DateTimeOffset detectedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                INSERT INTO router_state (singleton_id, router_os_version, is_supported, detected_at_utc)
                VALUES (1, $version, $isSupported, $detectedAtUtc)
                ON CONFLICT(singleton_id) DO UPDATE SET
                    router_os_version = excluded.router_os_version,
                    is_supported = excluded.is_supported,
                    detected_at_utc = excluded.detected_at_utc;
                """;
            command.Parameters.AddWithValue("$version", version);
            command.Parameters.AddWithValue("$isSupported", isSupported);
            command.Parameters.AddWithValue("$detectedAtUtc", detectedAtUtc.ToString("O"));
            await command.ExecuteNonQueryAsync(cancellationToken);
            return new RouterVersionStoredRepositoryResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new CanceledRepositoryErrorResult();
        }
        catch (Exception exception)
        {
            return LogRepositoryError(exception, "Failed to store the detected RouterOS version");
        }
    }

    public async Task<RepositoryResult> SynchronizeDhcpLeasesAsync(
        IReadOnlyList<NormalizedDhcpLease> leases,
        DateTimeOffset synchronizedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            var timestamp = synchronizedAtUtc.ToString("O");

            await ExecuteAsync(
                connection,
                (SqliteTransaction)transaction,
                "UPDATE devices SET is_online = 0, updated_at_utc = $now WHERE mac_address IS NOT NULL AND archived_at_utc IS NULL;",
                [new("$now", timestamp)],
                cancellationToken);

            foreach (var lease in leases)
            {
                var deviceId = await FindDeviceIdByMacAsync(connection, (SqliteTransaction)transaction, lease.MacAddress, cancellationToken)
                    ?? Guid.NewGuid().ToString("N");
                await ExecuteAsync(
                    connection,
                    (SqliteTransaction)transaction,
                    """
                    INSERT INTO devices (
                        id, mac_address, discovered_name, type, is_manual, is_online,
                        current_ip_address, dhcp_status, dhcp_dynamic, dhcp_server, dhcp_comment,
                        last_seen, created_at_utc, updated_at_utc)
                    VALUES (
                        $id, $mac, $hostName, 'Client', 0, $isOnline,
                        $ip, $status, $dynamic, $server, $comment, $lastSeen, $now, $now)
                    ON CONFLICT(mac_address) DO UPDATE SET
                        discovered_name = excluded.discovered_name,
                        is_online = excluded.is_online,
                        current_ip_address = excluded.current_ip_address,
                        dhcp_status = excluded.dhcp_status,
                        dhcp_dynamic = excluded.dhcp_dynamic,
                        dhcp_server = excluded.dhcp_server,
                        dhcp_comment = excluded.dhcp_comment,
                        last_seen = excluded.last_seen,
                        updated_at_utc = excluded.updated_at_utc;
                    """,
                    [
                        new("$id", deviceId), new("$mac", lease.MacAddress),
                        new("$hostName", (object?)lease.HostName ?? DBNull.Value), new("$ip", lease.IpAddress),
                        new("$status", (object?)lease.Status ?? DBNull.Value), new("$dynamic", lease.IsDynamic),
                        new("$isOnline", lease.IsActive),
                        new("$server", (object?)lease.Server ?? DBNull.Value),
                        new("$comment", (object?)lease.Comment ?? DBNull.Value),
                        new("$lastSeen", (object?)lease.LastSeen ?? DBNull.Value), new("$now", timestamp)
                    ],
                    cancellationToken);

                await ExecuteAsync(
                    connection,
                    (SqliteTransaction)transaction,
                    """
                    UPDATE ip_assignments
                    SET valid_to_utc = $now
                    WHERE valid_to_utc IS NULL
                      AND ((device_id = $deviceId AND ip_address <> $ip)
                           OR (device_id <> $deviceId AND ip_address = $ip));
                    """,
                    [new("$deviceId", deviceId), new("$ip", lease.IpAddress), new("$now", timestamp)],
                    cancellationToken);

                if (lease.IsActive || !lease.IsDynamic)
                {
                    await ExecuteAsync(
                        connection,
                        (SqliteTransaction)transaction,
                        """
                        INSERT INTO ip_assignments (device_id, ip_address, source, valid_from_utc)
                        SELECT $deviceId, $ip, 'Dhcp', $now
                        WHERE NOT EXISTS (
                            SELECT 1 FROM ip_assignments
                            WHERE device_id = $deviceId AND ip_address = $ip AND valid_to_utc IS NULL
                        );
                        """,
                        [new("$deviceId", deviceId), new("$ip", lease.IpAddress), new("$now", timestamp)],
                        cancellationToken);
                }
            }

            await ExecuteAsync(
                connection,
                (SqliteTransaction)transaction,
                """
                UPDATE ip_assignments
                SET valid_to_utc = $now
                WHERE valid_to_utc IS NULL AND source = 'Dhcp'
                  AND device_id IN (SELECT id FROM devices WHERE is_online = 0 AND dhcp_dynamic = 1);
                """,
                [new("$now", timestamp)],
                cancellationToken);

            await ExecuteAsync(
                connection,
                (SqliteTransaction)transaction,
                "UPDATE router_state SET last_dhcp_sync_at_utc = $now WHERE singleton_id = 1;",
                [new("$now", timestamp)],
                cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return new DeviceWriteRepositoryResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new CanceledRepositoryErrorResult();
        }
        catch (Exception exception)
        {
            return LogRepositoryError(exception, "Failed to synchronize DHCP leases");
        }
    }

    public async Task<RepositoryResult> ListAsync(CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id, friendly_name, discovered_name, mac_address, current_ip_address,
                       type, is_manual, is_online, updated_at_utc
                FROM devices
                WHERE archived_at_utc IS NULL
                ORDER BY is_online DESC, COALESCE(friendly_name, discovered_name, mac_address, current_ip_address);
                """;
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            var devices = new List<DeviceSummaryResponse>();
            while (await reader.ReadAsync(cancellationToken))
            {
                var friendlyName = GetNullableString(reader, 1);
                var discoveredName = GetNullableString(reader, 2);
                var macAddress = GetNullableString(reader, 3);
                var ipAddress = GetNullableString(reader, 4);
                devices.Add(new DeviceSummaryResponse(
                    reader.GetString(0),
                    GetDisplayName(friendlyName, discoveredName, macAddress, ipAddress),
                    friendlyName,
                    discoveredName,
                    macAddress,
                    ipAddress,
                    reader.GetString(5),
                    reader.GetBoolean(6),
                    reader.GetBoolean(7),
                    DateTimeOffset.Parse(reader.GetString(8))));
            }

            return new RepositoryResult<IReadOnlyList<DeviceSummaryResponse>>(devices);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new CanceledRepositoryErrorResult();
        }
        catch (Exception exception)
        {
            return LogRepositoryError(exception, "Failed to list devices");
        }
    }

    public async Task<RepositoryResult> GetAsync(string id, CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                SELECT id, friendly_name, discovered_name, mac_address, current_ip_address,
                       type, notes, is_manual, is_online, dhcp_dynamic, dhcp_status, dhcp_server, dhcp_comment,
                       last_seen, created_at_utc, updated_at_utc
                FROM devices WHERE id = $id AND archived_at_utc IS NULL;
                """;
            command.Parameters.AddWithValue("$id", id);
            DeviceDetailsResponse details;
            await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            {
                if (!await reader.ReadAsync(cancellationToken))
                {
                    return new DeviceNotFoundRepositoryErrorResult();
                }

                var friendlyName = GetNullableString(reader, 1);
                var discoveredName = GetNullableString(reader, 2);
                var macAddress = GetNullableString(reader, 3);
                var ipAddress = GetNullableString(reader, 4);
                details = new DeviceDetailsResponse(
                    reader.GetString(0),
                    GetDisplayName(friendlyName, discoveredName, macAddress, ipAddress),
                    friendlyName,
                    discoveredName,
                    macAddress,
                    ipAddress,
                    reader.GetString(5),
                    GetNullableString(reader, 6),
                    reader.GetBoolean(7),
                    reader.GetBoolean(8),
                    reader.IsDBNull(9) ? null : reader.GetBoolean(9),
                    GetNullableString(reader, 10),
                    GetNullableString(reader, 11),
                    GetNullableString(reader, 12),
                    GetNullableString(reader, 13),
                    DateTimeOffset.Parse(reader.GetString(14)),
                    DateTimeOffset.Parse(reader.GetString(15)),
                    []);
            }

            return new RepositoryResult<DeviceDetailsResponse>(details with
            {
                IpAssignments = await ReadAssignmentsAsync(connection, id, cancellationToken)
            });
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new CanceledRepositoryErrorResult();
        }
        catch (Exception exception)
        {
            return LogRepositoryError(exception, "Failed to read device {DeviceId}", id);
        }
    }

    public async Task<RepositoryResult> RenameAsync(
        string id,
        string? friendlyName,
        DateTimeOffset updatedAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText =
                """
                UPDATE devices SET friendly_name = $friendlyName, updated_at_utc = $now
                WHERE id = $id AND archived_at_utc IS NULL;
                """;
            command.Parameters.AddWithValue("$friendlyName", (object?)friendlyName ?? DBNull.Value);
            command.Parameters.AddWithValue("$now", updatedAtUtc.ToString("O"));
            command.Parameters.AddWithValue("$id", id);
            return await command.ExecuteNonQueryAsync(cancellationToken) == 0 ?
                new DeviceNotFoundRepositoryErrorResult() :
                new DeviceWriteRepositoryResult();
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new CanceledRepositoryErrorResult();
        }
        catch (Exception exception)
        {
            return LogRepositoryError(exception, "Failed to rename device {DeviceId}", id);
        }
    }

    public async Task<RepositoryResult> CreateManualAsync(
        string friendlyName,
        string? addressOrSubnet,
        string? macAddress,
        string type,
        string? notes,
        DateTimeOffset createdAtUtc,
        CancellationToken cancellationToken)
    {
        try
        {
            var id = Guid.NewGuid().ToString("N");
            await using var connection = await _connectionFactory.OpenConnectionAsync(cancellationToken);
            await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
            await ExecuteAsync(
                connection,
                (SqliteTransaction)transaction,
                """
                INSERT INTO devices (
                    id, mac_address, friendly_name, type, notes, is_manual, is_online,
                    current_ip_address, created_at_utc, updated_at_utc)
                VALUES ($id, $mac, $name, $type, $notes, 1, 0, $address, $now, $now);
                """,
                [
                    new("$id", id), new("$mac", (object?)macAddress ?? DBNull.Value),
                    new("$name", friendlyName), new("$type", type),
                    new("$notes", (object?)notes ?? DBNull.Value),
                    new("$address", (object?)addressOrSubnet ?? DBNull.Value),
                    new("$now", createdAtUtc.ToString("O"))
                ],
                cancellationToken);
            if (addressOrSubnet is not null)
            {
                await ExecuteAsync(
                    connection,
                    (SqliteTransaction)transaction,
                    "INSERT INTO ip_assignments (device_id, ip_address, source, valid_from_utc) VALUES ($id, $address, 'Manual', $now);",
                    [new("$id", id), new("$address", addressOrSubnet), new("$now", createdAtUtc.ToString("O"))],
                    cancellationToken);
            }

            await transaction.CommitAsync(cancellationToken);
            return new RepositoryResult<string>(id);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new CanceledRepositoryErrorResult();
        }
        catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
        {
            _logger.LogInformation(exception, "A manual device conflicted with an existing identity");
            return new DeviceConflictRepositoryErrorResult();
        }
        catch (Exception exception)
        {
            return LogRepositoryError(exception, "Failed to create a manual device");
        }
    }

    private async Task<IReadOnlyList<IpAssignmentResponse>> ReadAssignmentsAsync(
        SqliteConnection connection,
        string deviceId,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.CommandText =
            """
            SELECT ip_address, source, valid_from_utc, valid_to_utc
            FROM ip_assignments WHERE device_id = $deviceId ORDER BY valid_from_utc DESC;
            """;
        command.Parameters.AddWithValue("$deviceId", deviceId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var assignments = new List<IpAssignmentResponse>();
        while (await reader.ReadAsync(cancellationToken))
        {
            assignments.Add(new IpAssignmentResponse(
                reader.GetString(0),
                reader.GetString(1),
                DateTimeOffset.Parse(reader.GetString(2)),
                reader.IsDBNull(3) ? null : DateTimeOffset.Parse(reader.GetString(3))));
        }

        return assignments;
    }

    private static async Task<string?> FindDeviceIdByMacAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        string macAddress,
        CancellationToken cancellationToken)
    {
        await using var command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = "SELECT id FROM devices WHERE mac_address = $mac;";
        command.Parameters.AddWithValue("$mac", macAddress);
        return await command.ExecuteScalarAsync(cancellationToken) as string;
    }

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

    private DeviceRepositoryErrorResult LogRepositoryError(Exception exception, string message, params object?[] arguments)
    {
        _logger.LogError(exception, message, arguments);
        return new DeviceRepositoryErrorResult();
    }

    private static string? GetNullableString(SqliteDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetString(ordinal);

    private static string GetDisplayName(
        string? friendlyName,
        string? discoveredName,
        string? macAddress,
        string? ipAddress) =>
        friendlyName ?? discoveredName ?? macAddress ?? ipAddress ?? "Unnamed device";
}
