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
}
