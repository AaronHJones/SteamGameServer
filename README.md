# Game Server Monitor

A self-hosted web dashboard for monitoring and managing dedicated game servers on Linux. Built with ASP.NET Core Blazor Server and MudBlazor.

![.NET 10](https://img.shields.io/badge/.NET-10.0-512BD4) ![Blazor](https://img.shields.io/badge/Blazor-Server-512BD4) ![MudBlazor](https://img.shields.io/badge/MudBlazor-9.x-594AE2)

## Features

- **Dashboard** — live status cards for all configured servers (online/offline, player count, map)
- **Server detail** — player list, live RCON log console, editable settings
- **Steam A2S queries** — real-time server info via UDP
- **WebSocket RCON** — send commands and stream logs (Rust / Facepunch RCON)
- **Service control** — start / stop / restart via `systemctl` directly from the UI
- **Create systemd service** — generate and install `.service` files for any game server
- **SteamCMD installer** — run or generate SteamCMD install commands in-browser
- **Multi-game support** — Rust, Palworld, 7 Days to Die, Valheim, ARK, Project Zomboid, and any generic server

---

## Requirements

| Requirement | Version |
|---|---|
| [.NET SDK](https://dotnet.microsoft.com/download) | 10.0+ |
| Linux (deployment target) | Debian / Ubuntu recommended |
| `systemd` | For service control features |
| `nginx` (optional) | Reverse proxy on port 80 |
| `steamcmd` (optional) | For in-app game server installs |

---

## Quick Start (Local / Development)

1. **Clone the repo**

   ```bash
   git clone https://github.com/YOUR_USERNAME/RustServerHealth.git
   cd RustServerHealth
   ```

2. **Run the app**

   ```bash
   dotnet run
   ```

3. **Open in browser**

   Navigate to `http://localhost:5005` (or the URL shown in the terminal).

   > On first run, a default `servers.json` is created next to the binary. Edit it or use the **Add Server** page in the UI.

---

## Production Deployment (Linux)

### Option A — Automated deploy script

The `deploy/deploy.sh` script builds a self-contained release and deploys it to `/opt/gameserver-ui`.

**Deploy directly on the Linux server:**

```bash
chmod +x deploy/deploy.sh
./deploy/deploy.sh --local
```

**Deploy remotely over SSH** (run from Windows/WSL):

```bash
./deploy/deploy.sh your-server-hostname-or-ip
```

The script will:
- Build a release publish
- Copy files to `/opt/gameserver-ui`
- Install and enable the `gameserver-ui` systemd service
- Install and reload the nginx reverse proxy config

### Option B — Manual steps

1. **Publish**

   ```bash
   dotnet publish -c Release -r linux-x64 --self-contained false -o /tmp/gameserver-ui-publish
   ```

2. **Copy to server**

   ```bash
   sudo mkdir -p /opt/gameserver-ui
   sudo rsync -a /tmp/gameserver-ui-publish/ /opt/gameserver-ui/
   ```

3. **Install the systemd service**

   Edit `deploy/gameserver-ui.service` — replace `youruser` with the Linux user that will run the app.

   ```bash
   sudo cp deploy/gameserver-ui.service /etc/systemd/system/
   sudo systemctl daemon-reload
   sudo systemctl enable --now gameserver-ui
   ```

4. **Install the nginx config** (optional, for port 80 proxy)

   Edit `deploy/nginx.conf` — replace `server_name _;` with your hostname or IP.

   ```bash
   sudo cp deploy/nginx.conf /etc/nginx/sites-available/gameserver-ui.conf
   sudo ln -s /etc/nginx/sites-available/gameserver-ui.conf /etc/nginx/sites-enabled/
   sudo nginx -t && sudo systemctl reload nginx
   ```

5. **Check status**

   ```bash
   sudo systemctl status gameserver-ui
   journalctl -u gameserver-ui -f
   ```

---

## Allowing Service Control Without a Password

By default `systemctl start/stop/restart` requires `sudo`. Grant passwordless access:

```bash
sudo visudo -f /etc/sudoers.d/gameserver-ui
```

Add the following line (replace `youruser`):

```
youruser ALL=(ALL) NOPASSWD: /bin/systemctl start *, /bin/systemctl stop *, /bin/systemctl restart *
```

Alternatively, run all game servers as **user services** (no sudo required):

```bash
loginctl enable-linger youruser
# Move .service files to ~/.config/systemd/user/
systemctl --user enable rust-server
```

---

## Adding a Game Server

### Via the UI

1. Click **Add Server** in the sidebar.
2. Fill in the server details:
   - **Name** — display name shown on the dashboard
   - **Game** — select the game type (Rust, Palworld, etc.)
   - **Host** — IP address or hostname of the game server (use `localhost` if running on the same machine)
   - **Game Port** — main UDP game port (default: `28015` for Rust)
   - **Query Port** — Steam A2S query port (default: `28016` for Rust)
   - **RCON Port** — WebSocket RCON port (default: `28017` for Rust)
   - **RCON Password** — set in your server's startup args (`+rcon.password`)
   - **Service Name** — the `systemd` service name (e.g. `rust-server`)
3. Click **Save**. The server appears on the dashboard immediately.

### Via `servers.json`

Server configurations are stored in `servers.json` next to the running binary (default: `/opt/gameserver-ui/servers.json`). You can edit it directly while the app is stopped:

```json
[
  {
    "Id": "rust-1",
    "Name": "My Rust Server",
    "GameType": "Rust",
    "ServiceName": "rust-server",
    "IsUserService": false,
    "Host": "localhost",
    "GamePort": 28015,
    "QueryPort": 28016,
    "RconPort": 28017,
    "RconPassword": "your-rcon-password",
    "MaxPlayers": 100,
    "SteamAppId": 258550,
    "InstallDir": "/home/youruser/gameservers/rust_dedicated",
    "Enabled": true,
    "PollIntervalSeconds": 10,
    "LogBufferSize": 500
  }
]
```

> `servers.json` is listed in `.gitignore` — it will never be committed to your repo.

---

## Configuration

`appsettings.json` controls logging levels. The `RustServer` section is a legacy placeholder and is not used at runtime — all active server configs come from `servers.json`.

To change the listening port, set the environment variable before starting:

```bash
export ASPNETCORE_URLS=http://localhost:5050
```

Or update `deploy/gameserver-ui.service`.

---

## Project Structure

```
RustServerHealth/
├── Components/
│   ├── Pages/          # Blazor pages (Home, ServerDetail, AddServer, …)
│   ├── Layout/         # NavMenu, MainLayout
│   └── Shared/         # ServerCard component
├── Models/             # GameServer, ServerModels
├── Services/
│   ├── GameServerRegistry.cs    # Loads/saves servers.json
│   ├── GameServerManager.cs     # Background polling host
│   ├── GameServerMonitor.cs     # Per-server status polling
│   ├── SteamQueryService.cs     # Steam A2S UDP queries
│   ├── RconService.cs           # WebSocket RCON
│   └── ServiceController.cs     # systemctl integration
├── deploy/
│   ├── deploy.sh                # Build + deploy script
│   ├── gameserver-ui.service    # systemd unit template
│   └── nginx.conf               # nginx reverse proxy template
└── wwwroot/                     # Static assets, CSS, JS
```

---

## License

MIT
