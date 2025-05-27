namespace GameServer.Models;

public class Question
{
    public int Id { get; set; }
    public int CategoryId { get; set; }
    public required string Text { get; set; }
    public required string Answer { get; set; }
    public int Price { get; set; }
    
    public Category Category { get; set; } = null!;
}