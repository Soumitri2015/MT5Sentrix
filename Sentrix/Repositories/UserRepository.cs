using Microsoft.EntityFrameworkCore;
using Sentrix.EntityModel;
//using Sentrix.Models;
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
                var userRole = _conn.UserRoles.FirstOrDefault(ur => ur.UserId == id);
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

        public async Task<List<Sentrix.Models.EventLog>> GetEventsByUser(int userId)
        {
            var events = await _conn.TradeEvents.Where(e => e.UserId == userId).OrderByDescending(e => e.TradeDate).ThenByDescending(e => e.EventTime)
                .Select(e => new Sentrix.Models.EventLog
                {
                    Timestamp = e.EventTime.ToString("HH:mm"),
                    Message = e.Message,
                    DisplayDateTime = e.DisplayDateTime.ToString("yyyy-MM-dd HH:mm:ss")
                })
                .ToListAsync();
            return events;
        }
        public string GetUserSubTrialDate(int userId)
        {
            try
            {
                var user = _conn.Users.FirstOrDefault(u => u.Id == userId);
                if (user != null)
                {
                    // If they have a SubCode, they are fully subscribed, not on trial
                    if (!string.IsNullOrEmpty(user.SubCode))
                    {
                        return "Subscribed";
                    }

                    // Calculate trial end date (7 days after creation)
                    DateTime trialEndDate = user.CreatedDate.AddDays(7);

                    // Check if trial is expired
                    if (DateTime.Now > trialEndDate)
                    {
                        // Set IsActive to false if the trial is over and no SubCode exists
                        if (user.IsActive)
                        {
                            user.IsActive = false;
                            _conn.SaveChanges();
                        }
                        return "Trial Expired";
                    }

                    // If still in trial, return the date it ends
                    return trialEndDate.ToString("yyyy-MM-dd");
                }
                return "User Not Found";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetUserSubTrialDate exception: {ex.Message}");
                return "Error Retrieving Trial Date";
            }
        }
        public bool IsUserInTrial(int userId)
        {
            try
            {
                var user = _conn.Users.FirstOrDefault(u => u.Id == userId);
                if (user != null)
                {
                    if (!string.IsNullOrEmpty(user.SubCode))
                    {
                        return false; // Not in trial, they are subscribed
                    }
                    DateTime trialEndDate = user.CreatedDate.AddDays(7);
                    return DateTime.Now <= trialEndDate; // True if still in trial
                }
                return false; // User not found, treat as not in trial
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"IsUserInTrial exception: {ex.Message}");
                return false; // On error, treat as not in trial
            }
        }
    }
}
