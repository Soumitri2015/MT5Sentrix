using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentrix.EntityModel
{
    public class Positions
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public string SessionName { get; set; }
        public Decimal Lots { get; set; }
        public string Direction { get; set; }
        public Decimal TakeProfit { get; set; }
        public Decimal StopLoss { get; set; }
        public string Symbol { get; set; }
        
        public decimal EntryPrice { get; set; }
        public DateTime CreatedUtc { get; set; }
        public Decimal NetProfit { get; set; }
        public string Status { get; set; }
        public DateTime TradeDate { get; set; }
        public int Ticket { get; set; }
    }
}
