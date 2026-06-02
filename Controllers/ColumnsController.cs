using Microsoft.AspNetCore.Mvc;
using kanban.net.Models;
using kanban.net.Services;

namespace kanban.net.Controllers;

[ApiController]
[Route("api/[controller]")]
public class ColumnsController : ControllerBase
{
    private readonly SqliteStorageService _storage;

    public ColumnsController(SqliteStorageService storage)
    {
        _storage = storage;
    }

    [HttpGet]
    public async Task<IActionResult> GetAll()
    {
        var store = await _storage.LoadAsync();
        return Ok(store.Columns.OrderBy(c => c.Position).ToList());
    }

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] KanbanColumn column)
    {
        if (string.IsNullOrWhiteSpace(column.Title))
            return BadRequest(new { error = "Title is required" });

        var store = await _storage.LoadAsync();
        column.Id = Guid.NewGuid().ToString();
        column.Position = store.Columns.Count == 0 ? 0 : store.Columns.Max(c => c.Position) + 1;
        store.Columns.Add(column);
        await _storage.SaveAsync(store);
        return CreatedAtAction(nameof(GetAll), new { }, column);
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] KanbanColumn updated)
    {
        if (string.IsNullOrWhiteSpace(updated.Title))
            return BadRequest(new { error = "Title is required" });

        var store = await _storage.LoadAsync();
        var col = store.Columns.FirstOrDefault(c => c.Id == id);
        if (col == null) return NotFound();

        col.Title = updated.Title;
        await _storage.SaveAsync(store);
        return Ok(col);
    }

    [HttpPut("reorder")]
    public async Task<IActionResult> Reorder([FromBody] List<string> orderedIds)
    {
        if (orderedIds == null) return BadRequest();

        var store = await _storage.LoadAsync();
        for (int i = 0; i < orderedIds.Count; i++)
        {
            var col = store.Columns.FirstOrDefault(c => c.Id == orderedIds[i]);
            if (col != null) col.Position = i;
        }
        await _storage.SaveAsync(store);
        return Ok(store.Columns.OrderBy(c => c.Position).ToList());
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, [FromQuery] bool force = false)
    {
        var store = await _storage.LoadAsync();
        var col = store.Columns.FirstOrDefault(c => c.Id == id);
        if (col == null) return NotFound();

        var cardsInColumn = store.Cards.Where(c => c.Column == id).ToList();
        if (cardsInColumn.Count > 0 && !force)
        {
            return Conflict(new { error = "Column is not empty", cardCount = cardsInColumn.Count });
        }

        foreach (var card in cardsInColumn)
        {
            store.Cards.Remove(card);
        }

        store.Columns.Remove(col);

        // Renormalize positions
        var ordered = store.Columns.OrderBy(c => c.Position).ToList();
        for (int i = 0; i < ordered.Count; i++) ordered[i].Position = i;

        await _storage.SaveAsync(store);
        return NoContent();
    }
}
