using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using mt5_term_api;
using Sentrix.Repositories;
using Sentrix.UIPages;
using System;
using System.Configuration;
using System.Data;
using System.Windows;


namespace Sentrix
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        public IServiceProvider ServiceProvider { get; private set; }
        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);
            var services = new ServiceCollection();
            var connectionString =
                                ConfigurationManager.ConnectionStrings["MyDbConnection"].ConnectionString;
            services.AddDbContextFactory<ApplicationDBContext>(options =>
            options.UseSqlServer(connectionString));
            services.AddScoped<UserRepository>();
            services.AddScoped<ConfigRepository>();
            services.AddScoped<ConfigHelper>();
            services.AddScoped<AlertService>();
            services.AddTransient<AdminPanelWindow>();
            services.AddTransient<LoginSignup>();

            services.AddScoped<PositionRepository>();
            services.AddSingleton<MainWindow>();
            ServiceProvider = services.BuildServiceProvider();
            var mainWindow = ServiceProvider.GetRequiredService<LoginSignup>();

            mainWindow.Show();
        }
    }

}