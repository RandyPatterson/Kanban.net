# Kanban.NET

A lightweight Kanban board web application built with ASP.NET Core MVC and .NET 10. Data is persisted in a local SQLite database. Ships with a JSON REST API **and** a built-in **Model Context Protocol (MCP) server** so LLM agents can manage the board. Can be run as a console app or installed as a **Windows Service**.

## Features

- **Kanban board** with drag-and-drop card management
- **Multi-project boards** — create, rename and switch between multiple boards
- **Columns** — create, rename, reorder and delete columns per board (seeded with To Do / In Progress / Done)
- **Collapsible columns** — collapse/expand individual columns to focus the board; state is remembered per project
- **Per-column sorting** — sort each column independently by position, title, priority or label via a dropdown in the column header (hidden while the column is collapsed); choice is remembered per project
- **Labels** — create, edit, color-code and assign labels to cards
- **Priorities** — create, edit, color-code and assign priority levels to cards
- **Attachments** — attach files to cards; a paperclip badge shows the attachment count on the card, and files can be opened or removed from the card editor. Files are stored on disk under `App_Data/attachments/` with metadata tracked in the database
- **Search & filter** — find cards by title/description or filter by label
- **REST API** — JSON API for cards, columns, labels, priorities and projects
- **MCP server** — Model Context Protocol server (Streamable HTTP + legacy SSE) that exposes CRUD tools for every board entity to LLM agents
- **SQLite storage** — zero-config persistence in `App_Data/kanban.db` (legacy `kanban.json` files are auto-migrated on first run); uploaded attachments are stored on disk under `App_Data/attachments/`
- **PWA support** — service worker and manifest for installable web app experience
- **Windows Service** — run as a background service that starts automatically with Windows

## Tech Stack

| Layer     | Technology                              |
|-----------|-----------------------------------------|
| Framework | ASP.NET Core MVC (.NET 10)              |
| Frontend  | Bootstrap, jQuery, vanilla JS           |
| Storage   | Local SQL Lite database (`App_Data/`)   |
| Hosting   | Kestrel (console or Windows Service)    |

## Getting Started

### Prerequisites

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)

### Run in Development

```bash
dotnet run
```

The app starts at **<http://localhost:5207**> (development port).

### Run in Production (Console)

```bash
dotnet run --configuration Release
```

Listens on **<http://0.0.0.0:5100**> (configured in `appsettings.json`).

## Install as a Windows Service

The included PowerShell script handles publishing, installing, and managing the service. **Run as Administrator.**

### Quick Start

```powershell
# Publish the app and register the Windows Service
.\install-service.ps1 -Action install

# Start the service
.\install-service.ps1 -Action start
```

The board is now available at **<http://localhost:5100**.>

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
| Data file     | `publish\App_Data\kanban.db`  |

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

The board UI is backed by a JSON API. All card, column, label and priority endpoints accept an optional `?projectId=` query parameter (defaults to `default`) so the same routes serve every board.

### Projects

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET`    | `/api/projects`       | List all projects |
| `POST`   | `/api/projects`       | Create a project (seeded with default columns and priorities) |
| `PUT`    | `/api/projects/{id}`  | Rename a project |
| `DELETE` | `/api/projects/{id}`  | Delete a project (fails if it would leave zero projects) |

### Cards

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET`    | `/api/cards`            | List cards (query: `?search=` `&labelId=` `&projectId=`) |
| `POST`   | `/api/cards`            | Create a card |
| `PUT`    | `/api/cards/{id}`       | Update a card (title, description, labels, priority) |
| `PUT`    | `/api/cards/{id}/move`  | Move a card to a column/position |
| `DELETE` | `/api/cards/{id}`       | Delete a card |
| `POST`   | `/api/cards/{id}/attachments`                 | Upload a file attachment (multipart form field `file`, max 50 MB) |
| `GET`    | `/api/cards/{id}/attachments/{attachmentId}`  | Download/open an attachment |
| `DELETE` | `/api/cards/{id}/attachments/{attachmentId}`  | Remove an attachment (deletes the stored file) |

### Columns

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET`    | `/api/columns`          | List columns for a project (ordered by position) |
| `POST`   | `/api/columns`          | Create a column |
| `PUT`    | `/api/columns/{id}`     | Rename a column |
| `PUT`    | `/api/columns/reorder`  | Reorder columns (body: array of column ids) |
| `DELETE` | `/api/columns/{id}`     | Delete a column (query: `?force=true` to also delete cards inside it) |

### Labels

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET`    | `/api/labels`       | List labels for a project |
| `POST`   | `/api/labels`       | Create a label |
| `PUT`    | `/api/labels/{id}`  | Update a label (name, color) |
| `DELETE` | `/api/labels/{id}`  | Delete a label (also removes it from cards that reference it) |

### Priorities

| Method | Endpoint | Description |
|--------|----------|-------------|
| `GET`    | `/api/priorities`      | List priorities for a project |
| `POST`   | `/api/priorities`      | Create a priority |
| `PUT`    | `/api/priorities/{id}` | Update a priority (name, color) |
| `DELETE` | `/api/priorities/{id}` | Delete a priority (clears `PriorityId` on cards that reference it) |

## MCP Server

Kanban.NET hosts a **Model Context Protocol (MCP) server** inline with the web app, so LLM agents (Claude Desktop, VS Code, MCP Inspector, custom clients, …) can create and manipulate boards with the same behavior as the REST API. All board entities — projects, columns, cards, labels and priorities — are exposed as MCP tools.

### Endpoint

The MCP server is mounted at **`/mcp`** and speaks two HTTP transports:

| Transport | URL | Notes |
|---|---|---|
| Streamable HTTP | `http://localhost:5100/mcp` | Recommended transport for new clients |
| Legacy SSE — event stream | `http://localhost:5100/mcp/sse` | For clients that only support SSE |
| Legacy SSE — client &rarr; server | `http://localhost:5100/mcp/message` | POST target used by SSE clients |

> Under the Visual Studio launch profile the port is **5207** (see `Properties/launchSettings.json`) rather than **5100**.

### Client configuration

Example `mcp.json` fragment for VS Code / Claude Desktop-style clients using the SSE transport:

```json
{
  "mcpServers": {
    "kanban": {
      "type": "sse",
      "url": "http://localhost:5100/mcp/sse"
    }
  }
}
```

For Streamable HTTP clients use `"type": "http"` and `"url": "http://localhost:5100/mcp"`.

### Available tools

Every tool accepts an optional `projectId` (defaults to `default`) — the same as the REST API. Failures surface as MCP tool errors with descriptive messages.

#### Projects

| Tool | Description |
|---|---|
| `list_projects`  | List all projects (boards) |
| `create_project` | Create a new project (seeds default columns and priorities) |
| `update_project` | Rename an existing project |
| `delete_project` | Delete a project (fails if it is the last remaining project) |

#### Columns

| Tool | Description |
|---|---|
| `list_columns`    | List columns ordered by position |
| `create_column`   | Create a new column |
| `update_column`   | Rename a column |
| `reorder_columns` | Reorder columns to match a supplied list of ids |
| `delete_column`   | Delete a column (`force=true` also deletes cards inside it) |

#### Cards

| Tool | Description |
|---|---|
| `list_cards`  | List cards (supports `search`, `labelId`, `projectId` filters) |
| `get_card`    | Fetch a single card by id |
| `create_card` | Create a card (optional column, description, labels, priority) |
| `update_card` | Update mutable fields (title, description, labels, priority) |
| `move_card`   | Move a card to a different column / position |
| `delete_card` | Delete a card and renormalize positions in the column |

#### Labels

| Tool | Description |
|---|---|
| `list_labels`  | List labels for a project |
| `create_label` | Create a label |
| `update_label` | Update a label (name, color) |
| `delete_label` | Delete a label (also removes it from every card that references it) |

#### Priorities

| Tool | Description |
|---|---|
| `list_priorities`  | List priority levels for a project |
| `create_priority`  | Create a priority |
| `update_priority`  | Update a priority (name, color) |
| `delete_priority`  | Delete a priority (clears `PriorityId` on cards that reference it) |

## Project Structure

```text
kanban.net/
├── Controllers/
│   ├── CardsController.cs        # Cards REST API
│   ├── ColumnsController.cs      # Columns REST API
│   ├── LabelsController.cs       # Labels REST API
│   ├── PrioritiesController.cs   # Priorities REST API
│   ├── ProjectsController.cs     # Projects REST API
│   └── HomeController.cs         # MVC views
├── Models/
│   ├── KanbanCard.cs             # Card model
│   ├── KanbanColumn.cs           # Column model
│   ├── KanbanLabel.cs            # Label model
│   ├── KanbanPriority.cs         # Priority model
│   ├── KanbanProject.cs          # Project (board) model
│   └── KanbanStore.cs            # Root store aggregate
├── Services/
│   ├── SqliteStorageService.cs   # SQLite persistence
│   └── Mcp/                      # MCP tool classes (one per entity)
│       ├── CardTools.cs
│       ├── ColumnTools.cs
│       ├── LabelTools.cs
│       ├── PriorityTools.cs
│       └── ProjectTools.cs
├── Views/                        # Razor views
├── wwwroot/                      # Static assets (CSS, JS, icons)
├── App_Data/                     # Runtime data (kanban.db)
├── Program.cs                    # App startup, MCP server, Windows Service config
├── appsettings.json              # Configuration (port, logging)
└── install-service.ps1           # Windows Service helper script
```
