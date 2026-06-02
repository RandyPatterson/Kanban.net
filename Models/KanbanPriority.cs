namespace kanban.net.Models;

public class KanbanPriority
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#e67e22";
}
