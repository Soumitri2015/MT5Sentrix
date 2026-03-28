using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentrix.Models
{
    public static class UserSession
    {
        public static int UserId { get; private set; }
        public static string UserRole { get; private set; }

        public static void SetUser(int userId)
        {
            UserId = userId;
        }
        public static void SetUserRole(string role)
        {
            UserRole = role;
        }

        public static bool IsLoggedIn => UserId > 0;
    }
}
