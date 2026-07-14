using System.ComponentModel;
using kanban.net.Models;
using ModelContextProtocol;
using ModelContextProtocol.Server;

namespace kanban.net.Services.Mcp;

/// <summary>
/// MCP tools that expose CRUD operations for Kanban projects (boards).
/// Mirrors <see cref="Controllers.ProjectsController"/>: creating a project also
/// seeds it with default columns and priorities, and the last remaining project
/// cannot be deleted so the board stays usable.
/// </summary>
[McpServerToolType]
public static class ProjectTools
{
    [McpServerTool(Name = "list_projects"),
     Description("Lists all Kanban projects (boards), ordered by position.")]
    public static async Task<IReadOnlyList<KanbanProject>> ListProjectsAsync(
        SqliteStorageService storage,
        CancellationToken cancellationToken = default)
    {
        return await storage.LoadProjectsAsync();
    }

    [McpServerTool(Name = "create_project"),
     Description("Creates a new project (board). A new id is generated and the project is seeded with default 'To Do / In Progress / Done' columns and Low/Medium/High priorities.")]
    public static async Task<KanbanProject> CreateProjectAsync(
        SqliteStorageService storage,
        [Description("The project name. Required.")] string name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new McpException("Name is required.");

        var project = new KanbanProject
        {
            Id = Guid.NewGuid().ToString(),
            Name = name.Trim()
        };

        return await storage.CreateProjectAsync(project);
    }

    [McpServerTool(Name = "update_project"),
     Description("Renames an existing project.")]
    public static async Task<KanbanProject> UpdateProjectAsync(
        SqliteStorageService storage,
        [Description("The id of the project to update.")] string id,
        [Description("The new project name. Required.")] string name,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new McpException("Name is required.");

        var updated = await storage.UpdateProjectAsync(id, name.Trim())
            ?? throw new McpException($"Project '{id}' was not found.");
        return updated;
    }

    [McpServerTool(Name = "delete_project"),
     Description("Deletes a project along with all of its columns, cards, labels and priorities. Fails if it is the last remaining project.")]
    public static async Task<string> DeleteProjectAsync(
        SqliteStorageService storage,
        [Description("The id of the project to delete.")] string id,
        CancellationToken cancellationToken = default)
    {
        var projects = await storage.LoadProjectsAsync();
        if (projects.All(p => p.Id != id))
            throw new McpException($"Project '{id}' was not found.");

        if (projects.Count <= 1)
            throw new McpException("Cannot delete the last remaining project.");

        await storage.DeleteProjectAsync(id);
        return $"Project '{id}' deleted.";
    }
}
