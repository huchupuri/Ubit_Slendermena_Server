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

public class CustomQuestionSet
{
    public List<CustomCategory> Categories { get; set; } = new();
}

public class CustomCategory
{
    public string Name { get; set; } = string.Empty;
    public List<CustomQuestion> Questions { get; set; } = new();
}

public class CustomQuestion
{
    public string Text { get; set; } = string.Empty;
    public string Answer { get; set; } = string.Empty;
    public int Price { get; set; }
}

