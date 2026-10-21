# Telemetry Collector Service

## Install the agent

The release workflow builds self-contained `win-x64` and `linux-x64` packages and attaches them to a GitHub Release whenever a `v*` tag is pushed. The target machine does not need the .NET runtime installed.

`ApiUrl` is supplied at install time and stored as service configuration. User Secrets are for local development and are not available to system services.

### Windows 10/11 or Windows Server (x64)

Open PowerShell **as Administrator** and run (replace the URL with the ingestion API endpoint):

```powershell
& ([scriptblock]::Create((Invoke-RestMethod 'https://raw.githubusercontent.com/vlad595/TelemetryCollector-Service/main/install.ps1'))) -ApiUrl 'https://your-api.example.com/metrics'
```

This installs and starts the `TelemetryCollectorServiceAgent` automatic Windows service. The installer is safe to rerun to update the agent and its API URL.

### Linux (systemd, x64)

```bash
curl -fsSL https://raw.githubusercontent.com/vlad595/TelemetryCollector-Service/main/install.sh | sudo bash -s -- 'https://your-api.example.com/metrics'
```

This installs and starts the `telemetry-collector-agent` systemd service, enables it at boot, and restarts it after failures. The installer is safe to rerun.

### Publish a release

From a clean working tree, push a version tag:

```bash
git tag v1.0.0
git push origin v1.0.0
```

In GitHub, open **Actions** and wait for **Build and publish agent** to finish. It creates the release and uploads both platform archives. The installer commands always download the latest release. The repository's Actions setting **Workflow permissions** must allow creating releases (the workflow requests `contents: write`).

The first setup requires the repository changes, including these installers, to be merged to `main` before users run the commands.
