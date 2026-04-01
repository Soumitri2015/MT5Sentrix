using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentrix.Models
{
    public class EventLog
    {
        public string Timestamp { get; set; }
        public string Message { get; set; }
        public string DisplayDateTime { get; set; }
    }

    public class EventHourGroup
    {
        public string Hour { get; set; }
        public List<EventLog> Events { get; set; }
    }

    public class EventDateGroup
    {
        public string Date { get; set; }
        public List<EventHourGroup> Hours { get; set; }
    }
}
