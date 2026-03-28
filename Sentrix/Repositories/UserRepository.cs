using Sentrix.EntityModel;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentrix.Repositories
{
    public class UserRepository
    {
        public ApplicationDBContext _conn;
        public UserRepository(ApplicationDBContext context) { _conn = context; }

        public void AddUser(Users user, bool isAdmin = false)
        {
            try
            {
                _conn.Users.Add(user);
                _conn.SaveChanges();

                var roleId = isAdmin ? 2 : 1;
                var userRole = new UserRoles
                {
                    UserId = user.Id,
                    RoleId = roleId
                };
                _conn.UserRoles.Add(userRole);
                _conn.SaveChanges();
            }
            catch (Exception)
            {

                throw;
            }
            
        }

        public int GetUser(string email, string password)
        {
            try
            {
                var user = _conn.Users.FirstOrDefault(u => u.Email == email && u.Password == password);
                return user != null ? user.Id : -1;
            }
            catch (Exception)
            {

                throw;
            }
           
        }

        public void UpdateUser(Users user)
        {
            var existingUser = _conn.Users.Find(user.Id);
            if (existingUser != null)
            {
                existingUser.UserName = user.UserName;
                existingUser.Email = user.Email;
                existingUser.Password = user.Password;
                existingUser.IsActive = user.IsActive;
                _conn.SaveChanges();
            }
        }
        public string GetUserRoleById(int id)
        {
            try
            {
                if (id == -1) return null;
                var userRole = _conn.UserRoles.Find(id);
                var roleName = userRole != null ? userRole.RoleId == 1 ? "Normal User" : "Admin User" : null;
                return roleName;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetUserRoleBy id exception{ex.Message}");
                return null;
            }
        }
        public List<Users> GetUsersByRoleId(int roleId)
        {
            try
            {

                var users = (from u in _conn.Users
                             join ur in _conn.UserRoles
                             on u.Id equals ur.UserId
                             where u.IsActive && ur.RoleId == roleId
                             orderby u.UserName
                             select u).Distinct().ToList();
                return users;
            }
            catch (Exception)
            {

                throw;
            }
        }
    }
}
