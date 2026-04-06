using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Sentrix.Models
{
    public class EaConfigPayload
    {
        [JsonPropertyName("CMD")]
        public string Cmd { get; set; } = "UPDATE_CONFIG";

        [JsonPropertyName("SessionActive")]
        public bool SessionActive { get; set; }

        [JsonPropertyName("MaxTradesDaily")]
        public int MaxTradesDaily { get; set; }

        [JsonPropertyName("CurrentDailyTrades")]
        public int CurrentDailyTrades { get; set; }

        [JsonPropertyName("MaxLossPercent")]
        public double MaxLossPercent { get; set; }

        [JsonPropertyName("Manage1R")]
        public bool Manage1R { get; set; }
    }
}
