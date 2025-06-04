using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ubit_Slendermena_Server.Models
{
    public class ClientPlayer
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public required string Username { get; set; }
        public int TotalGames { get; set; } = 0;
        public int Wins { get; set; } = 0;
        public int TotalScore { get; set; } = 0;
    }
}
