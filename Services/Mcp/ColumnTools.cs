using System.ComponentModel;
using kanban.net.Models;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace kanban.net.Services.Mcp;

/// <summary>
/// MCP tools that expose CRUD (plus reorder) operations for Kanban columns.
/// Mirrors <see cref="Controllers.ColumnsController"/> so MCP callers use the
/// same validation and ordering behavior as REST callers.
/// </summary>
[McpServerToolType]
public static class ColumnTools
{
    private static string ResolveProjectId(string? projectId) =>
        string.IsNullOrWhiteSpace(projectId) ? "default" : projectId;

    [McpServerTool(Name = "list_columns"),
     Description("Lists Kanban columns for a project, ordered by their position on the board.")]
    public static async Task<IReadOnlyList<KanbanColumn>> ListColumnsAsync(
        SqliteStorageService storage,
        [Description("The project id to load columns from. Defaults to 'default' when omitted.")] string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var store = await storage.LoadAsync(ResolveProjectId(projectId));
        return store.Columns.OrderBy(c => c.Position).ToList();
    }

    [McpServerTool(Name = "create_column"),
     Description("Creates a new column. A new id is generated and the column is appended to the end of the board.")]
    public static async Task<KanbanColumn> CreateColumnAsync(
        SqliteStorageService storage,
        [Description("The column title (for example, 'To Do', 'In Progress', 'Done'). Required.")] string title,
        [Description("The project id to add the column to. Defaults to 'default' when omitted.")] string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new McpException("Title is required.");

        var pid = ResolveProjectId(projectId);
        var store = await storage.LoadAsync(pid);

        var column = new KanbanColumn
        {
            Id = Guid.NewGuid().ToString(),
            Title = title,
            Position = store.Columns.Count == 0 ? 0 : store.Columns.Max(c => c.Position) + 1
        };

        store.Columns.Add(column);
        await storage.SaveAsync(store, pid);
        return column;
    }

    [McpServerTool(Name = "update_column"),
     Description("Updates the title of an existing column. Position is preserved; use reorder_columns to change ordering.")]
    public static async Task<KanbanColumn> UpdateColumnAsync(
        SqliteStorageService storage,
        [Description("The id of the column to update.")] string id,
        [Description("The new title for the column. Required.")] string title,
        [Description("The project the column belongs to. Defaults to 'default' when omitted.")] string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new McpException("Title is required.");

        var pid = ResolveProjectId(projectId);
        var store = await storage.LoadAsync(pid);
        var column = store.Columns.FirstOrDefault(c => c.Id == id)
            ?? throw new McpException($"Column '{id}' was not found.");

        column.Title = title;
        await storage.SaveAsync(store, pid);
        return column;
    }

    [McpServerTool(Name = "reorder_columns"),
     Description("Reorders columns to match the provided list of ids. The first id becomes position 0, the next 1, and so on. Ids not present in the project are ignored.")]
    public static async Task<IReadOnlyList<KanbanColumn>> ReorderColumnsAsync(
        SqliteStorageService storage,
        [Description("The desired ordering of column ids from left to right.")] IReadOnlyList<string> orderedIds,
        [Description("The project the columns belong to. Defaults to 'default' when omitted.")] string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        if (orderedIds == null)
            throw new McpException("orderedIds is required.");

        var pid = ResolveProjectId(projectId);
        var store = await storage.LoadAsync(pid);
        for (int i = 0; i < orderedIds.Count; i++)
        {
            var col = store.Columns.FirstOrDefault(c => c.Id == orderedIds[i]);
            if (col != null) col.Position = i;
        }
        await storage.SaveAsync(store, pid);
        return store.Columns.OrderBy(c => c.Position).ToList();
    }

    [McpServerTool(Name = "delete_column"),
     Description("Deletes a column. If the column contains cards the call fails unless force=true, in which case the contained cards are deleted as well. Remaining column positions are renormalized.")]
    public static async Task<string> DeleteColumnAsync(
        SqliteStorageService storage,
        [Description("The id of the column to delete.")] string id,
        [Description("When true, cards that live in this column are deleted along with the column. Defaults to false.")] bool force = false,
        [Description("The project the column belongs to. Defaults to 'default' when omitted.")] string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var pid = ResolveProjectId(projectId);
        var store = await storage.LoadAsync(pid);
        var column = store.Columns.FirstOrDefault(c => c.Id == id)
            ?? throw new McpException($"Column '{id}' was not found.");

        var cardsInColumn = store.Cards.Where(c => c.Column == id).ToList();
        if (cardsInColumn.Count > 0 && !force)
        {
            throw new McpException(
                $"Column '{id}' is not empty (contains {cardsInColumn.Count} card(s)). Pass force=true to delete the column and its cards.");
        }

        foreach (var card in cardsInColumn)
        {
            store.Cards.Remove(card);
        }

        store.Columns.Remove(column);

        var ordered = store.Columns.OrderBy(c => c.Position).ToList();
        for (int i = 0; i < ordered.Count; i++) ordered[i].Position = i;

        await storage.SaveAsync(store, pid);
        return $"Column '{id}' deleted{(cardsInColumn.Count > 0 ? $" along with {cardsInColumn.Count} card(s)" : string.Empty)}.";
    }
}
