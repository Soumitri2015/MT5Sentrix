using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentrix.Models
{
    public class TradeEntry
    {
        public string Symbol { get; set; }
        public double Lots { get; set; }
        public string Direction { get; set; }
        public double Entry { get; set; }
        public double? TP { get; set; }
        public double? SL { get; set; }
        public DateTime CreatedUtc { get; set; }
        public double Net { get; set; }
        public long Ticket { get; set; } 

        public string Status { get; set; } = "Open";
    }
}
