# Kanban.NET

A lightweight Kanban board web application built with ASP.NET Core MVC and .NET 10. Data is stored in a local JSON file — no database required. Can be run as a console app or installed as a **Windows Service**.

## Features

- **Kanban board** with drag-and-drop card management across columns (To Do → In Progress → Done)
- **Labels** — create, edit, and assign colored labels to cards
- **Search & filter** — find cards by title/description or filter by label
- **JSON file storage** — zero-config persistence in `App_Data/kanban.json`
- **PWA support** — service worker and manifest for installable web app experience
- **Windows Service** — run as a background service that starts automatically with Windows

## Tech Stack

| Layer     | Technology                      |
|-----------|---------------------------------|
| Framework | ASP.NET Core MVC (.NET 10)      |
| Frontend  | Bootstrap, jQuery, vanilla JS   |
| Storage   | Local JSON file (`App_Data/`)   |
| Hosting   | Kestrel (console or Windows Service) |

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Run in Development

```bash
dotnet run
```

The app starts at **http://localhost:5207** (development port).

### Run in Production (Console)

```bash
dotnet run --configuration Release
```

Listens on **http://0.0.0.0:5100** (configured in `appsettings.json`).

## Install as a Windows Service

The included PowerShell script handles publishing, installing, and managing the service. **Run as Administrator.**

### Quick Start

```powershell
# Publish the app and register the Windows Service
.\install-service.ps1 -Action install

# Start the service
.\install-service.ps1 -Action start
```

The board is now available at **http://localhost:5100**.

### All Commands

| Command | Description |
|---------|-------------|
| `.\install-service.ps1 -Action publish`   | Build and publish to `.\publish\` |
| `.\install-service.ps1 -Action install`   | Publish + register the Windows Service |
| `.\install-service.ps1 -Action start`     | Start the service |
| `.\install-service.ps1 -Action stop`      | Stop the service |
| `.\install-service.ps1 -Action uninstall` | Stop + remove the service |
| `.\install-service.ps1 -Action reinstall` | Uninstall + install (for updates) |

### Service Details

| Setting       | Value                  |
|---------------|------------------------|
| Service name  | `KanbanBoard`          |
| Display name  | Kanban Board           |
| Startup type  | Automatic              |
| HTTP port     | 5100                   |
| Data file     | `publish\App_Data\kanban.json` |

### Changing the Port

Edit the `Kestrel` section in `appsettings.json` (or `publish\appsettings.json` after publishing):

```json
{
  "Kestrel": {
    "Endpoints": {
      "Http": {
        "Url": "http://0.0.0.0:8080"
      }
    }
  }
}
```

## REST API

The board UI is backed by a JSON API:

### Cards

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET`    | `/api/cards`            | List all cards (query: `?search=` `&labelId=`) |
| `POST`   | `/api/cards`            | Create a card |
| `PUT`    | `/api/cards/{id}`       | Update a card |
| `PUT`    | `/api/cards/{id}/move`  | Move a card to a column/position |
| `DELETE` | `/api/cards/{id}`       | Delete a card |

### Labels

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET`    | `/api/labels`       | List all labels |
| `POST`   | `/api/labels`       | Create a label |
| `PUT`    | `/api/labels/{id}`  | Update a label |
| `DELETE` | `/api/labels/{id}`  | Delete a label |

## Project Structure

```
kanban.net/
├── Controllers/
│   ├── CardsController.cs      # Cards REST API
│   ├── LabelsController.cs     # Labels REST API
│   └── HomeController.cs       # MVC views
├── Models/
│   ├── KanbanCard.cs           # Card model
│   ├── KanbanLabel.cs          # Label model
│   └── KanbanStore.cs          # Root store (cards + labels)
├── Services/
│   └── JsonStorageService.cs   # JSON file persistence
├── Views/                      # Razor views
├── wwwroot/                    # Static assets (CSS, JS, icons)
├── App_Data/                   # Runtime data (kanban.json)
├── Program.cs                  # App startup + Windows Service config
├── appsettings.json            # Configuration (port, logging)
└── install-service.ps1         # Windows Service helper script
```
