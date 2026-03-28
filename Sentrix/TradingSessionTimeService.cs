using Sentrix.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentrix
{
    public class TradingSessionTimeService
    {
        private readonly AppConfigData _appConfigData;

        public TradingSessionTimeService(AppConfigData appConfigData)
        {
            _appConfigData = appConfigData;
        }



        //public bool IsTradingAllowed(string sessionName, DateTime nowLocal)
        //{

        //    if (_appConfigData.TradingSessions == null ||
        //        _appConfigData.TradingSessions.Count == 0)
        //    {

        //        return false;
        //    }

        //    if (!_appConfigData.TradingSessions.TryGetValue(sessionName, out var windows))
        //    {

        //        return true;
        //    }
        //    if (windows == null || windows.Count != 1)
        //    {

        //        return true;
        //    }


        //    var window = windows[0];
        //    //Debug.WriteLine($"Raw StartTime string: '{window.StartTime}'");
        //    //Debug.WriteLine($"Raw EndTime string: '{window.EndTime}'");

        //    if (!TimeSpan.TryParseExact(window.StartTime, @"hh\:mm", null, out var start))
        //    {

        //        return false;

        //    }

        //    if (!TimeSpan.TryParseExact(window.EndTime, @"hh\:mm", null, out var end))
        //    {

        //        return false;
        //    }
        //    TimeSpan now = nowLocal.TimeOfDay;


        //    bool result;


        //    if (start <= end)
        //    {

        //        result = now >= start && now <= end;
        //    }
        //    else
        //    {

        //        result = now >= start || now <= end;
        //    }



        //    return result;
        //}

        public bool IsTradingAllowed(string sessionName, DateTime nowLoacal)
        {
            if(string.IsNullOrEmpty(sessionName))
                return false;
            if(_appConfigData.TradingSessions == null ||
                _appConfigData.TradingSessions.Count == 0)
                return false;

            var normalizedSessionName = AlertService.NormaliZeSession(sessionName);

            if (!_appConfigData.TradingSessions.TryGetValue(sessionName.Replace(" ",""), out var windows))
                return false;   
                if (windows == null || windows.Count == 0)
                   return false;
               bool sessionMatched = false;
            var now = nowLoacal.TimeOfDay;
            foreach (var window in windows)
                {
                    if (!TimeSpan.TryParseExact(window.StartTime, @"hh\:mm", null, out var start))
                        continue;
                    if (!TimeSpan.TryParseExact(window.EndTime, @"hh\:mm", null, out var end))
                        continue;
                    bool isInWindow;

                    if (start <= end)
                    {
                        isInWindow = now >= start && now <= end;
                    }
                    else
                    {
                        isInWindow = now >= start || now <= end;
                    }
                   if(isInWindow)
                    return true;

            }
               
            
            return false;
        }

        //public bool IsCurrentTimeWithinAnySession(DateTime nowLocal)
        //{
        //    if (_appConfigData.TradingSessions == null ||
        //        _appConfigData.TradingSessions.Count == 0)
        //        return false;
        //    foreach (var session in _appConfigData.TradingSessions)
        //    {
        //        if (IsTradingAllowed(session.Key, nowLocal))
        //            return true;
        //    }
        //    return false;
        //}
        public bool IsCurrentTimeWithinAnySession(DateTime nowLocal)
        {
            if (_appConfigData.TradingSessions == null ||
                _appConfigData.TradingSessions.Count == 0)
                return false;
           var activeSessions = GetActiveSession(DateTime.UtcNow);
            if(string.IsNullOrEmpty(activeSessions))
                return false;
            return IsTradingAllowed(activeSessions, nowLocal);
        }

        public string GetActiveSession(DateTime utcNow)
        {
            var now = utcNow.TimeOfDay;

            string selectedSession = null;
            TimeSpan latestStart = TimeSpan.MinValue;

            bool IsInRange(TimeSpan start, TimeSpan end)
            {
                if (start <= end)
                    return now >= start && now <= end;
                else
                    return now >= start || now <= end;
            }

            void CheckSession(string name, TimeSpan start, TimeSpan end)
            {
                if (IsInRange(start, end))
                {
                    // pick the session with the latest start time
                    if (start > latestStart)
                    {
                        latestStart = start;
                        selectedSession = name;
                    }
                }
            }

            
            CheckSession("Tokyo", TimeSpan.FromHours(0), TimeSpan.FromHours(9));

            
            CheckSession("Singapore", TimeSpan.FromHours(1), TimeSpan.FromHours(9));

            
            CheckSession("Frankfurt", TimeSpan.FromHours(6), TimeSpan.FromHours(15));

            
            CheckSession("London", TimeSpan.FromHours(7), TimeSpan.FromHours(16));

            
            CheckSession("New York", TimeSpan.FromHours(13), TimeSpan.FromHours(22));

            return selectedSession;
        }
    }
}
