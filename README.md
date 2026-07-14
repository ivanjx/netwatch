# NetWatch

NetWatch is a lightweight, self-hosted bandwidth monitor for MikroTik routers running RouterOS v7 or later.

It collects NetFlow v5 traffic, discovers devices from RouterOS DHCP leases, and stores usage data in a local SQLite database.

## Deployment

1. Configure the MikroTik, replacing `192.168.88.2` with the Docker host's address:

   ```routeros
   /user
   add name=netwatch group=read password=change-me

   /ip service
   enable www

   /ip firewall filter
   add chain=input action=accept protocol=tcp src-address=192.168.88.2 dst-port=80 place-before=0 comment="Allow NetWatch REST"

   /ip traffic-flow
   set enabled=yes interfaces=all active-flow-timeout=1m inactive-flow-timeout=15s

   /ip traffic-flow target
   add dst-address=192.168.88.2 port=2055 version=5
   ```

For more responsive live traffic updates, reduce the MikroTik `active-flow-timeout` and
`inactive-flow-timeout` to `5s` or `10s`. Shorter timeouts increase the number of exported
flow records and may add load to the router.

2. Add `docker-compose.yml`:

   ```yaml
   services:
     netwatch:
       image: ghcr.io/ivanjx/netwatch:latest
       environment:
         NETFLOW_ALLOWED_EXPORTERS: 192.168.88.1
         MIKROTIK_BASE_URL: http://192.168.88.1
         MIKROTIK_USERNAME: netwatch
         MIKROTIK_PASSWORD: change-me
         TZ: America/New_York
       ports:
         - "8080:8080/tcp"
         - "2055:2055/udp"
       volumes:
         - netwatch-data:/data
       restart: unless-stopped

   volumes:
     netwatch-data:
   ```

3. Start NetWatch:

   ```sh
   docker compose up -d
   ```

4. Open `http://<docker-host>:8080`.

For now it is highly recommended to use proxy level authentication as this project does not come with built-in auth mechanism.

## Environment variables

| Variable | Default | Notes |
| --- | --- | --- |
| `TZ` | `UTC` | IANA reporting timezone, for example `America/New_York`. |
| `NETFLOW_LISTEN_PORT` | `2055` | UDP port used by the NetFlow v5 listener. |
| `NETFLOW_ALLOWED_EXPORTERS` | — | Comma-separated router IPs allowed to submit flows; allows any exporter when unset. |
| `IGNORED_NETWORKS` | — | Comma-separated CIDR networks to discard; ignores none when unset. |
| `USAGE_FLUSH_INTERVAL_SECONDS` | `5` | How often collected usage is written to SQLite. |
| `LIVE_SAMPLE_INTERVAL_SECONDS` | `2` | Minimum flow duration, from `1` to `60` seconds, used when calculating live rates. |
| `LIVE_IDLE_TIMEOUT_SECONDS` | `15` | How long, from `1` to `3600` seconds, live observations remain visible after their last flow export. |
| `MIKROTIK_BASE_URL` | — | DHCP sync: RouterOS REST base URL; disables sync when unset. |
| `MIKROTIK_USERNAME` | — | DHCP sync: RouterOS REST username; omits authentication when unset. |
| `MIKROTIK_PASSWORD` | — | DHCP sync: RouterOS REST password; ignored when the username is unset. |
| `MIKROTIK_ALLOW_INVALID_CERTIFICATE` | `false` | DHCP sync: accept an invalid/self-signed HTTPS certificate. Use only on a trusted network. |
| `DHCP_SYNC_INTERVAL_SECONDS` | `30` | How often DHCP leases are refreshed. |

## License

NetWatch is available under the [MIT License](LICENSE).
