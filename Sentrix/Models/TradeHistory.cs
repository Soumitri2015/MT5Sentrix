using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentrix.Models
{
    public class TradeHistory
    {
        public string Symbol { get; set; }
        public string Direction { get; set; }
        public double EntryPrice { get; set; }
        public double ClosePrice { get; set; }
        public double Quantity { get; set; }
        public string Volume { get; set; }
        public DateTime CloseTimeUtc { get; set; }
        public double Net { get; set; }
        public double Balance { get; set; }
        public int Ticket { get; set; }
    }
}
