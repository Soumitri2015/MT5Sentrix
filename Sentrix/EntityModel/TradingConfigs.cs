using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentrix.EntityModel
{
    public class TradingConfigs
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public int MaxTradesPerDay { get; set; }
        public int MaxTradesPerSession { get; set; }
        public decimal LossPercentvalue { get; set; }
        public string LockMessage { get; set; }
        public bool CloseTradesOutsideSession { get; set; }
    }
}
