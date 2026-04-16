using Sentrix.Models;
using Sentrix.Repositories;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace Sentrix
{
    public class ConfigHelper
    {
        private static Dictionary<int, AppConfigData> _cachedConfig = new Dictionary<int, AppConfigData>();
        private static readonly object _lock = new object();
        private static readonly string ConfigFolder =
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                         "SentriX");
        private ConfigRepository _configRepo;
        public ConfigHelper(ConfigRepository configRepository) { _configRepo = configRepository; }
        public event Action OnConfigUpdated;

        public AppConfigData Load()
        {
            try
            {
                int userId = UserSession.UserId;
                if (userId == 0)
                    throw new InvalidOperationException("User not logged in");
                if (_cachedConfig.ContainsKey(userId))
                    return _cachedConfig[userId];

                var config = _configRepo.GetConfigDatabyUserId(userId);
                if (config == null)
                {
                    config = GetDefaultConfig();
                    config.UserID = userId;
                    _configRepo.SaveConfigByUserId(userId, config);

                }
                _cachedConfig[userId] = config;
                return config;
            }
            catch (Exception ex)
            {
                // Log the exception as needed
                Console.WriteLine($"Error loading config: {ex.Message}");
                return null;
            }
        }

        private static AppConfigData GetDefaultConfig()
        {
            return new AppConfigData
            {
                MaxTradesPerDay = int.Parse(
            ConfigurationManager.AppSettings["MaxTradesPerDay"] ?? "7"),


                LockMessage =
            ConfigurationManager.AppSettings["LockMessage"] ?? "System Locked",

                MaxTradesPerSession = int.Parse(
            ConfigurationManager.AppSettings["MaxTradesPerSession"] ?? "3"),

                LossPercentValue = double.Parse(
            ConfigurationManager.AppSettings["LossPercentValue"] ?? "1"),
                CloseTradesOutsideSession = bool.Parse(ConfigurationManager.AppSettings["CloseTradesOutsideSession"] ?? "false"),
                TradingSessions = new Dictionary<string, List<TimeWindow>>
        {
            { "London", new List<TimeWindow> { new TimeWindow { StartTime="08:00", EndTime="10:00" } } },
                    {"NewYork", new List<TimeWindow> { new TimeWindow { StartTime="07:00", EndTime="09:00" } } }
        }
            };
        }

        public void Save(AppConfigData config)
        {

            if (config == null)
                throw new ArgumentNullException("config");

            lock (_lock)
            {
                try
                {
                    if (!UserSession.IsLoggedIn)
                        throw new InvalidOperationException("User not logged in.");

                    int userId = UserSession.UserId;

                    config.UserID = userId;


                    _configRepo.SaveConfigByUserId(userId, config);

                    _cachedConfig[userId] = config;

                    Debug.WriteLine($"Config saved for UserId: {userId}");
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show(
                        "Failed to save configuration:\n\n" + ex.Message,
                        "Save Error",
                        MessageBoxButton.OK,
                        MessageBoxImage.Error);

                    throw;
                }
            }

        }

        public void SaveNewConfig(int targetUserId, AppConfigData newConfig)
        {
            // Save to database
            _configRepo.SaveConfigByUserId(targetUserId, newConfig);

            // Update local memory cache so the app doesn't load stale data
            _cachedConfig[targetUserId] = newConfig;

            // Fire the event to tell MainWindow to push to MT5!
            OnConfigUpdated?.Invoke();
        }

    }
}
