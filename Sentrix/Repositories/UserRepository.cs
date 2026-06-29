using Microsoft.EntityFrameworkCore;
using NLog.Time;
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
            // A transaction ensures that if the WebUser fails to save, the local User is rolled back, preventing mismatched databases.
            using var transaction = _conn.Database.BeginTransaction();
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


                var webUser = new WebUsers
                {
                    Id = user.Id.ToString(),
                    FullName = user.UserName,
                    IsActive = user.IsActive,
                    CreatedAt = user.CreatedDate,
                    UserName = user.UserName,
                    Email = user.Email,
                    NormalizedEmail = user.Email?.ToUpper(),
                    PasswordHash = user.Password,
                    Status = user.IsActive,


                    PhoneNumberConfirmed = false,
                    TwoFactorEnabled = false,
                    AccessFailedCount = 0
                };

                _conn.WebUsers.Add(webUser); 
                _conn.SaveChanges();

           
                transaction.Commit();
            }
            catch (Exception)
            {
                transaction.Rollback();
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
            catch (Exception ex)    
            {

                Console.WriteLine(ex.Message);
                return -1;
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
                    // 1. Check for an official Web Subscription first
                    if (!string.IsNullOrEmpty(user.SubCode))
                    {
                        // Match the user's code to the StatusToken in WebSubcription
                        var subscription = _conn.WebScriptions.FirstOrDefault(s => s.StatusToken == user.SubCode);

                        if (subscription != null)
                        {
                            if (!subscription.IsActive || DateTime.Now > subscription.ExpiryDate)
                            {
                                return "Subscription Expired";
                            }

                            // Returns e.g., "Paid - Ends 2026-07-09" or "Free - Ends 2026-07-09"
                            return $"{subscription.PaymentStatus} - Ends {subscription.ExpiryDate.ToString("yyyy-MM-dd")}";
                        }
                    }

                    // 2. Fallback to 7-day Trial if no code is present
                    DateTime trialEndDate = user.CreatedDate.AddDays(7);

                    if (DateTime.Now > trialEndDate)
                    {
                        return "Trial Expired";
                    }

                    return $"Trial Ends {trialEndDate.ToString("yyyy-MM-dd")}";
                }
                return "User Not Found";
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"GetUserSubTrialDate exception: {ex.Message}");
                return "Error Retrieving Status";
            }
        }
        public bool IsUserInTrial(int userId)
        {
            try
            {
                var user = _conn.Users.FirstOrDefault(u => u.Id == userId);
                if (user == null) return false;

                
                if (!string.IsNullOrEmpty(user.SubCode))
                {
                    var subscription = _conn.WebScriptions.FirstOrDefault(s => s.StatusToken == user.SubCode);

                    if (subscription != null)
                    {
                        
                        return subscription.IsActive && DateTime.Now <= subscription.ExpiryDate;
                    }
                }

                
                return DateTime.Now <= user.CreatedDate.AddDays(7);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"HasActiveAccess exception: {ex.Message}");
                return false;
            }
        }

        public string GetSubCodeType(string code)
        {
            try
            {
                var subcription = _conn.WebScriptions.FirstOrDefault(cd => cd.StatusToken == code);

                if (subcription != null)
                    return subcription.PaymentStatus;

                return "Invalid Code";

            }
            catch (Exception ex)
            {

                return "";
            }
           
        }

        public async Task<bool> IsUserBanned(string userId)
        {
            try
            {
                var isUserBnned = _conn.WebUsers.FirstOrDefault(u => u.Id == userId);
                return isUserBnned != null? isUserBnned.IsActive : false;

            }
            catch (Exception)
            {

                throw;
            }

        }


    }
}
