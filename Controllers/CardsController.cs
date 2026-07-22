using kanban.net.Models;
using kanban.net.Services;
using Microsoft.AspNetCore.Mvc;

namespace kanban.net.Controllers;

[ApiController]
[Route("api/[controller]")]
public class CardsController : ControllerBase
{
	private readonly SqliteStorageService _storage;
	private readonly IWebHostEnvironment _env;

	public CardsController(SqliteStorageService storage, IWebHostEnvironment env)
	{
		_storage = storage;
		_env = env;
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

	[HttpPost("{id}/attachments")]
	[RequestSizeLimit(52_428_800)] // 50 MB
	public async Task<IActionResult> UploadAttachment(string id, IFormFile file, [FromQuery] string? projectId)
	{
		if (file == null || file.Length == 0)
			return BadRequest(new { error = "No file uploaded" });

		var pid = ResolveProjectId(projectId);
		var store = await _storage.LoadAsync(pid);
		var card = store.Cards.FirstOrDefault(c => c.Id == id);
		if (card == null) return NotFound();

		var attachmentId = Guid.NewGuid().ToString();
		var safeName = Path.GetFileName(file.FileName);
		if (string.IsNullOrWhiteSpace(safeName)) safeName = "file";

		var relativeDir = Path.Combine("App_Data", "attachments", pid, id);
		var absoluteDir = Path.Combine(_env.ContentRootPath, relativeDir);
		Directory.CreateDirectory(absoluteDir);

		var storedFileName = $"{attachmentId}_{safeName}";
		var relativePath = Path.Combine(relativeDir, storedFileName);
		var absolutePath = Path.Combine(absoluteDir, storedFileName);

		using (var stream = System.IO.File.Create(absolutePath))
		{
			await file.CopyToAsync(stream);
		}

		var attachment = new CardAttachment
		{
			Id = attachmentId,
			FileName = safeName,
			StoredPath = relativePath.Replace('\\', '/'),
			ContentType = string.IsNullOrWhiteSpace(file.ContentType) ? "application/octet-stream" : file.ContentType,
			Size = file.Length,
			UploadedAt = DateTime.UtcNow
		};

		card.Attachments.Add(attachment);
		card.UpdatedAt = DateTime.UtcNow;
		await _storage.SaveAsync(store, pid);

		return Ok(attachment);
	}

	[HttpGet("{id}/attachments/{attachmentId}")]
	public async Task<IActionResult> DownloadAttachment(string id, string attachmentId, [FromQuery] string? projectId)
	{
		var pid = ResolveProjectId(projectId);
		var store = await _storage.LoadAsync(pid);
		var card = store.Cards.FirstOrDefault(c => c.Id == id);
		if (card == null) return NotFound();

		var attachment = card.Attachments.FirstOrDefault(a => a.Id == attachmentId);
		if (attachment == null) return NotFound();

		var absolutePath = Path.Combine(_env.ContentRootPath, attachment.StoredPath.Replace('/', Path.DirectorySeparatorChar));
		if (!System.IO.File.Exists(absolutePath)) return NotFound();

		var stream = System.IO.File.OpenRead(absolutePath);
		return File(stream, attachment.ContentType, attachment.FileName);
	}

	[HttpDelete("{id}/attachments/{attachmentId}")]
	public async Task<IActionResult> DeleteAttachment(string id, string attachmentId, [FromQuery] string? projectId)
	{
		var pid = ResolveProjectId(projectId);
		var store = await _storage.LoadAsync(pid);
		var card = store.Cards.FirstOrDefault(c => c.Id == id);
		if (card == null) return NotFound();

		var attachment = card.Attachments.FirstOrDefault(a => a.Id == attachmentId);
		if (attachment == null) return NotFound();

		var absolutePath = Path.Combine(_env.ContentRootPath, attachment.StoredPath.Replace('/', Path.DirectorySeparatorChar));
		try
		{
			if (System.IO.File.Exists(absolutePath)) System.IO.File.Delete(absolutePath);
		}
		catch
		{
			// Ignore file system errors; still remove the DB record.
		}

		card.Attachments.Remove(attachment);
		card.UpdatedAt = DateTime.UtcNow;
		await _storage.SaveAsync(store, pid);

		return NoContent();
	}
}

public class MoveRequest
{
	public string Column { get; set; } = string.Empty;
	public int Position { get; set; }
}
