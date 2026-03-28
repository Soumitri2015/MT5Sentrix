using Sentrix.EntityModel;
using Sentrix.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentrix.Repositories
{
    
    public class ConfigRepository
    {
        public ApplicationDBContext _conn;

        public ConfigRepository(ApplicationDBContext conn)
        {
            _conn = conn;
        }

        public AppConfigData GetConfigDatabyUserId(int userId)
        {
            try
            {
                var config = _conn.TradingConfigs.Where(x => x.UserId == userId).FirstOrDefault();

                var windows = _conn.TradingSessionDefinitions.Where(x => x.UserId == userId).ToList();

                if (config == null && !windows.Any())
                    return null;
                var appConfigData = new AppConfigData
                {
                    UserID = userId,
                    TradingSessions = new Dictionary<string, List<TimeWindow>>()
                };

                if (config != null)
                {

                    appConfigData = new AppConfigData
                    {
                        UserID = userId,
                        MaxTradesPerDay = config.MaxTradesPerDay,
                        MaxTradesPerSession = config.MaxTradesPerSession,
                        LossPercentValue = (double)config.LossPercentvalue,
                        LockMessage = config.LockMessage,
                        CloseTradesOutsideSession = config.CloseTradesOutsideSession,
                        TradingSessions = new Dictionary<string, List<TimeWindow>>()
                    };
                }




                if (windows != null && windows.Any())
                {
                    var grouped = windows.GroupBy(x => x.SessionName);

                    foreach (var group in grouped)
                    {
                        var normalizedKey = AlertService.NormaliZeSession(group.Key);
                        var timeWindows = group.Select(x => new TimeWindow
                        {
                            StartTime = x.StartTime.ToString(@"hh\:mm"),
                            EndTime = x.EndTime.ToString(@"hh\:mm")
                        }).ToList();

                        if (appConfigData.TradingSessions.ContainsKey(normalizedKey))
                        {
                            appConfigData.TradingSessions[normalizedKey].AddRange(timeWindows);
                        }
                        else
                        {
                            appConfigData.TradingSessions.Add(normalizedKey, timeWindows);
                        }   
                    }
                }

                return appConfigData;
            }
            catch (Exception ex)
            {

                Debug.WriteLine($"config data by user id exceptions{ex.Message}");
                return null;
            }


        }

        public void SaveConfigByUserId(int userId, AppConfigData config)
        {

            try
            {
                var existingConfig = _conn.TradingConfigs
                                      .FirstOrDefault(x => x.UserId == userId);

                if (existingConfig == null)
                {
                    existingConfig = new TradingConfigs
                    {
                        UserId = userId
                    };

                    _conn.TradingConfigs.Add(existingConfig);
                }


                existingConfig.MaxTradesPerDay = config.MaxTradesPerDay;
                existingConfig.MaxTradesPerSession = config.MaxTradesPerSession;
                existingConfig.LossPercentvalue = (decimal)config.LossPercentValue;
                existingConfig.LockMessage = config.LockMessage;
                existingConfig.CloseTradesOutsideSession = config.CloseTradesOutsideSession;




                var oldSessions = _conn.TradingSessionDefinitions
                                       .Where(x => x.UserId == userId)
                                       .ToList();

                if (oldSessions.Any())
                    _conn.TradingSessionDefinitions.RemoveRange(oldSessions);


                if (config.TradingSessions != null)
                {
                    foreach (var session in config.TradingSessions)
                    {
                        string sessionName = session.Key;

                        foreach (var window in session.Value)
                        {
                            _conn.TradingSessionDefinitions.Add(new TradingSessionDefinitions
                            {
                                UserId = userId,
                                SessionName = sessionName,
                                StartTime = TimeSpan.Parse(window.StartTime),
                                EndTime = TimeSpan.Parse(window.EndTime)
                            });
                        }
                    }
                }

                _conn.SaveChanges();
            }
            catch (Exception ex)
            {

                Debug.Write("SaveConfigByUserId ex -------->" + ex.ToString());
            }

        }
    }
}
