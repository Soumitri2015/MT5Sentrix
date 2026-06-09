using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentrix.Models
{
    public static class     UserSession
    {
        public static int UserId { get; private set; }
        public static string UserRole { get; private set; }
        public static bool IsInTrial { get; private set; } 

        public static void SetUser(int userId)
        {
            UserId = userId;
        }
        public static void SetUserRole(string role)
        {
            UserRole = role;
        }
        public static void SetTrialStatus(bool isInTrial)
        {
            IsInTrial = isInTrial;
        }


        public static bool IsLoggedIn => UserId > 0;
    }
}
