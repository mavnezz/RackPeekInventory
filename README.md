# RackPeekInventory

![Version](https://img.shields.io/badge/Version-0.1.0-blue) ![Status](https://img.shields.io/badge/Status-Alpha-orange) ![.NET](https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white) ![Platform](https://img.shields.io/badge/Platform-Linux%20%7C%20Windows-lightgrey)

A cross-platform system inventory agent for [RackPeek](https://github.com/timmoth/RackPeek). It automatically collects hardware and system information from the machine it runs on and sends it to a RackPeek server via the inventory API.

Run it once for a quick snapshot, or keep it running as a daemon that periodically updates your infrastructure documentation — no manual data entry required.

## Features

- **Auto-Discovery** — Detects CPU, RAM, drives, GPUs, NICs, OS, hardware model, and IPMI availability
- **Cross-Platform** — Native collectors for Linux (`/proc`, `/sys`, `lsblk`, `lspci`) and Windows (WMI)
- **Two Modes** — On-demand (single run) or daemon (periodic updates via `--daemon`)
- **Dry Run** — Inspect collected data as JSON without sending anything (`--dry-run`)
- **Zero Config Start** — Sensible defaults, configure via `appsettings.json`, environment variables, or CLI arguments
- **Upsert Semantics** — Creates new resources on first run, updates them on subsequent runs
- **Lightweight** — Single binary, no database, no background services beyond the optional daemon mode

## Requirements

- [.NET 10.0 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (for building from source)
- A running [RackPeek](https://github.com/timmoth/RackPeek) server with the inventory API enabled (`RPK_API_KEY` set)

## Quick Start

### 1. Build

```bash
git clone https://github.com/your-org/RackPeekInventory.git
cd RackPeekInventory
dotnet build
```

### 2. Dry Run (no server needed)

Inspect what the agent would collect and send:

```bash
dotnet run --project RackPeekInventory -- --dry-run
```

Example output:

```json
{
  "hostname": "srv01",
  "hardwareType": "server",
  "ramGb": 31.2,
  "ipmi": false,
  "model": "PowerEdge R730",
  "cpus": [
    {
      "model": "Intel(R) Xeon(R) CPU E5-2680 v4",
      "cores": 14,
      "threads": 28
    }
  ],
  "drives": [
    { "type": "nvme", "size": 1000 },
    { "type": "ssd", "size": 500 }
  ],
  "nics": [
    { "type": "rj45", "speed": 1, "ports": 1 }
  ],
  "systemType": "baremetal",
  "os": "Ubuntu 24.04.1 LTS",
  "cores": 28,
  "systemRam": 31.2,
  "systemDrives": [
    { "type": "nvme", "size": 1000 },
    { "type": "ssd", "size": 500 }
  ]
}
```

### 3. Send to RackPeek Server

```bash
dotnet run --project RackPeekInventory -- \
  --Inventory:ServerUrl=http://rackpeek-server:8080 \
  --Inventory:ApiKey=your-api-key
```

### 4. Daemon Mode

Run continuously with periodic updates (default: every 5 minutes):

```bash
dotnet run --project RackPeekInventory -- --daemon \
  --Inventory:ServerUrl=http://rackpeek-server:8080 \
  --Inventory:ApiKey=your-api-key \
  --Inventory:IntervalSeconds=300
```

## Configuration

Configuration follows standard .NET precedence: `appsettings.json` < environment variables < CLI arguments.

### appsettings.json

```json
{
  "Inventory": {
    "ServerUrl": "http://localhost:5000",
    "ApiKey": "",
    "HardwareType": "server",
    "IntervalSeconds": 300,
    "SystemType": "baremetal"
  }
}
```

### Environment Variables

```bash
export Inventory__ServerUrl=http://rackpeek-server:8080
export Inventory__ApiKey=your-api-key
export Inventory__HardwareType=server
export Inventory__IntervalSeconds=300
export Inventory__SystemType=baremetal
```

### CLI Arguments

```bash
--Inventory:ServerUrl=http://...
--Inventory:ApiKey=your-key
--Inventory:HardwareType=desktop
--Inventory:SystemType=vm
--Inventory:IntervalSeconds=60
```

### Settings Reference

| Setting | Default | Description |
|---|---|---|
| `ServerUrl` | `http://localhost:5000` | RackPeek server URL |
| `ApiKey` | *(empty)* | API key for authentication (`X-Api-Key` header) |
| `HardwareType` | `server` | Hardware type: `server`, `desktop`, or `laptop` |
| `IntervalSeconds` | `300` | Update interval in daemon mode (seconds) |
| `SystemType` | `baremetal` | System type: `baremetal`, `hypervisor`, `vm`, `container`, `embedded`, `cloud`, `other` |
| `Tags` | *(none)* | Tags to attach to the resources |
| `Labels` | *(none)* | Key-value labels to attach to the resources |

## CLI Flags

| Flag | Description |
|---|---|
| `--daemon` | Run as a background daemon with periodic updates |
| `--dry-run` | Collect and print inventory as JSON, do not send |
| `--verbose` | Enable debug-level logging |

## What Gets Collected

### Linux

| Data Point | Source |
|---|---|
| Hostname | `Environment.MachineName` |
| OS | `/etc/os-release` (PRETTY_NAME) |
| CPU | `/proc/cpuinfo` — model, physical cores, threads per socket |
| RAM | `/proc/meminfo` (total), `dmidecode` (speed, requires root) |
| Drives | `lsblk --json` — type (nvme/ssd/hdd/sas/usb), size |
| GPUs | `/sys/class/drm/card*/` + `lspci` — model, VRAM |
| NICs | `/sys/class/net/*/` — speed, type (rj45/sfp+/sfp28/qsfp28) |
| IPMI | `/dev/ipmi0` existence check |
| Model | `/sys/class/dmi/id/product_name` |

### Windows

| Data Point | WMI Class |
|---|---|
| Hostname | `Environment.MachineName` |
| OS | `Win32_OperatingSystem` → Caption |
| CPU | `Win32_Processor` → Name, NumberOfCores, NumberOfLogicalProcessors |
| RAM | `Win32_ComputerSystem` (total), `Win32_PhysicalMemory` (speed) |
| Drives | `Win32_DiskDrive` → Size, MediaType, InterfaceType |
| GPUs | `Win32_VideoController` → Name, AdapterRAM |
| NICs | `Win32_NetworkAdapter` (PhysicalAdapter=True) → Name, Speed |
| IPMI | `Win32_BMC` existence check |
| Model | `Win32_ComputerSystem` → Model |

All collectors are fault-tolerant — if a data source is unavailable (e.g., no `lspci`, no root access for `dmidecode`), the corresponding field is simply omitted.

## How It Works

```
┌─────────────────────┐       POST /api/inventory        ┌─────────────────────┐
│  RackPeekInventory  │  ─────────────────────────────►  │   RackPeek Server   │
│                     │       X-Api-Key: <key>           │                     │
│  ┌───────────────┐  │       Content-Type: json          │  ┌───────────────┐  │
│  │ LinuxCollector│  │                                   │  │ Upsert HW     │  │
│  │   or          │  │       ◄─── 200 OK ───             │  │ Upsert System │  │
│  │ WinCollector  │  │       { hardware, system }        │  │ (YAML config) │  │
│  └───────────────┘  │                                   │  └───────────────┘  │
└─────────────────────┘                                   └─────────────────────┘
```

1. The agent detects the OS and selects the appropriate collector (Linux or Windows)
2. The collector gathers hardware information from system APIs and files
3. The data is mapped to an `InventoryRequest` matching the RackPeek API schema
4. The request is sent to `POST /api/inventory` with the configured API key
5. RackPeek creates or updates the hardware and system resources in its YAML config

## Project Structure

```
RackPeekInventory/
├── RackPeekInventory/
│   ├── Program.cs                 # Entry point — mode selection, DI setup
│   ├── InventorySettings.cs       # Strongly-typed configuration
│   ├── appsettings.json           # Default configuration
│   ├── Models/
│   │   ├── InventoryRequest.cs    # API request DTO
│   │   └── InventoryResponse.cs   # API response DTO
│   ├── Collectors/
│   │   ├── ISystemCollector.cs    # Collector interface
│   │   ├── LinuxCollector.cs      # Linux: /proc, /sys, lsblk, lspci
│   │   └── WindowsCollector.cs   # Windows: WMI queries
│   ├── Client/
│   │   └── RackPeekClient.cs     # Typed HttpClient for API calls
│   └── Worker/
│       └── InventoryWorker.cs     # BackgroundService for daemon mode
└── Tests/
    ├── Collectors/
    │   └── LinuxCollectorTests.cs
    └── Client/
        └── RackPeekClientTests.cs
```

## Running Tests

```bash
dotnet test
```

## Building a Self-Contained Binary

```bash
# Linux x64
dotnet publish RackPeekInventory -c Release -r linux-x64 --self-contained -o dist/linux-x64

# Windows x64
dotnet publish RackPeekInventory -c Release -r win-x64 --self-contained -o dist/win-x64
```

## Systemd Service (Linux Daemon)

To run RackPeekInventory as a systemd service:

```ini
# /etc/systemd/system/rackpeek-inventory.service
[Unit]
Description=RackPeek Inventory Agent
After=network.target

[Service]
Type=simple
ExecStart=/opt/rackpeek-inventory/RackPeekInventory --daemon
Environment=Inventory__ServerUrl=http://rackpeek-server:8080
Environment=Inventory__ApiKey=your-api-key
Environment=Inventory__IntervalSeconds=300
Restart=always
RestartSec=10

[Install]
WantedBy=multi-user.target
```

```bash
sudo systemctl enable --now rackpeek-inventory
```

## RackPeek Server Setup

The inventory API must be enabled on the RackPeek server by setting the `RPK_API_KEY` environment variable:

```bash
# Docker
docker run -d \
  --name rackpeek \
  -p 8080:8080 \
  -e RPK_API_KEY=your-api-key \
  -v rackpeek-config:/app/config \
  aptacode/rackpeek:latest
```

If `RPK_API_KEY` is empty or unset, the API returns `503 Service Unavailable` (fail-closed).

## License

This project is licensed under the [GNU Affero General Public License v3.0](LICENSE), the same license as [RackPeek](https://github.com/timmoth/RackPeek).
