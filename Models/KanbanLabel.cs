namespace kanban.net.Models;

public class KanbanLabel
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public string Color { get; set; } = "#3498db";
}
