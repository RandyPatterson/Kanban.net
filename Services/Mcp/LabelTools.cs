using System.ComponentModel;
using kanban.net.Models;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace kanban.net.Services.Mcp;

/// <summary>
/// MCP tools that expose CRUD operations for Kanban labels.
/// Mirrors <see cref="Controllers.LabelsController"/>; deleting a label also
/// removes the label reference from every card that had it attached.
/// </summary>
[McpServerToolType]
public static class LabelTools
{
    private static string ResolveProjectId(string? projectId) =>
        string.IsNullOrWhiteSpace(projectId) ? "default" : projectId;

    [McpServerTool(Name = "list_labels"),
     Description("Lists Kanban labels defined for a project.")]
    public static async Task<IReadOnlyList<KanbanLabel>> ListLabelsAsync(
        SqliteStorageService storage,
        [Description("The project id to load labels from. Defaults to 'default' when omitted.")] string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var store = await storage.LoadAsync(ResolveProjectId(projectId));
        return store.Labels.ToList();
    }

    [McpServerTool(Name = "create_label"),
     Description("Creates a new label with the given name and color. A new id is generated automatically.")]
    public static async Task<KanbanLabel> CreateLabelAsync(
        SqliteStorageService storage,
        [Description("The label name (for example, 'bug', 'feature'). Required.")] string name,
        [Description("A hex color for the label (for example, '#3498db'). Defaults to '#3498db' when omitted.")] string? color = null,
        [Description("The project id to add the label to. Defaults to 'default' when omitted.")] string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new McpException("Name is required.");

        var pid = ResolveProjectId(projectId);
        var store = await storage.LoadAsync(pid);

        var label = new KanbanLabel
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Color = string.IsNullOrWhiteSpace(color) ? "#3498db" : color
        };

        store.Labels.Add(label);
        await storage.SaveAsync(store, pid);
        return label;
    }

    [McpServerTool(Name = "update_label"),
     Description("Updates the name and color of an existing label.")]
    public static async Task<KanbanLabel> UpdateLabelAsync(
        SqliteStorageService storage,
        [Description("The id of the label to update.")] string id,
        [Description("The new label name.")] string name,
        [Description("The new label color as a hex string.")] string color,
        [Description("The project the label belongs to. Defaults to 'default' when omitted.")] string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var pid = ResolveProjectId(projectId);
        var store = await storage.LoadAsync(pid);
        var label = store.Labels.FirstOrDefault(l => l.Id == id)
            ?? throw new McpException($"Label '{id}' was not found.");

        label.Name = name;
        label.Color = color;

        await storage.SaveAsync(store, pid);
        return label;
    }

    [McpServerTool(Name = "delete_label"),
     Description("Deletes a label. The label reference is also removed from every card that had it attached.")]
    public static async Task<string> DeleteLabelAsync(
        SqliteStorageService storage,
        [Description("The id of the label to delete.")] string id,
        [Description("The project the label belongs to. Defaults to 'default' when omitted.")] string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var pid = ResolveProjectId(projectId);
        var store = await storage.LoadAsync(pid);
        var label = store.Labels.FirstOrDefault(l => l.Id == id)
            ?? throw new McpException($"Label '{id}' was not found.");

        store.Labels.Remove(label);

        foreach (var card in store.Cards)
        {
            card.LabelIds.Remove(id);
        }

        await storage.SaveAsync(store, pid);
        return $"Label '{id}' deleted.";
    }
}
