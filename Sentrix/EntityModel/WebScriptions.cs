using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentrix.EntityModel
{
    public class WebScriptions
    {
        public int Id { get; set; }
        public int PlanId { get; set; }
        public DateTime StartDate { get; set; }
        public DateTime ExpiryDate { get; set; }
        public bool IsActive { get; set; }
        public string PaymentStatus { get; set; }
        public DateTime CreatedAt { get; set; }
        public string StatusToken { get; set; }
        public string CompanyId { get; set; }

    }
}
