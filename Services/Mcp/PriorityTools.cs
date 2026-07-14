using System.ComponentModel;
using kanban.net.Models;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace kanban.net.Services.Mcp;

/// <summary>
/// MCP tools that expose CRUD operations for Kanban priorities.
/// Mirrors <see cref="Controllers.PrioritiesController"/>; deleting a priority
/// clears the PriorityId from every card that referenced it.
/// </summary>
[McpServerToolType]
public static class PriorityTools
{
    private static string ResolveProjectId(string? projectId) =>
        string.IsNullOrWhiteSpace(projectId) ? "default" : projectId;

    [McpServerTool(Name = "list_priorities"),
     Description("Lists the priority levels defined for a project.")]
    public static async Task<IReadOnlyList<KanbanPriority>> ListPrioritiesAsync(
        SqliteStorageService storage,
        [Description("The project id to load priorities from. Defaults to 'default' when omitted.")] string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var store = await storage.LoadAsync(ResolveProjectId(projectId));
        return store.Priorities.ToList();
    }

    [McpServerTool(Name = "create_priority"),
     Description("Creates a new priority with the given name and color. A new id is generated automatically.")]
    public static async Task<KanbanPriority> CreatePriorityAsync(
        SqliteStorageService storage,
        [Description("The priority name (for example, 'Low', 'Medium', 'High'). Required.")] string name,
        [Description("A hex color for the priority (for example, '#e67e22'). Defaults to '#e67e22' when omitted.")] string? color = null,
        [Description("The project id to add the priority to. Defaults to 'default' when omitted.")] string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new McpException("Name is required.");

        var pid = ResolveProjectId(projectId);
        var store = await storage.LoadAsync(pid);

        var priority = new KanbanPriority
        {
            Id = Guid.NewGuid().ToString(),
            Name = name,
            Color = string.IsNullOrWhiteSpace(color) ? "#e67e22" : color
        };

        store.Priorities.Add(priority);
        await storage.SaveAsync(store, pid);
        return priority;
    }

    [McpServerTool(Name = "update_priority"),
     Description("Updates the name and color of an existing priority.")]
    public static async Task<KanbanPriority> UpdatePriorityAsync(
        SqliteStorageService storage,
        [Description("The id of the priority to update.")] string id,
        [Description("The new priority name.")] string name,
        [Description("The new priority color as a hex string.")] string color,
        [Description("The project the priority belongs to. Defaults to 'default' when omitted.")] string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var pid = ResolveProjectId(projectId);
        var store = await storage.LoadAsync(pid);
        var priority = store.Priorities.FirstOrDefault(p => p.Id == id)
            ?? throw new McpException($"Priority '{id}' was not found.");

        priority.Name = name;
        priority.Color = color;

        await storage.SaveAsync(store, pid);
        return priority;
    }

    [McpServerTool(Name = "delete_priority"),
     Description("Deletes a priority. Any card that referenced this priority has its PriorityId cleared.")]
    public static async Task<string> DeletePriorityAsync(
        SqliteStorageService storage,
        [Description("The id of the priority to delete.")] string id,
        [Description("The project the priority belongs to. Defaults to 'default' when omitted.")] string? projectId = null,
        CancellationToken cancellationToken = default)
    {
        var pid = ResolveProjectId(projectId);
        var store = await storage.LoadAsync(pid);
        var priority = store.Priorities.FirstOrDefault(p => p.Id == id)
            ?? throw new McpException($"Priority '{id}' was not found.");

        store.Priorities.Remove(priority);

        foreach (var card in store.Cards.Where(c => c.PriorityId == id))
        {
            card.PriorityId = null;
        }

        await storage.SaveAsync(store, pid);
        return $"Priority '{id}' deleted.";
    }
}
