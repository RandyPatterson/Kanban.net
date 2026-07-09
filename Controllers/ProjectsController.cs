using Microsoft.AspNetCore.Mvc;
using kanban.net.Models;
using kanban.net.Services;

namespace kanban.net.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ProjectsController : ControllerBase
{
    private readonly SqliteStorageService _storage;

    public ProjectsController(SqliteStorageService storage)
    {
        _storage = storage;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var projects = await _storage.LoadProjectsAsync();
        return Ok(projects);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] KanbanProject project)
    {
        if (string.IsNullOrWhiteSpace(project.Name))
            return BadRequest(new { error = "Name is required" });

        project.Id = Guid.NewGuid().ToString();
        var created = await _storage.CreateProjectAsync(project);
        return CreatedAtAction(nameof(GetAll), new { }, created);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] KanbanProject updated)
    {
        if (string.IsNullOrWhiteSpace(updated.Name))
            return BadRequest(new { error = "Name is required" });

        var project = await _storage.UpdateProjectAsync(id, updated.Name.Trim());
        if (project == null) return NotFound();
        return Ok(project);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var projects = await _storage.LoadProjectsAsync();
        if (projects.All(p => p.Id != id)) return NotFound();

        // Always keep at least one project so the board remains usable.
        if (projects.Count <= 1)
            return Conflict(new { error = "Cannot delete the last remaining project." });

        await _storage.DeleteProjectAsync(id);
        return NoContent();
    }
}
