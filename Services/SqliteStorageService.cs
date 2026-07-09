using System.Text.Json;
using kanban.net.Models;
using Microsoft.Data.Sqlite;

namespace kanban.net.Services;

/// <summary>
/// SQLite-backed storage that preserves the same Load/Save semantics as the
/// previous JsonStorageService so the controllers don't need to change.
/// On first run, if a legacy App_Data/kanban.json exists, its contents are
/// imported into the new database and the file is renamed to .migrated.
/// </summary>
public class SqliteStorageService
{
    private static readonly SemaphoreSlim _lock = new(1, 1);
    private readonly string _connectionString;
    private readonly string _dbPath;
    private readonly string _legacyJsonPath;

    public SqliteStorageService(IWebHostEnvironment env)
    {
        var dataDir = Path.Combine(env.ContentRootPath, "App_Data");
        Directory.CreateDirectory(dataDir);
        _dbPath = Path.Combine(dataDir, "kanban.db");
        _legacyJsonPath = Path.Combine(dataDir, "kanban.json");
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = _dbPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Cache = SqliteCacheMode.Shared
        }.ToString();

        InitializeSchema();
        EnsureDefaultProject();
        BackfillLegacyProjectData();
        MigrateFromJsonIfNeeded();
        SeedDefaultColumnsIfNeeded();
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        conn.Open();
        using (var pragma = conn.CreateCommand())
        {
            pragma.CommandText = "PRAGMA foreign_keys = ON;";
            pragma.ExecuteNonQuery();
        }
        return conn;
    }

    private void InitializeSchema()
    {
        using var conn = OpenConnection();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Projects (
                Id       TEXT PRIMARY KEY,
                Name     TEXT NOT NULL,
                Position INTEGER NOT NULL DEFAULT 0
            );

            CREATE TABLE IF NOT EXISTS Labels (
                Id        TEXT PRIMARY KEY,
                Name      TEXT NOT NULL,
                Color     TEXT NOT NULL,
                ProjectId TEXT NOT NULL DEFAULT 'default'
            );

            CREATE TABLE IF NOT EXISTS Cards (
                Id          TEXT PRIMARY KEY,
                Title       TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                ColumnName  TEXT NOT NULL DEFAULT 'todo',
                Position    INTEGER NOT NULL DEFAULT 0,
                CreatedAt   TEXT NOT NULL,
                UpdatedAt   TEXT NOT NULL,
                PriorityId  TEXT NULL,
                ProjectId   TEXT NOT NULL DEFAULT 'default'
            );

            CREATE TABLE IF NOT EXISTS Priorities (
                Id        TEXT PRIMARY KEY,
                Name      TEXT NOT NULL,
                Color     TEXT NOT NULL,
                ProjectId TEXT NOT NULL DEFAULT 'default'
            );

            CREATE TABLE IF NOT EXISTS CardLabels (
                CardId  TEXT NOT NULL,
                LabelId TEXT NOT NULL,
                PRIMARY KEY (CardId, LabelId),
                FOREIGN KEY (CardId)  REFERENCES Cards(Id)  ON DELETE CASCADE,
                FOREIGN KEY (LabelId) REFERENCES Labels(Id) ON DELETE CASCADE
            );

            CREATE INDEX IF NOT EXISTS IX_Cards_Column   ON Cards(ColumnName);
            CREATE INDEX IF NOT EXISTS IX_Cards_Position ON Cards(ColumnName, Position);
            CREATE INDEX IF NOT EXISTS IX_CardLabels_LabelId ON CardLabels(LabelId);

            CREATE TABLE IF NOT EXISTS Columns (
                Id        TEXT PRIMARY KEY,
                Title     TEXT NOT NULL,
                Position  INTEGER NOT NULL DEFAULT 0,
                ProjectId TEXT NOT NULL DEFAULT 'default'
            );
        ";
            cmd.ExecuteNonQuery();
        }

        // Ensure columns added after the original schema exist on pre-existing databases.
        EnsureColumn(conn, "Cards", "PriorityId", "TEXT NULL");
        EnsureColumn(conn, "Labels", "ProjectId", "TEXT NOT NULL DEFAULT 'default'");
        EnsureColumn(conn, "Cards", "ProjectId", "TEXT NOT NULL DEFAULT 'default'");
        EnsureColumn(conn, "Priorities", "ProjectId", "TEXT NOT NULL DEFAULT 'default'");
        EnsureColumn(conn, "Columns", "ProjectId", "TEXT NOT NULL DEFAULT 'default'");

        // Now that every ProjectId column is guaranteed to exist, create supporting indexes.
        using (var idx = conn.CreateCommand())
        {
            idx.CommandText = @"
            CREATE INDEX IF NOT EXISTS IX_Cards_Project      ON Cards(ProjectId);
            CREATE INDEX IF NOT EXISTS IX_Labels_Project     ON Labels(ProjectId);
            CREATE INDEX IF NOT EXISTS IX_Priorities_Project ON Priorities(ProjectId);
            CREATE INDEX IF NOT EXISTS IX_Columns_Project    ON Columns(ProjectId);
        ";
            idx.ExecuteNonQuery();
        }
    }

    private static bool ColumnExists(SqliteConnection conn, string table, string column)
    {
        using var pragma = conn.CreateCommand();
        pragma.CommandText = $"PRAGMA table_info({table});";
        using var r = pragma.ExecuteReader();
        while (r.Read())
        {
            if (string.Equals(r.GetString(1), column, StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static void EnsureColumn(SqliteConnection conn, string table, string column, string definition)
    {
        if (ColumnExists(conn, table, column)) return;
        using var alter = conn.CreateCommand();
        alter.CommandText = $"ALTER TABLE {table} ADD COLUMN {column} {definition};";
        alter.ExecuteNonQuery();
    }

    private void EnsureDefaultProject()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "INSERT OR IGNORE INTO Projects (Id, Name, Position) VALUES ('default', 'default', 0);";
        cmd.ExecuteNonQuery();
    }

    private void BackfillLegacyProjectData()
    {
        using var conn = OpenConnection();
        foreach (var table in new[] { "Labels", "Cards", "Priorities", "Columns" })
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = $"UPDATE {table} SET ProjectId = 'default' WHERE ProjectId IS NULL OR ProjectId = '';";
            cmd.ExecuteNonQuery();
        }
    }

    private void MigrateFromJsonIfNeeded()
    {
        if (!File.Exists(_legacyJsonPath)) return;
        // Only import if the database is empty.
        using (var conn = OpenConnection())
        using (var check = conn.CreateCommand())
        {
            check.CommandText = "SELECT (SELECT COUNT(*) FROM Cards) + (SELECT COUNT(*) FROM Labels);";
            var count = Convert.ToInt64(check.ExecuteScalar());
            if (count > 0) return;
        }

        try
        {
            var json = File.ReadAllText(_legacyJsonPath);
            var store = JsonSerializer.Deserialize<KanbanStore>(json, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                PropertyNameCaseInsensitive = true
            });
            if (store == null) return;

            SaveInternal(store, "default");

            // Preserve the original file for safety.
            var backup = _legacyJsonPath + ".migrated";
            if (File.Exists(backup)) File.Delete(backup);
            File.Move(_legacyJsonPath, backup);
        }
        catch
        {
            // Swallow migration errors – the app should still start with an empty db.
        }
    }

    private void SeedDefaultColumnsIfNeeded()
    {
        using var conn = OpenConnection();
        using (var check = conn.CreateCommand())
        {
            check.CommandText = "SELECT COUNT(*) FROM Columns WHERE ProjectId = 'default';";
            var count = Convert.ToInt64(check.ExecuteScalar());
            if (count > 0) return;
        }

        var defaults = new[]
        {
            new KanbanColumn { Id = "todo",       Title = "📝 To Do",       Position = 0 },
            new KanbanColumn { Id = "inprogress", Title = "🔄 In Progress", Position = 1 },
            new KanbanColumn { Id = "done",       Title = "✅ Done",        Position = 2 }
        };

        using var tx = conn.BeginTransaction();
        using var cmd = conn.CreateCommand();
        cmd.Transaction = tx;
        cmd.CommandText = "INSERT INTO Columns (Id, Title, Position, ProjectId) VALUES ($id, $title, $pos, 'default');";
        var pId = cmd.CreateParameter(); pId.ParameterName = "$id"; cmd.Parameters.Add(pId);
        var pTitle = cmd.CreateParameter(); pTitle.ParameterName = "$title"; cmd.Parameters.Add(pTitle);
        var pPos = cmd.CreateParameter(); pPos.ParameterName = "$pos"; cmd.Parameters.Add(pPos);
        foreach (var c in defaults)
        {
            pId.Value = c.Id;
            pTitle.Value = c.Title;
            pPos.Value = c.Position;
            cmd.ExecuteNonQuery();
        }
        tx.Commit();
    }

    public async Task<KanbanStore> LoadAsync(string projectId)
    {
        await _lock.WaitAsync();
        try
        {
            return await Task.Run(() => LoadInternal(projectId));
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(KanbanStore store, string projectId)
    {
        await _lock.WaitAsync();
        try
        {
            await Task.Run(() => SaveInternal(store, projectId));
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<List<KanbanProject>> LoadProjectsAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return await Task.Run(() =>
            {
                var projects = new List<KanbanProject>();
                using var conn = OpenConnection();
                using var cmd = conn.CreateCommand();
                cmd.CommandText = "SELECT Id, Name, Position FROM Projects ORDER BY Position, Name;";
                using var reader = cmd.ExecuteReader();
                while (reader.Read())
                {
                    projects.Add(new KanbanProject
                    {
                        Id = reader.GetString(0),
                        Name = reader.GetString(1),
                        Position = reader.GetInt32(2)
                    });
                }
                return projects;
            });
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<KanbanProject> CreateProjectAsync(KanbanProject project)
    {
        await _lock.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                using var conn = OpenConnection();
                using var tx = conn.BeginTransaction();

                using (var posCmd = conn.CreateCommand())
                {
                    posCmd.Transaction = tx;
                    posCmd.CommandText = "SELECT COALESCE(MAX(Position), -1) + 1 FROM Projects;";
                    project.Position = Convert.ToInt32(posCmd.ExecuteScalar());
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "INSERT INTO Projects (Id, Name, Position) VALUES ($id, $name, $pos);";
                    cmd.Parameters.AddWithValue("$id", project.Id);
                    cmd.Parameters.AddWithValue("$name", project.Name);
                    cmd.Parameters.AddWithValue("$pos", project.Position);
                    cmd.ExecuteNonQuery();
                }

                // Seed the new project with the default set of columns so its board is usable.
                var defaults = new[]
                {
                    new KanbanColumn { Id = Guid.NewGuid().ToString(), Title = "📝 To Do",       Position = 0 },
                    new KanbanColumn { Id = Guid.NewGuid().ToString(), Title = "🔄 In Progress", Position = 1 },
                    new KanbanColumn { Id = Guid.NewGuid().ToString(), Title = "✅ Done",        Position = 2 }
                };
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "INSERT INTO Columns (Id, Title, Position, ProjectId) VALUES ($id, $title, $pos, $pid);";
                    var pId = cmd.CreateParameter(); pId.ParameterName = "$id"; cmd.Parameters.Add(pId);
                    var pTitle = cmd.CreateParameter(); pTitle.ParameterName = "$title"; cmd.Parameters.Add(pTitle);
                    var pPos = cmd.CreateParameter(); pPos.ParameterName = "$pos"; cmd.Parameters.Add(pPos);
                    var pPid = cmd.CreateParameter(); pPid.ParameterName = "$pid"; cmd.Parameters.Add(pPid);
                    foreach (var c in defaults)
                    {
                        pId.Value = c.Id;
                        pTitle.Value = c.Title;
                        pPos.Value = c.Position;
                        pPid.Value = project.Id;
                        cmd.ExecuteNonQuery();
                    }
                }

                // Seed the new project with a default set of priorities.
                // Priorities are inserted low-to-high so that "High" has the greatest
                // ordinal and its cards sort to the top of each column.
                var defaultPriorities = new[]
                {
                    new KanbanPriority { Id = Guid.NewGuid().ToString(), Name = "Low",    Color = "#2ecc71" },
                    new KanbanPriority { Id = Guid.NewGuid().ToString(), Name = "Medium", Color = "#f1c40f" },
                    new KanbanPriority { Id = Guid.NewGuid().ToString(), Name = "High",   Color = "#e74c3c" }
                };
                using (var cmd = conn.CreateCommand())
                {
                    cmd.Transaction = tx;
                    cmd.CommandText = "INSERT INTO Priorities (Id, Name, Color, ProjectId) VALUES ($id, $name, $color, $pid);";
                    var pId = cmd.CreateParameter(); pId.ParameterName = "$id"; cmd.Parameters.Add(pId);
                    var pName = cmd.CreateParameter(); pName.ParameterName = "$name"; cmd.Parameters.Add(pName);
                    var pColor = cmd.CreateParameter(); pColor.ParameterName = "$color"; cmd.Parameters.Add(pColor);
                    var pPid = cmd.CreateParameter(); pPid.ParameterName = "$pid"; cmd.Parameters.Add(pPid);
                    foreach (var p in defaultPriorities)
                    {
                        pId.Value = p.Id;
                        pName.Value = p.Name;
                        pColor.Value = p.Color;
                        pPid.Value = project.Id;
                        cmd.ExecuteNonQuery();
                    }
                }

                tx.Commit();
            });
            return project;
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<KanbanProject?> UpdateProjectAsync(string id, string name)
    {
        await _lock.WaitAsync();
        try
        {
            return await Task.Run<KanbanProject?>(() =>
            {
                using var conn = OpenConnection();
                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "UPDATE Projects SET Name = $name WHERE Id = $id;";
                    cmd.Parameters.AddWithValue("$name", name);
                    cmd.Parameters.AddWithValue("$id", id);
                    if (cmd.ExecuteNonQuery() == 0) return null;
                }

                using (var cmd = conn.CreateCommand())
                {
                    cmd.CommandText = "SELECT Id, Name, Position FROM Projects WHERE Id = $id;";
                    cmd.Parameters.AddWithValue("$id", id);
                    using var reader = cmd.ExecuteReader();
                    if (reader.Read())
                    {
                        return new KanbanProject
                        {
                            Id = reader.GetString(0),
                            Name = reader.GetString(1),
                            Position = reader.GetInt32(2)
                        };
                    }
                }
                return null;
            });
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task DeleteProjectAsync(string id)
    {
        await _lock.WaitAsync();
        try
        {
            await Task.Run(() =>
            {
                using var conn = OpenConnection();
                using var tx = conn.BeginTransaction();
                using var cmd = conn.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = @"
                    DELETE FROM CardLabels WHERE CardId IN (SELECT Id FROM Cards WHERE ProjectId = $pid);
                    DELETE FROM Cards      WHERE ProjectId = $pid;
                    DELETE FROM Labels     WHERE ProjectId = $pid;
                    DELETE FROM Priorities WHERE ProjectId = $pid;
                    DELETE FROM Columns    WHERE ProjectId = $pid;
                    DELETE FROM Projects   WHERE Id = $pid;";
                cmd.Parameters.AddWithValue("$pid", id);
                cmd.ExecuteNonQuery();
                tx.Commit();
            });
        }
        finally
        {
            _lock.Release();
        }
    }

    private KanbanStore LoadInternal(string projectId)
    {
        var store = new KanbanStore();
        using var conn = OpenConnection();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, Title, Position FROM Columns WHERE ProjectId = $pid ORDER BY Position;";
            cmd.Parameters.AddWithValue("$pid", projectId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                store.Columns.Add(new KanbanColumn
                {
                    Id = reader.GetString(0),
                    Title = reader.GetString(1),
                    Position = reader.GetInt32(2)
                });
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, Name, Color FROM Labels WHERE ProjectId = $pid;";
            cmd.Parameters.AddWithValue("$pid", projectId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                store.Labels.Add(new KanbanLabel
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Color = reader.GetString(2)
                });
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, Name, Color FROM Priorities WHERE ProjectId = $pid;";
            cmd.Parameters.AddWithValue("$pid", projectId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                store.Priorities.Add(new KanbanPriority
                {
                    Id = reader.GetString(0),
                    Name = reader.GetString(1),
                    Color = reader.GetString(2)
                });
            }
        }

        var cardsById = new Dictionary<string, KanbanCard>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT Id, Title, Description, ColumnName, Position, CreatedAt, UpdatedAt, PriorityId
                                FROM Cards WHERE ProjectId = $pid ORDER BY ColumnName, Position;";
            cmd.Parameters.AddWithValue("$pid", projectId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var card = new KanbanCard
                {
                    Id = reader.GetString(0),
                    Title = reader.GetString(1),
                    Description = reader.GetString(2),
                    Column = reader.GetString(3),
                    Position = reader.GetInt32(4),
                    CreatedAt = ParseDate(reader.GetString(5)),
                    UpdatedAt = ParseDate(reader.GetString(6)),
                    PriorityId = reader.IsDBNull(7) ? null : reader.GetString(7)
                };
                cardsById[card.Id] = card;
                store.Cards.Add(card);
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT cl.CardId, cl.LabelId
                                FROM CardLabels cl
                                INNER JOIN Cards c ON c.Id = cl.CardId
                                WHERE c.ProjectId = $pid;";
            cmd.Parameters.AddWithValue("$pid", projectId);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                var cardId = reader.GetString(0);
                var labelId = reader.GetString(1);
                if (cardsById.TryGetValue(cardId, out var card))
                {
                    card.LabelIds.Add(labelId);
                }
            }
        }

        return store;
    }

    private void SaveInternal(KanbanStore store, string projectId)
    {
        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = @"
                DELETE FROM CardLabels WHERE CardId IN (SELECT Id FROM Cards WHERE ProjectId = $pid);
                DELETE FROM Cards      WHERE ProjectId = $pid;
                DELETE FROM Labels     WHERE ProjectId = $pid;
                DELETE FROM Columns    WHERE ProjectId = $pid;
                DELETE FROM Priorities WHERE ProjectId = $pid;";
            cmd.Parameters.AddWithValue("$pid", projectId);
            cmd.ExecuteNonQuery();
        }

        using (var insertCol = conn.CreateCommand())
        {
            insertCol.Transaction = tx;
            insertCol.CommandText = "INSERT INTO Columns (Id, Title, Position, ProjectId) VALUES ($id, $title, $pos, $pid);";
            var pId = insertCol.CreateParameter(); pId.ParameterName = "$id"; insertCol.Parameters.Add(pId);
            var pTitle = insertCol.CreateParameter(); pTitle.ParameterName = "$title"; insertCol.Parameters.Add(pTitle);
            var pPos = insertCol.CreateParameter(); pPos.ParameterName = "$pos"; insertCol.Parameters.Add(pPos);
            var pPid = insertCol.CreateParameter(); pPid.ParameterName = "$pid"; insertCol.Parameters.Add(pPid);
            foreach (var c in store.Columns)
            {
                pId.Value = c.Id;
                pTitle.Value = c.Title ?? string.Empty;
                pPos.Value = c.Position;
                pPid.Value = projectId;
                insertCol.ExecuteNonQuery();
            }
        }

        using (var insertLabel = conn.CreateCommand())
        {
            insertLabel.Transaction = tx;
            insertLabel.CommandText = "INSERT INTO Labels (Id, Name, Color, ProjectId) VALUES ($id, $name, $color, $pid);";
            var pId = insertLabel.CreateParameter(); pId.ParameterName = "$id"; insertLabel.Parameters.Add(pId);
            var pName = insertLabel.CreateParameter(); pName.ParameterName = "$name"; insertLabel.Parameters.Add(pName);
            var pColor = insertLabel.CreateParameter(); pColor.ParameterName = "$color"; insertLabel.Parameters.Add(pColor);
            var pPid = insertLabel.CreateParameter(); pPid.ParameterName = "$pid"; insertLabel.Parameters.Add(pPid);

            foreach (var label in store.Labels)
            {
                pId.Value = label.Id;
                pName.Value = label.Name ?? string.Empty;
                pColor.Value = label.Color ?? string.Empty;
                pPid.Value = projectId;
                insertLabel.ExecuteNonQuery();
            }
        }

        using (var insertPriority = conn.CreateCommand())
        {
            insertPriority.Transaction = tx;
            insertPriority.CommandText = "INSERT INTO Priorities (Id, Name, Color, ProjectId) VALUES ($id, $name, $color, $pid);";
            var pId = insertPriority.CreateParameter(); pId.ParameterName = "$id"; insertPriority.Parameters.Add(pId);
            var pName = insertPriority.CreateParameter(); pName.ParameterName = "$name"; insertPriority.Parameters.Add(pName);
            var pColor = insertPriority.CreateParameter(); pColor.ParameterName = "$color"; insertPriority.Parameters.Add(pColor);
            var pPid = insertPriority.CreateParameter(); pPid.ParameterName = "$pid"; insertPriority.Parameters.Add(pPid);

            foreach (var priority in store.Priorities)
            {
                pId.Value = priority.Id;
                pName.Value = priority.Name ?? string.Empty;
                pColor.Value = priority.Color ?? string.Empty;
                pPid.Value = projectId;
                insertPriority.ExecuteNonQuery();
            }
        }

        using (var insertCard = conn.CreateCommand())
        using (var insertCardLabel = conn.CreateCommand())
        {
            insertCard.Transaction = tx;
            insertCard.CommandText = @"INSERT INTO Cards (Id, Title, Description, ColumnName, Position, CreatedAt, UpdatedAt, PriorityId, ProjectId)
                                       VALUES ($id, $title, $desc, $col, $pos, $created, $updated, $priority, $pid);";
            var cId = insertCard.CreateParameter(); cId.ParameterName = "$id"; insertCard.Parameters.Add(cId);
            var cTitle = insertCard.CreateParameter(); cTitle.ParameterName = "$title"; insertCard.Parameters.Add(cTitle);
            var cDesc = insertCard.CreateParameter(); cDesc.ParameterName = "$desc"; insertCard.Parameters.Add(cDesc);
            var cCol = insertCard.CreateParameter(); cCol.ParameterName = "$col"; insertCard.Parameters.Add(cCol);
            var cPos = insertCard.CreateParameter(); cPos.ParameterName = "$pos"; insertCard.Parameters.Add(cPos);
            var cCreated = insertCard.CreateParameter(); cCreated.ParameterName = "$created"; insertCard.Parameters.Add(cCreated);
            var cUpdated = insertCard.CreateParameter(); cUpdated.ParameterName = "$updated"; insertCard.Parameters.Add(cUpdated);
            var cPriority = insertCard.CreateParameter(); cPriority.ParameterName = "$priority"; insertCard.Parameters.Add(cPriority);
            var cPid = insertCard.CreateParameter(); cPid.ParameterName = "$pid"; insertCard.Parameters.Add(cPid);

            insertCardLabel.Transaction = tx;
            insertCardLabel.CommandText = "INSERT OR IGNORE INTO CardLabels (CardId, LabelId) VALUES ($cardId, $labelId);";
            var lCardId = insertCardLabel.CreateParameter(); lCardId.ParameterName = "$cardId"; insertCardLabel.Parameters.Add(lCardId);
            var lLabelId = insertCardLabel.CreateParameter(); lLabelId.ParameterName = "$labelId"; insertCardLabel.Parameters.Add(lLabelId);

            var validLabelIds = new HashSet<string>(store.Labels.Select(l => l.Id));
            var validPriorityIds = new HashSet<string>(store.Priorities.Select(p => p.Id));

            foreach (var card in store.Cards)
            {
                cId.Value = card.Id;
                cTitle.Value = card.Title ?? string.Empty;
                cDesc.Value = card.Description ?? string.Empty;
                cCol.Value = card.Column ?? "todo";
                cPos.Value = card.Position;
                cCreated.Value = card.CreatedAt.ToString("O");
                cUpdated.Value = card.UpdatedAt.ToString("O");
                cPriority.Value = (card.PriorityId != null && validPriorityIds.Contains(card.PriorityId))
                    ? card.PriorityId
                    : (object)DBNull.Value;
                cPid.Value = projectId;
                insertCard.ExecuteNonQuery();

                if (card.LabelIds == null) continue;
                foreach (var labelId in card.LabelIds.Distinct())
                {
                    if (!validLabelIds.Contains(labelId)) continue;
                    lCardId.Value = card.Id;
                    lLabelId.Value = labelId;
                    insertCardLabel.ExecuteNonQuery();
                }
            }
        }

        tx.Commit();
    }

    private static DateTime ParseDate(string value)
    {
        if (DateTime.TryParse(value, System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.RoundtripKind, out var dt))
        {
            return dt;
        }
        return DateTime.UtcNow;
    }
}
