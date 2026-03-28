using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentrix.Models
{
    public class DailyTradeState
    {
        public DateTime Date { get; set; }
        public int TradesToday { get; set; }
        public Dictionary<string, int> SessionTrades { get; set; } = new();
    }
}
