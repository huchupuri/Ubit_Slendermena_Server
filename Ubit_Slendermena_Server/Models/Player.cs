public class Player
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Username { get; set; }
    public string Password_hash { get; set; } = null!;
    public int TotalGames { get; set; } = 0;
    public int Wins { get; set; } = 0;
    public int TotalScore { get; set; } = 0;
}