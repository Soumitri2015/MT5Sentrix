using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentrix.Models
{
    
        public class AppConfigData
        {
            public int UserID { get; set; }
            public int MaxTradesPerDay { get; set; }
            //public int ExtractionIntervalMs { get; set; }
            public string LockMessage { get; set; }
            public int MaxTradesPerSession { get; set; }
            public double LossPercentValue { get; set; }
            public bool CloseTradesOutsideSession { get; set; }


            public Dictionary<string, List<TimeWindow>> TradingSessions { get; set; }
        }

        public class TimeWindow
        {
            public string StartTime { get; set; }
            public string EndTime { get; set; }
        }

    public class TradingSessionInfo
    {
        public string Sessions { get; set; }
        public DateTime? CurrentTimeUtc { get; set; }
    }
}
