using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentrix.Models
{
    public class LegacyDailyTradeData
    {
        public string date { get; set; }
        public Dictionary<string, List<string>> sessions { get; set; }
    }
}
