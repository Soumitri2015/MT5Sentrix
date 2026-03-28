using Sentrix.Models;
using System;
using System.Collections.Generic;
using System.Configuration;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;

namespace Sentrix
{
    public class AppConfig
    {
        private static AppConfigData _cached;
        public static void Initialize()
        {
            using var scope = ((App)System.Windows.Application.Current).ServiceProvider.CreateScope();
            var helper = scope.ServiceProvider.GetRequiredService<ConfigHelper>();
            _cached = helper.Load();
        }
        private static AppConfigData Config => _cached;


        // private static AppConfigData Config => new ConfigHelper().Load();

        public static int MaxTradesPerDay => Config.MaxTradesPerDay;

        public static string LockMessage => Config.LockMessage;

        public static int MaxTradesPerSession => Config.MaxTradesPerSession;

        public static double LossPercentValue => Config.LossPercentValue;

        public static bool CloseTradesOutsideSession => Config.CloseTradesOutsideSession;

        public static Dictionary<string, List<TimeWindow>> TradingSessions => Config.TradingSessions;

        public static string TessDataPath =>
            ConfigurationManager.AppSettings["TessDataPath"] ?? "./tessdata";

        public static string CropImagePath =>
            ConfigurationManager.AppSettings["CropImagePath"] ?? "./temp_crop.png";
    }
}
