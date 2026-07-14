using System.ComponentModel;
using kanban.net.Models;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace kanban.net.Services.Mcp;

/// <summary>
/// MCP tools that expose CRUD (and card-move) operations for Kanban cards.
/// The behavior mirrors <see cref="Controllers.CardsController"/> so MCP clients
/// see the same data model, validation rules and ordering semantics as REST callers.
/// </summary>
[McpServerToolType]
public static class CardTools
{
    private static string ResolveProjectId(string? projectId) =>
        string.IsNullOrWhiteSpace(projectId) ? "default" : projectId;

    [McpServerTool(Name = "list_cards"),
     Description("Lists Kanban cards for a project, optionally filtered by a free-text search over title/description and/or by a label id.")]
    public static async Task<IReadOnlyList<KanbanCard>> ListCardsAsync(
        SqliteStorageService storage,
        [Description("Free-text search matched against card title and description (case-insensitive). Optional.")] string? search = null,
        [Description("If provided, only cards that reference this label id are returned.")] string? labelId = null,
        [Description("The project id to load cards from. Defaults to 'default' when omitted.")] string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var store = await storage.LoadAsync(ResolveProjectId(projectId));
        IEnumerable<KanbanCard> cards = store.Cards;

        if (!string.IsNullOrWhiteSpace(search))
        {
            cards = cards.Where(c =>
                c.Title.Contains(search, StringComparison.OrdinalIgnoreCase) ||
                c.Description.Contains(search, StringComparison.OrdinalIgnoreCase));
        }

        if (!string.IsNullOrWhiteSpace(labelId))
        {
            cards = cards.Where(c => c.LabelIds.Contains(labelId));
        }

        return cards.OrderBy(c => c.Position).ToList();
    }

    [McpServerTool(Name = "get_card"),
     Description("Returns a single Kanban card by id.")]
    public static async Task<KanbanCard> GetCardAsync(
        SqliteStorageService storage,
        [Description("The id of the card to fetch.")] string id,
        [Description("The project the card belongs to. Defaults to 'default' when omitted.")] string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var store = await storage.LoadAsync(ResolveProjectId(projectId));
        return store.Cards.FirstOrDefault(c => c.Id == id)
            ?? throw new McpException($"Card '{id}' was not found.");
    }

    [McpServerTool(Name = "create_card"),
     Description("Creates a new Kanban card in the specified column. Assigns a new id, timestamps and appends it to the end of the column.")]
    public static async Task<KanbanCard> CreateCardAsync(
        SqliteStorageService storage,
        [Description("Card title. Required and must be non-empty.")] string title,
        [Description("Optional card description body.")] string? description = null,
        [Description("Target column id. Defaults to 'todo' when omitted.")] string? column = null,
        [Description("Optional list of label ids to attach to the card.")] IReadOnlyList<string>? labelIds = null,
        [Description("Optional priority id to associate with the card.")] string? priorityId = null,
        [Description("The project id to add the card to. Defaults to 'default' when omitted.")] string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(title))
            throw new McpException("Title is required.");

        var pid = ResolveProjectId(projectId);
        var store = await storage.LoadAsync(pid);

        var card = new KanbanCard
        {
            Id = Guid.NewGuid().ToString(),
            Title = title,
            Description = description ?? string.Empty,
            Column = string.IsNullOrWhiteSpace(column) ? "todo" : column,
            LabelIds = labelIds?.ToList() ?? new List<string>(),
            PriorityId = priorityId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        var columnCards = store.Cards.Where(c => c.Column == card.Column);
        card.Position = columnCards.Any() ? columnCards.Max(c => c.Position) + 1 : 0;

        store.Cards.Add(card);
        await storage.SaveAsync(store, pid);
        return card;
    }

    [McpServerTool(Name = "update_card"),
     Description("Updates the mutable fields (title, description, labels, priority) of an existing card. Column and position are not changed by this tool; use move_card for that.")]
    public static async Task<KanbanCard> UpdateCardAsync(
        SqliteStorageService storage,
        [Description("The id of the card to update.")] string id,
        [Description("New title for the card.")] string title,
        [Description("New description for the card. Empty string clears the description.")] string? description = null,
        [Description("Replacement list of label ids for the card. Pass an empty list to remove all labels; omit to leave labels unchanged.")] IReadOnlyList<string>? labelIds = null,
        [Description("New priority id for the card. Pass null to clear it.")] string? priorityId = null,
        [Description("The project the card belongs to. Defaults to 'default' when omitted.")] string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var pid = ResolveProjectId(projectId);
        var store = await storage.LoadAsync(pid);
        var card = store.Cards.FirstOrDefault(c => c.Id == id)
            ?? throw new McpException($"Card '{id}' was not found.");

        card.Title = title;
        card.Description = description ?? string.Empty;
        if (labelIds != null)
        {
            card.LabelIds = labelIds.ToList();
        }
        card.PriorityId = priorityId;
        card.UpdatedAt = DateTime.UtcNow;

        await storage.SaveAsync(store, pid);
        return card;
    }

    [McpServerTool(Name = "move_card"),
     Description("Moves a card to a different column and/or a new position within a column. Positions in the source and target columns are renormalized.")]
    public static async Task<KanbanCard> MoveCardAsync(
        SqliteStorageService storage,
        [Description("The id of the card to move.")] string id,
        [Description("The id of the destination column.")] string column,
        [Description("The zero-based position within the destination column. Values are clamped to the valid range.")] int position,
        [Description("The project the card belongs to. Defaults to 'default' when omitted.")] string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var pid = ResolveProjectId(projectId);
        var store = await storage.LoadAsync(pid);
        var card = store.Cards.FirstOrDefault(c => c.Id == id)
            ?? throw new McpException($"Card '{id}' was not found.");

        var oldColumn = card.Column;
        card.Column = column;
        card.UpdatedAt = DateTime.UtcNow;

        var targetCards = store.Cards
            .Where(c => c.Column == column && c.Id != id)
            .OrderBy(c => c.Position)
            .ToList();

        var pos = Math.Clamp(position, 0, targetCards.Count);
        targetCards.Insert(pos, card);
        for (int i = 0; i < targetCards.Count; i++)
            targetCards[i].Position = i;

        if (oldColumn != column)
        {
            var oldCards = store.Cards
                .Where(c => c.Column == oldColumn)
                .OrderBy(c => c.Position)
                .ToList();
            for (int i = 0; i < oldCards.Count; i++)
                oldCards[i].Position = i;
        }

        await storage.SaveAsync(store, pid);
        return card;
    }

    [McpServerTool(Name = "delete_card"),
     Description("Deletes a card and renormalizes the remaining cards in the same column.")]
    public static async Task<string> DeleteCardAsync(
        SqliteStorageService storage,
        [Description("The id of the card to delete.")] string id,
        [Description("The project the card belongs to. Defaults to 'default' when omitted.")] string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var pid = ResolveProjectId(projectId);
        var store = await storage.LoadAsync(pid);
        var card = store.Cards.FirstOrDefault(c => c.Id == id)
            ?? throw new McpException($"Card '{id}' was not found.");

        store.Cards.Remove(card);

        var remaining = store.Cards
            .Where(c => c.Column == card.Column)
            .OrderBy(c => c.Position)
            .ToList();
        for (int i = 0; i < remaining.Count; i++)
            remaining[i].Position = i;

        await storage.SaveAsync(store, pid);
        return $"Card '{id}' deleted.";
    }
}
