namespace kanban.net.Models;

public class KanbanProject
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = string.Empty;
    public int Position { get; set; }
}
