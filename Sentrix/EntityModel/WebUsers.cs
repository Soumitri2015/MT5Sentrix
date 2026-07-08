using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentrix.EntityModel
{
    public class WebUsers
    {
        public string Id {  get; set; }
        public string FullName {  get; set; }   
        public bool IsActive {  get; set; }
        public DateTime CreatedAt { get; set; }
        public string? UserName { get; set; }
        public string? NormalizedEmail { get; set; }
        public string? Email { get; set; }
        public string? PasswordHash { get; set; }
        public string? SecurityStamp { get; set; }
        public string? ConcurrencyStamp { get; set; }
        public string? PhoneNumber { get; set; }
        public bool PhoneNumberConfirmed { get; set; }
        public bool TwoFactorEnabled {  get; set; }
        public DateTimeOffset? LockoutEnd { get; set; }
        public bool LockoutEnabled { get; set; }
        public int AccessFailedCount { get; set; }
        public string? CompanyId { get; set; }
        public bool? Status {  get; set; } 

    }
}
