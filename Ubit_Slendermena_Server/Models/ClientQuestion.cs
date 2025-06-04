using GameServer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Ubit_Slendermena_Server.Models
{
    public class ClientQuestion
    {
        public int Id { get; set; }
        public int CategoryId { get; set; }
        public required string Text { get; set; }
        public int Price { get; set; }

        public Category Category { get; set; } = null!;
    }
}
