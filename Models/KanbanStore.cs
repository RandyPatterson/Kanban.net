namespace kanban.net.Models;

public class KanbanStore
{
    public List<KanbanLabel> Labels { get; set; } = new();
    public List<KanbanCard> Cards { get; set; } = new();
}
