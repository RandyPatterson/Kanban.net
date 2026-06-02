namespace kanban.net.Models;

public class KanbanColumn
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Title { get; set; } = string.Empty;
    public int Position { get; set; }
}
