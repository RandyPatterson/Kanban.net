using Microsoft.AspNetCore.Mvc;
using kanban.net.Models;
using kanban.net.Services;

namespace kanban.net.Controllers;

[ApiController]
[Route("api/[controller]")]
public class PrioritiesController : ControllerBase
{
    private readonly SqliteStorageService _storage;

    public PrioritiesController(SqliteStorageService storage)
    {
        _storage = storage;
    }

    private static string ResolveProjectId(string? projectId) =>
        string.IsNullOrWhiteSpace(projectId) ? "default" : projectId;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? projectId)
    {
        var store = await _storage.LoadAsync(ResolveProjectId(projectId));
        return Ok(store.Priorities);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] KanbanPriority priority, [FromQuery] string? projectId)
    {
        if (string.IsNullOrWhiteSpace(priority.Name))
            return BadRequest(new { error = "Name is required" });

        var pid = ResolveProjectId(projectId);
        var store = await _storage.LoadAsync(pid);

        priority.Id = Guid.NewGuid().ToString();
        store.Priorities.Add(priority);
        await _storage.SaveAsync(store, pid);

        return CreatedAtAction(nameof(GetAll), new { }, priority);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] KanbanPriority updated, [FromQuery] string? projectId)
    {
        var pid = ResolveProjectId(projectId);
        var store = await _storage.LoadAsync(pid);
        var priority = store.Priorities.FirstOrDefault(p => p.Id == id);
        if (priority == null) return NotFound();

        priority.Name = updated.Name;
        priority.Color = updated.Color;

        await _storage.SaveAsync(store, pid);
        return Ok(priority);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, [FromQuery] string? projectId)
    {
        var pid = ResolveProjectId(projectId);
        var store = await _storage.LoadAsync(pid);
        var priority = store.Priorities.FirstOrDefault(p => p.Id == id);
        if (priority == null) return NotFound();

        store.Priorities.Remove(priority);

        // Clear priority reference from all cards
        foreach (var card in store.Cards.Where(c => c.PriorityId == id))
        {
            card.PriorityId = null;
        }

        await _storage.SaveAsync(store, pid);
        return NoContent();
    }
}
