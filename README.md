# NetWatch

NetWatch is a lightweight, self-hosted bandwidth monitor for MikroTik routers running RouterOS v7 or later.

The repository currently contains the Phase 0 technical-spike harness. It proves the riskiest integration points before the production architecture is built:

- a .NET 10 Native AOT Minimal API;
- a standalone Blazor WebAssembly client published and hosted by the API;
- persistent SQLite reads and writes;
- UDP reception and strict NetFlow v5 decoding;
- RouterOS REST version detection, DHCP lease reads, and a one-second Torch probe;
- a single-container Linux Native AOT build.

Automated verification is in-process and does not launch NetWatch. See [Phase 0 verification](docs/PHASE0_VERIFICATION.md) for the user-run runtime checks.

## Automated checks

```powershell
dotnet build NetWatch.slnx --configuration Release
dotnet test NetWatch.slnx --configuration Release --no-build
dotnet publish src/NetWatch.Server/NetWatch.Server.csproj --configuration Release --runtime win-x64 --self-contained true
```
