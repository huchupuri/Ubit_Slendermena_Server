namespace GameServer.Models;

public class Player
{
    private string _passwordHash;
    public Guid Id { get; set; } = Guid.NewGuid();
    public required string Username { get; set; }
    public int TotalGames { get; set; } = 0;
    public int Wins { get; set; } = 0;
    public string Password_hash
    {
        get { return _passwordHash; }
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Пароль не может быть пустым.");
            if ((value).Length < 6)
                throw new ArgumentException("Пароль должен быть меньше 6 символов.", nameof(value));
            _passwordHash = value;
        }
    }
    public int TotalScore { get; set; } = 0;
}