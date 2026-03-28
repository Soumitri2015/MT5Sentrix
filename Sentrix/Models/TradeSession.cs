using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentrix.Models
{
    public class TradeSession
    {
        public string SessionId { get; set; }
        public TimeSpan StartTime { get; set; }
        public TimeSpan EndTime { get; set; }
        public int MaxTradesAllowed { get; set; }

        public int TradesPlacedInThisSession { get; set; }

        public HashSet<string> SeenPositionIds { get; set; } = new HashSet<string>();
        public bool IsInWindow(TimeSpan now) => now >= StartTime && now <= EndTime;
    }
}
