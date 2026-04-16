using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

using System.Configuration;

namespace Sentrix.Properties
{
    public sealed class Settings : ApplicationSettingsBase
    {
        private static Settings defaultInstance =
            (Settings)Synchronized(new Settings());

        public static Settings Default => defaultInstance;

        [UserScopedSetting]
        [DefaultSettingValue("false")]
        public bool RememberMe
        {
            get => (bool)this[nameof(RememberMe)];
            set => this[nameof(RememberMe)] = value;
        }

        [UserScopedSetting]
        [DefaultSettingValue("")]
        public string SavedEmail
        {
            get => (string)this[nameof(SavedEmail)];
            set => this[nameof(SavedEmail)] = value;
        }

        [UserScopedSetting]
        [DefaultSettingValue("")]
        public string SavedPassword
        {
            get => (string)this[nameof(SavedPassword)];
            set => this[nameof(SavedPassword)] = value;
        }
    }
}
