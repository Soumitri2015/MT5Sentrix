using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentrix.Models
{
    public class MT5AccountInfo
    {
        public long Login { get; set; }
        public double Balance { get; set; }
        public double Equity { get; set; }
        public string Currency { get; set; }
        public string ServerTime { get; set; }
    }
}
