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

            if (!_appConfigData.TradingSessions.TryGetValue(sessionName.Replace(" ","").ToLower(), out var windows))
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
        public async Task<bool> IsCurrentTimeWithinAnySession(DateTime nowLocal)
        {

            bool isMasterWindow = IsWithMasterESTWindow();

            bool isCustomSessionActive  = false;
            if(_appConfigData.TradingSessions != null && _appConfigData.TradingSessions.Count > 0)
            {
                var activeSessionsName =await  GetActiveSession(DateTime.UtcNow);
                if(!string.IsNullOrEmpty(activeSessionsName))
                    isCustomSessionActive =  IsTradingAllowed(activeSessionsName,nowLocal);
            }
              
            //var activeSessions = GetActiveSession(DateTime.UtcNow);
            //if(string.IsNullOrEmpty(activeSessions))
            //    return false;
            //return IsTradingAllowed(activeSessions, nowLocal);

            return isMasterWindow || isCustomSessionActive;
        }

        public async Task<string> GetActiveSession(DateTime utcNow)
        {
            // 1. Convert UTC to Eastern Time (Automatically handles EST/EDT shifts)
            TimeZoneInfo easternZone;
            try
            {
                // Standard ID for Windows environments
                easternZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                // Fallback for Linux/macOS environments (if applicable)
                easternZone = TimeZoneInfo.FindSystemTimeZoneById("America/New_York");
            }

            DateTime easternTime = TimeZoneInfo.ConvertTimeFromUtc(utcNow, easternZone);
            TimeSpan now = easternTime.TimeOfDay;

            // 2. Define the hardcoded ET windows
            TimeSpan londonStart = new TimeSpan(2, 0, 0);   // 2:00 AM ET
            TimeSpan londonEnd = new TimeSpan(7, 59, 59);   // 7:59:59 AM ET

            TimeSpan nyStart = new TimeSpan(8, 0, 0);       // 8:00 AM ET
            TimeSpan nyEnd = new TimeSpan(11, 30, 0);       // 11:30 AM ET

            // 3. Evaluate current time against the windows
            if (now >= londonStart && now <= londonEnd)
            {
                return "London";
            }
            else if (now >= nyStart && now <= nyEnd)
            {
                return "NewYork"; // Matches the MQL5 exact string (no space)
            }

            // Return null (or "None") if outside allowed trading hours
            return "Frankfurt";
        }

        public bool IsWithMasterESTWindow()
        {
            try
            {
                DateTime utcNow = DateTime.UtcNow;

                TimeZoneInfo estZone = TimeZoneInfo.FindSystemTimeZoneById("Eastern Standard Time");

                DateTime estTime = TimeZoneInfo.ConvertTimeFromUtc(utcNow, estZone);

                

                TimeSpan startTime = new TimeSpan(2, 0, 0); // 1:00 PM EST
                TimeSpan endTime = new TimeSpan(11, 30, 0); // 10:00 PM EST

                return estTime.TimeOfDay >= startTime && estTime.TimeOfDay <= endTime;
            }
            catch (Exception ex)
            {

                System.Diagnostics.Debug.WriteLine($"EST Window Check Error: {ex.Message}");
                return false;
            }
        }
    }
}
