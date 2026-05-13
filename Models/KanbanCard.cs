namespace kanban.net.Models;

public class KanbanCard
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Column { get; set; } = "todo";
    public List<string> LabelIds { get; set; } = new();
    public int Position { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}
