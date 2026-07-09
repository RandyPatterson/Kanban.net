using Microsoft.AspNetCore.Mvc;
using kanban.net.Models;
using kanban.net.Services;

namespace kanban.net.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LabelsController : ControllerBase
{
    private readonly SqliteStorageService _storage;

    public LabelsController(SqliteStorageService storage)
    {
        _storage = storage;
    }

    private static string ResolveProjectId(string? projectId) =>
        string.IsNullOrWhiteSpace(projectId) ? "default" : projectId;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? projectId)
    {
        var store = await _storage.LoadAsync(ResolveProjectId(projectId));
        return Ok(store.Labels);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] KanbanLabel label, [FromQuery] string? projectId)
    {
        if (string.IsNullOrWhiteSpace(label.Name))
            return BadRequest(new { error = "Name is required" });

        var pid = ResolveProjectId(projectId);
        var store = await _storage.LoadAsync(pid);

        label.Id = Guid.NewGuid().ToString();
        store.Labels.Add(label);
        await _storage.SaveAsync(store, pid);

        return CreatedAtAction(nameof(GetAll), new { }, label);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] KanbanLabel updated, [FromQuery] string? projectId)
    {
        var pid = ResolveProjectId(projectId);
        var store = await _storage.LoadAsync(pid);
        var label = store.Labels.FirstOrDefault(l => l.Id == id);
        if (label == null) return NotFound();

        label.Name = updated.Name;
        label.Color = updated.Color;

        await _storage.SaveAsync(store, pid);
        return Ok(label);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, [FromQuery] string? projectId)
    {
        var pid = ResolveProjectId(projectId);
        var store = await _storage.LoadAsync(pid);
        var label = store.Labels.FirstOrDefault(l => l.Id == id);
        if (label == null) return NotFound();

        store.Labels.Remove(label);

        // Remove label reference from all cards
        foreach (var card in store.Cards)
        {
            card.LabelIds.Remove(id);
        }

        await _storage.SaveAsync(store, pid);
        return NoContent();
    }
}
