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

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var store = await _storage.LoadAsync();
        return Ok(store.Priorities);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] KanbanPriority priority)
    {
        if (string.IsNullOrWhiteSpace(priority.Name))
            return BadRequest(new { error = "Name is required" });

        var store = await _storage.LoadAsync();

        priority.Id = Guid.NewGuid().ToString();
        store.Priorities.Add(priority);
        await _storage.SaveAsync(store);

        return CreatedAtAction(nameof(GetAll), new { }, priority);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] KanbanPriority updated)
    {
        var store = await _storage.LoadAsync();
        var priority = store.Priorities.FirstOrDefault(p => p.Id == id);
        if (priority == null) return NotFound();

        priority.Name = updated.Name;
        priority.Color = updated.Color;

        await _storage.SaveAsync(store);
        return Ok(priority);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var store = await _storage.LoadAsync();
        var priority = store.Priorities.FirstOrDefault(p => p.Id == id);
        if (priority == null) return NotFound();

        store.Priorities.Remove(priority);

        // Clear priority reference from all cards
        foreach (var card in store.Cards.Where(c => c.PriorityId == id))
        {
            card.PriorityId = null;
        }

        await _storage.SaveAsync(store);
        return NoContent();
    }
}
