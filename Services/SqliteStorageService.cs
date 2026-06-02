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
        using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            CREATE TABLE IF NOT EXISTS Labels (
                Id    TEXT PRIMARY KEY,
                Name  TEXT NOT NULL,
                Color TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS Cards (
                Id          TEXT PRIMARY KEY,
                Title       TEXT NOT NULL,
                Description TEXT NOT NULL DEFAULT '',
                ColumnName  TEXT NOT NULL DEFAULT 'todo',
                Position    INTEGER NOT NULL DEFAULT 0,
                CreatedAt   TEXT NOT NULL,
                UpdatedAt   TEXT NOT NULL
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
                Id       TEXT PRIMARY KEY,
                Title    TEXT NOT NULL,
                Position INTEGER NOT NULL DEFAULT 0
            );
        ";
        cmd.ExecuteNonQuery();
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

            SaveInternal(store);

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
            check.CommandText = "SELECT COUNT(*) FROM Columns;";
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
        cmd.CommandText = "INSERT INTO Columns (Id, Title, Position) VALUES ($id, $title, $pos);";
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

    public async Task<KanbanStore> LoadAsync()
    {
        await _lock.WaitAsync();
        try
        {
            return await Task.Run(LoadInternal);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task SaveAsync(KanbanStore store)
    {
        await _lock.WaitAsync();
        try
        {
            await Task.Run(() => SaveInternal(store));
        }
        finally
        {
            _lock.Release();
        }
    }

    private KanbanStore LoadInternal()
    {
        var store = new KanbanStore();
        using var conn = OpenConnection();

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT Id, Title, Position FROM Columns ORDER BY Position;";
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
            cmd.CommandText = "SELECT Id, Name, Color FROM Labels;";
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

        var cardsById = new Dictionary<string, KanbanCard>();
        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = @"SELECT Id, Title, Description, ColumnName, Position, CreatedAt, UpdatedAt
                                FROM Cards ORDER BY ColumnName, Position;";
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
                    UpdatedAt = ParseDate(reader.GetString(6))
                };
                cardsById[card.Id] = card;
                store.Cards.Add(card);
            }
        }

        using (var cmd = conn.CreateCommand())
        {
            cmd.CommandText = "SELECT CardId, LabelId FROM CardLabels;";
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

    private void SaveInternal(KanbanStore store)
    {
        using var conn = OpenConnection();
        using var tx = conn.BeginTransaction();

        using (var cmd = conn.CreateCommand())
        {
            cmd.Transaction = tx;
            cmd.CommandText = "DELETE FROM CardLabels; DELETE FROM Cards; DELETE FROM Labels; DELETE FROM Columns;";
            cmd.ExecuteNonQuery();
        }

        using (var insertCol = conn.CreateCommand())
        {
            insertCol.Transaction = tx;
            insertCol.CommandText = "INSERT INTO Columns (Id, Title, Position) VALUES ($id, $title, $pos);";
            var pId = insertCol.CreateParameter(); pId.ParameterName = "$id"; insertCol.Parameters.Add(pId);
            var pTitle = insertCol.CreateParameter(); pTitle.ParameterName = "$title"; insertCol.Parameters.Add(pTitle);
            var pPos = insertCol.CreateParameter(); pPos.ParameterName = "$pos"; insertCol.Parameters.Add(pPos);
            foreach (var c in store.Columns)
            {
                pId.Value = c.Id;
                pTitle.Value = c.Title ?? string.Empty;
                pPos.Value = c.Position;
                insertCol.ExecuteNonQuery();
            }
        }

        using (var insertLabel = conn.CreateCommand())
        {
            insertLabel.Transaction = tx;
            insertLabel.CommandText = "INSERT INTO Labels (Id, Name, Color) VALUES ($id, $name, $color);";
            var pId = insertLabel.CreateParameter(); pId.ParameterName = "$id"; insertLabel.Parameters.Add(pId);
            var pName = insertLabel.CreateParameter(); pName.ParameterName = "$name"; insertLabel.Parameters.Add(pName);
            var pColor = insertLabel.CreateParameter(); pColor.ParameterName = "$color"; insertLabel.Parameters.Add(pColor);

            foreach (var label in store.Labels)
            {
                pId.Value = label.Id;
                pName.Value = label.Name ?? string.Empty;
                pColor.Value = label.Color ?? string.Empty;
                insertLabel.ExecuteNonQuery();
            }
        }

        using (var insertCard = conn.CreateCommand())
        using (var insertCardLabel = conn.CreateCommand())
        {
            insertCard.Transaction = tx;
            insertCard.CommandText = @"INSERT INTO Cards (Id, Title, Description, ColumnName, Position, CreatedAt, UpdatedAt)
                                       VALUES ($id, $title, $desc, $col, $pos, $created, $updated);";
            var cId = insertCard.CreateParameter(); cId.ParameterName = "$id"; insertCard.Parameters.Add(cId);
            var cTitle = insertCard.CreateParameter(); cTitle.ParameterName = "$title"; insertCard.Parameters.Add(cTitle);
            var cDesc = insertCard.CreateParameter(); cDesc.ParameterName = "$desc"; insertCard.Parameters.Add(cDesc);
            var cCol = insertCard.CreateParameter(); cCol.ParameterName = "$col"; insertCard.Parameters.Add(cCol);
            var cPos = insertCard.CreateParameter(); cPos.ParameterName = "$pos"; insertCard.Parameters.Add(cPos);
            var cCreated = insertCard.CreateParameter(); cCreated.ParameterName = "$created"; insertCard.Parameters.Add(cCreated);
            var cUpdated = insertCard.CreateParameter(); cUpdated.ParameterName = "$updated"; insertCard.Parameters.Add(cUpdated);

            insertCardLabel.Transaction = tx;
            insertCardLabel.CommandText = "INSERT OR IGNORE INTO CardLabels (CardId, LabelId) VALUES ($cardId, $labelId);";
            var lCardId = insertCardLabel.CreateParameter(); lCardId.ParameterName = "$cardId"; insertCardLabel.Parameters.Add(lCardId);
            var lLabelId = insertCardLabel.CreateParameter(); lLabelId.ParameterName = "$labelId"; insertCardLabel.Parameters.Add(lLabelId);

            var validLabelIds = new HashSet<string>(store.Labels.Select(l => l.Id));

            foreach (var card in store.Cards)
            {
                cId.Value = card.Id;
                cTitle.Value = card.Title ?? string.Empty;
                cDesc.Value = card.Description ?? string.Empty;
                cCol.Value = card.Column ?? "todo";
                cPos.Value = card.Position;
                cCreated.Value = card.CreatedAt.ToString("O");
                cUpdated.Value = card.UpdatedAt.ToString("O");
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
