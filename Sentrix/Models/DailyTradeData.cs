using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentrix.Models
{
    public class DailyTradeData
    {
        public string date { get; set; }
        // public Dictionary<string, List<string>> sessions { get; set; }

        public Dictionary<string, List<TradeEntry>> sessions { get; set; }
        public Dictionary<string, List<TradeHistory>> sessionHistory { get; set; }
        public List<EventLog> events { get; set; } = new List<EventLog>();
    }
}
