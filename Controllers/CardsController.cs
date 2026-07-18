using kanban.net.Models;
using kanban.net.Services;
using Microsoft.AspNetCore.Mvc;

namespace kanban.net.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CardsController : ControllerBase
{
	private readonly SqliteStorageService _storage;

	public CardsController(SqliteStorageService storage)
	{
		_storage = storage;
	}

    private static string ResolveProjectId(string? projectId) =>
        string.IsNullOrWhiteSpace(projectId) ? "default" : projectId;

    [HttpGet]
    public async Task<IActionResult> GetAll([FromQuery] string? search, [FromQuery] string? labelId, [FromQuery] string? projectId)
    {
        var store = await _storage.LoadAsync(ResolveProjectId(projectId));
        var cards = store.Cards.AsEnumerable();

		if (!string.IsNullOrWhiteSpace(search))
		{
			var term = search.Trim().ToLowerInvariant();
			cards = cards.Where(c =>
				c.Title.Contains(term, StringComparison.OrdinalIgnoreCase) ||
				c.Description.Contains(term, StringComparison.OrdinalIgnoreCase));
		}

		if (!string.IsNullOrWhiteSpace(labelId))
		{
			cards = cards.Where(c => c.LabelIds.Contains(labelId));
		}

		return Ok(cards.ToList());
	}

    [HttpPost]
    public async Task<IActionResult> Create([FromBody] KanbanCard card, [FromQuery] string? projectId)
    {
        if (string.IsNullOrWhiteSpace(card.Title))
            return BadRequest(new { error = "Title is required" });

        var pid = ResolveProjectId(projectId);
        var store = await _storage.LoadAsync(pid);

		card.Id = Guid.NewGuid().ToString();
		card.CreatedAt = DateTime.UtcNow;
		card.UpdatedAt = DateTime.UtcNow;

		var columnCards = store.Cards.Where(c => c.Column == card.Column);
		card.Position = columnCards.Any() ? columnCards.Max(c => c.Position) + 1 : 0;

        store.Cards.Add(card);
        await _storage.SaveAsync(store, pid);

		return CreatedAtAction(nameof(GetAll), new { }, card);
	}

    [HttpPut("{id}")]
    public async Task<IActionResult> Update(string id, [FromBody] KanbanCard updated, [FromQuery] string? projectId)
    {
        var pid = ResolveProjectId(projectId);
        var store = await _storage.LoadAsync(pid);
        var card = store.Cards.FirstOrDefault(c => c.Id == id);
        if (card == null) return NotFound();

		card.Title = updated.Title;
		card.Description = updated.Description;
		card.LabelIds = updated.LabelIds ?? new List<string>();
		card.PriorityId = updated.PriorityId;
		card.UpdatedAt = DateTime.UtcNow;

        await _storage.SaveAsync(store, pid);
        return Ok(card);
    }

    [HttpPut("{id}/move")]
    public async Task<IActionResult> Move(string id, [FromBody] MoveRequest request, [FromQuery] string? projectId)
    {
        var pid = ResolveProjectId(projectId);
        var store = await _storage.LoadAsync(pid);
        var card = store.Cards.FirstOrDefault(c => c.Id == id);
        if (card == null) return NotFound();

		var oldColumn = card.Column;
		card.Column = request.Column;
		card.UpdatedAt = DateTime.UtcNow;

		// Reorder: remove from old position, insert at new
		var targetCards = store.Cards
			.Where(c => c.Column == request.Column && c.Id != id)
			.OrderBy(c => c.Position)
			.ToList();

		var pos = Math.Clamp(request.Position, 0, targetCards.Count);
		targetCards.Insert(pos, card);
		for (int i = 0; i < targetCards.Count; i++)
			targetCards[i].Position = i;

		// Reorder old column if different
		if (oldColumn != request.Column)
		{
			var oldCards = store.Cards
				.Where(c => c.Column == oldColumn)
				.OrderBy(c => c.Position)
				.ToList();
			for (int i = 0; i < oldCards.Count; i++)
				oldCards[i].Position = i;
		}

        await _storage.SaveAsync(store, pid);
        return Ok(card);
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> Delete(string id, [FromQuery] string? projectId)
    {
        var pid = ResolveProjectId(projectId);
        var store = await _storage.LoadAsync(pid);
        var card = store.Cards.FirstOrDefault(c => c.Id == id);
        if (card == null) return NotFound();

		store.Cards.Remove(card);

		// Reorder remaining cards in that column
		var remaining = store.Cards
			.Where(c => c.Column == card.Column)
			.OrderBy(c => c.Position)
			.ToList();
		for (int i = 0; i < remaining.Count; i++)
			remaining[i].Position = i;

        await _storage.SaveAsync(store, pid);
        return NoContent();
    }
}

public class MoveRequest
{
	public string Column { get; set; } = string.Empty;
	public int Position { get; set; }
}
