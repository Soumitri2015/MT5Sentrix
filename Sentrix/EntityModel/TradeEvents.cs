using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentrix.EntityModel
{
    public class TradeEvents
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public DateTime TradeDate { get; set; }
        public TimeOnly EventTime { get; set; }
        public string Message { get; set; }
        public DateTime DisplayDateTime { get; set; }
    }
}
