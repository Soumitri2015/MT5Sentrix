using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using mt5_term_api;
using Sentrix.Models;
using Sentrix.Repositories;
using Sentrix.UIPages;
using System;
using System.Configuration;
using System.Data;
using System.IO.Pipes;
using System.Windows;


namespace Sentrix
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : System.Windows.Application
    {
        public IServiceProvider ServiceProvider { get; private set; }
        private System.Windows.Forms.NotifyIcon _trayIcon;


             private static Mutex _mutex;
        private Thread _pipeServerThread;
        private const string AppMutexName = "SentriX_SingleInstance_Mutex";
        private const string AppPipeName = "SentriX_SingleInstance_Pipe";
        protected override void OnStartup(StartupEventArgs e)
        {

            _mutex = new Mutex(true, AppMutexName, out bool isNewInstance);
            if (!isNewInstance)
            {
                SendShowSignal();
                _mutex.Dispose();
                Shutdown();
                return;
            }
            base.OnStartup(e);

            StartPipeServer();
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
            SetupTrayIcon();
            //var mainWindow = ServiceProvider.GetRequiredService<LoginSignup>();

            //mainWindow.Show();

            var userRepo = ServiceProvider.GetRequiredService<UserRepository>();

            if (Sentrix.Properties.Settings.Default.RememberMe)
            {
                var savedEmail =Sentrix.Properties. Settings.Default.SavedEmail;
                var savedPassword =Sentrix.Properties. Settings.Default.SavedPassword;

                if (!string.IsNullOrWhiteSpace(savedEmail) &&
                    !string.IsNullOrWhiteSpace(savedPassword))
                {
                    var userId = userRepo.GetUser(savedEmail, savedPassword);

                    if (userId != -1)
                    {
                        // restore session
                        UserSession.SetUser(userId);
                        UserSession.SetUserRole(userRepo.GetUserRoleById(userId));

                        var mainWindow = ServiceProvider.GetRequiredService<MainWindow>();
                        MainWindow = mainWindow;
                        mainWindow.Show();
                        return;
                    }
                }
            }

            var loginWindow = ServiceProvider.GetRequiredService<LoginSignup>();
            MainWindow = loginWindow;
            loginWindow.Show();
        }

        private void StartPipeServer()
        {
            _pipeServerThread = new Thread(() =>
            {
                try
                {
                    while (true)
                    {
                        using var server = new System.IO.Pipes.NamedPipeServerStream(AppPipeName, PipeDirection.In, 1, PipeTransmissionMode.Byte, PipeOptions.None);

                        server.WaitForConnection(); // blocks until a second instance connects
                        server.Dispose();

                        // Marshal back to UI thread
                        Dispatcher.Invoke(ShowAppWindow);
                    }

                }
                catch (Exception)
                {

                    throw;
                }

            })
            { IsBackground = true };
           
            _pipeServerThread.Start();
        }
        private static void SendShowSignal()
        {
            try
            {
                using var client = new System.IO.Pipes.NamedPipeClientStream(".", AppPipeName, PipeDirection.Out);
                client.Connect(1000); // wait up to 1 second to connect
            }
            catch (Exception)
            {
                // If we fail to connect, it likely means the first instance isn't responsive. Just exit.
            }
        }

        private void SetupTrayIcon()
        {
            _trayIcon = new System.Windows.Forms.NotifyIcon();

            // Load your custom icon
            try
            {
                _trayIcon.Icon = new System.Drawing.Icon(@"C:\Users\soumy\source\repos\Sentrix\Sentrix\ChatGPT Image Dec 22, 2025, 04_42_04 PM.ico");
            }
            catch (Exception)
            {
                // Fallback to shield if the file is moved or missing
                _trayIcon.Icon = SystemIcons.Shield;
            }

            _trayIcon.Text = "SentriX Guardian";
            _trayIcon.Visible = true;

            var menu = new System.Windows.Forms.ContextMenuStrip();

            menu.Items.Add("Open SentriX", null, (s, e) => ShowAppWindow());
            menu.Items.Add("-"); // Adds a separator line
            menu.Items.Add("Exit", null, (s, e) => ExitApplication());

            _trayIcon.ContextMenuStrip = menu;

            _trayIcon.DoubleClick += (s, e) => ShowAppWindow();
        }

        private void ShowAppWindow()
        {
            // Smart window detection: Bring whichever window is active/hidden to the front
            bool windowFound = false;

            foreach (Window win in System.Windows.Application.Current.Windows)
            {
                if (win is MainWindow || win is LoginSignup)
                {
                    win.Show();
                    if (win.WindowState == WindowState.Minimized)
                        win.WindowState = WindowState.Normal;

                    win.Activate();
                    windowFound = true;
                    break;
                }
            }

            // Fallback in case everything is fully closed but the app is still running
            if (!windowFound)
            {
                var loginWindow = ServiceProvider.GetRequiredService<LoginSignup>();
                loginWindow.Show();
            }
        }

        private void ExitApplication()
        {
            // Clean up the tray icon before quitting
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }

            // Explicitly force the app to shut down
            System.Windows.Application.Current.Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            // Catch-all cleanup if the app exits another way
            if (_trayIcon != null)
            {
                _trayIcon.Visible = false;
                _trayIcon.Dispose();
            }
            base.OnExit(e);
        }
    }

}