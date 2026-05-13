using Microsoft.AspNetCore.Mvc;
using kanban.net.Models;
using kanban.net.Services;

namespace kanban.net.Controllers;

[ApiController]
[Route("api/[controller]")]
public class LabelsController : ControllerBase
{
    private readonly JsonStorageService _storage;

    public LabelsController(JsonStorageService storage)
    {
        _storage = storage;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var store = await _storage.LoadAsync();
        return Ok(store.Labels);
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] KanbanLabel label)
    {
        if (string.IsNullOrWhiteSpace(label.Name))
            return BadRequest(new { error = "Name is required" });

        var store = await _storage.LoadAsync();

        label.Id = Guid.NewGuid().ToString();
        store.Labels.Add(label);
        await _storage.SaveAsync(store);

        return CreatedAtAction(nameof(GetAll), new { }, label);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] KanbanLabel updated)
    {
        var store = await _storage.LoadAsync();
        var label = store.Labels.FirstOrDefault(l => l.Id == id);
        if (label == null) return NotFound();

        label.Name = updated.Name;
        label.Color = updated.Color;

        await _storage.SaveAsync(store);
        return Ok(label);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id)
    {
        var store = await _storage.LoadAsync();
        var label = store.Labels.FirstOrDefault(l => l.Id == id);
        if (label == null) return NotFound();

        store.Labels.Remove(label);

        // Remove label reference from all cards
        foreach (var card in store.Cards)
        {
            card.LabelIds.Remove(id);
        }

        await _storage.SaveAsync(store);
        return NoContent();
    }
}
