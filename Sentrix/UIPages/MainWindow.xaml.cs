using Microsoft.Extensions.DependencyInjection;
using Microsoft.Identity.Client;
using Microsoft.Win32;
using Sentrix.EntityModel;
using Sentrix.Models;
using Sentrix.Repositories;
using Sentrix.UIPages;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;
using System.Windows.Threading;
using static Vanara.PInvoke.Kernel32;
using EventLog = Sentrix.Models.EventLog;
using Path = System.IO.Path;

namespace Sentrix
{
    /// <summary>
    /// Interaction logic for MainWindow.xaml
    /// </summary>
    public partial class MainWindow : Window
    {
        #region Property
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private readonly object _sessionLockThread = new object();
        private readonly object _alertLock = new object();

        private Process targetProcess;
        private IntPtr targetHandle = IntPtr.Zero;
        private IntPtr _hookID = IntPtr.Zero;
        private OverlayWindow overlay;
        private System.Threading.Timer foregroundCheckTimer;

        private System.Windows.Forms.NotifyIcon trayIcon;
        private RECT placeOrderbutton;
        private GlobalMouseHook globalMouseHook = new GlobalMouseHook();

        private AppConfigData _config;
        private static Mutex _appMutex;
        private TradingSessionTimeService _tradingSessioinTimeService;
        private CancellationTokenSource _alertCts;
        private LowLevelMouseProc _proc;
        private DateTime _sessionResetDate = DateTime.Today;

        private Dictionary<string, TradingSession> _sessionToday = new Dictionary<string, TradingSession>(StringComparer.OrdinalIgnoreCase);

        private string _currentSession = null;

        private readonly string _positionLogDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SentriX", "PositionLogs");
        private static readonly string TradeStateFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "SentriX", "trade_state.json");

        // Unified Lock States
        private bool _isManualBlockActive = false;
        private bool _closeInProgress = false;
        private bool _allowClose = false;

        private int _tradesToday = 0;
        private int tradeCount = 0;
        
        private int MAX_Trades_PER_SESSION = 0;

        private const int WH_MOUSE_LL = 14;
        private const int WM_LBUTTONDOWN = 0x0201;
        private const int WM_RBUTTONDOWN = 0x0204;
        private const int WM_MBUTTONDOWN = 0x0207;
        const uint WM_CLOSE = 0x0010;

        private double _lastBalance = 0.0;
        private double _lastEquity = 0.0;

        private readonly ConfigHelper _configHelper;
        private int userId = UserSession.UserId;
        private string userRole = UserSession.UserRole;
        PositionRepository _positionRepo;

        private MT5Service _mt5Service = new MT5Service();
        private MT5AutoAttachService _mT5AutoAttachService;
        private string _mt5DataPath;
        private HashSet<long> _closingTickets = new HashSet<long>();
        private bool _isCurrentlyLocked = false;
        #endregion

        #region Win32 API & Helpers

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool EnableWindow(IntPtr hWnd, bool bEnable);

        private delegate IntPtr LowLevelMouseProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelMouseProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("user32.dll")]
        private static extern IntPtr WindowFromPoint(POINT point);

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", ExactSpelling = true)]
        static extern IntPtr GetAncestor(IntPtr hwnd, uint gaFlags);

        [DllImport("user32.dll")]
        static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);

        [DllImport("user32.dll")]
        static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        static extern bool PostMessage(IntPtr hWnd, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        static extern bool GetCursorPos(out POINT p);

        delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [StructLayout(LayoutKind.Sequential)]
        public struct POINT { public int X; public int Y; }

        [StructLayout(LayoutKind.Sequential)]
        public struct MSLLHOOKSTRUCT { public POINT pt; public uint mouseData; public uint flags; public uint time; public IntPtr dwExtraInfo; }

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }
       
        #endregion
       
        #region Auto events
        protected override void OnClosing(CancelEventArgs e)
        {

            //e.Cancel = true;
            //MessageBox.Show("The SentriX Guardian cannot be closed while cTrader is monitored.", "Access Denied", MessageBoxButton.OK, MessageBoxImage.Stop);
            //base.OnClosing(e);

            if (!_allowClose)
            {
                e.Cancel = true;
                Hide();
                System.Windows.MessageBox.Show(
                      "Access Denied: You do not have permission to stop the Sentrix .",
                      "Sentrix Protection",
                      MessageBoxButton.OK,
                      MessageBoxImage.Stop);
                return;
            }

            trayIcon?.Dispose();
            base.OnClosing(e);
        }
        
        private void InitialiseAutoAttach()
        {
            _mt5DataPath = new SentrixInstallerService().FindMT5DataPathPublic();
            if (_mt5DataPath != null)
            {
                _mT5AutoAttachService = new MT5AutoAttachService(_mt5DataPath);
            }
        }
        #endregion

        #region Constructors & Initializers
        public MainWindow(ConfigHelper helper,PositionRepository positionrepo)
        {
            try
            {
                InitializeComponent();
                ApplyRolePermission();
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show("Failed to initialize components:\n" + ex.Message);
            }

            _configHelper = helper;
            _config = _configHelper.Load();
            _positionRepo = positionrepo;
            MAX_Trades_PER_SESSION = (_config?.MaxTradesPerSession > 0) ? _config.MaxTradesPerSession : AppConfig.MaxTradesPerSession;

            _tradingSessioinTimeService = new TradingSessionTimeService(_config);
            Loaded += (_, __) => UpdateSettingsButtonState();

            bool isNewInstance;
            _appMutex = new Mutex(true, "SentriXGuardian_Global_Mutex", out isNewInstance);
            if (!isNewInstance)
            {
                System.Windows.Application.Current.Shutdown();
                return;
            }

            StartIntercepting();

            this.Loaded += MainWindow_Loaded;
            this.Focus();
            this.KeyDown += Window_KeyDown;
            foregroundCheckTimer = new System.Threading.Timer(MonitorProcess, null, 0, 100);

            Task.Run(() =>
            {
                var installer = new SentrixInstallerService();
                var result = installer.InstallMT5Integration();
                if (!result.Success)
                {
                    Dispatcher.Invoke(() => StatusText.Text = result.Message);
                }
            });

            InitialiseAutoAttach();
            initialiseMT5Service();
            SetupTrayIcon();
        }
        #endregion

        #region UI Event Handlers
        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                // Block all mouse button clicks (left, right, middle)
                if (wParam == (IntPtr)WM_LBUTTONDOWN ||
                    wParam == (IntPtr)WM_RBUTTONDOWN ||
                    wParam == (IntPtr)WM_MBUTTONDOWN)
                {
                    MSLLHOOKSTRUCT hookStruct = Marshal.PtrToStructure<MSLLHOOKSTRUCT>(lParam);
                    IntPtr windowUnderMouse = WindowFromPoint(hookStruct.pt);

                    // Check if that window belongs to cTrader
                    if (IsChildOfTarget(windowUnderMouse))
                    {
                        if (this.overlay != null && this.overlay.BlockClicks)
                        {
                            // Show feedback without blocking
                            System.Windows.Application.Current.Dispatcher.BeginInvoke(new Action(() =>
                            {
                                StatusText.Text = "Click Intercepted!";
                                ShowBlockSessionAlert(_currentSession);
                                if (overlay != null && overlay.IsVisible)
                                {
                                    overlay.ShowTempAlert("Max X trades for this session reached  \r\n\r\n  Trading locked until next session");
                                }
                            }));

                            return (IntPtr)1; // Block the click
                        }
                    }
                }
            }
            return CallNextHookEx(_hookID, nCode, wParam, lParam);
        }
        private bool IsChildOfTarget(IntPtr hWnd)
        {
            if (targetHandle == IntPtr.Zero) return false;

            // Check if the clicked handle is exactly the cTrader window
            if (hWnd == targetHandle) return true;

            // Check if the clicked handle is a child of the cTrader window
            const int GAF_ROOT = 2;
            IntPtr root = GetAncestor(hWnd, GAF_ROOT);

            // Check process ID to be sure
            try
            {
                GetWindowThreadProcessId(hWnd, out uint clickedProcessId);
                return (root == targetHandle) || (clickedProcessId == (uint)targetProcess?.Id);
            }
            catch
            {
                return false;
            }
        }
        public void StartIntercepting()
        {
            _proc = HookCallback; // Keep reference to prevent GC
            using (Process curProcess = Process.GetCurrentProcess())
            using (ProcessModule curModule = curProcess.MainModule)
            {
                _hookID = SetWindowsHookEx(WH_MOUSE_LL, _proc, GetModuleHandle(curModule.ModuleName), 0);
            }
        }
        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            LoadDailyState();
            PositionControllerBottomRight();
        }
        private void Window_KeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {

            if (e.Key == Key.F8)
            {
                GetCursorPos(out POINT p);
                placeOrderbutton = new RECT
                {
                    Left = p.X - 120,
                    Top = p.Y - 25,
                    Right = p.X + 120,
                    Bottom = p.Y + 25
                };
                StatusText.Text = "Place Order button calibrated";
            }
        }
        private void AdminPanelBtn_Click(object sender, RoutedEventArgs e)
        {
            var adminWindow = ((App)System.Windows.Application.Current).ServiceProvider.GetRequiredService<AdminPanelWindow>();
            adminWindow.Owner = this;

            adminWindow.Show();


        }

        private void MinimizeBtn_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.WindowState = WindowState.Minimized;
                Hide();
            }
            catch (Exception ex)
            {
            }
        }
        private void SettingsBtn_Click(object sender, RoutedEventArgs e)
        {
            if (!SettingsBtn.IsEnabled)
            {
                System.Windows.MessageBox.Show("Settings cannot be opened while outside of time window.",
                    "Session Active",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return;
            }

            var pwdWindow = new ConfigPasswordWindow();
            pwdWindow.ShowDialog();
            if (pwdWindow.IsValid)
            {
                var scope = ((App)System.Windows.Application.Current).ServiceProvider.GetRequiredService<ConfigEditorWindow>();
                scope.ShowDialog();
            }

            return;
        }

        private async void BlockClicks_Click(object sender, RoutedEventArgs e)
        {
            if (!CheckAttached()) return;

            _isManualBlockActive = !_isManualBlockActive;

            if (_isManualBlockActive)
            {
                tradeCount = 0;
                GlobalMouseHook.MouseDown += OnMouseDown;
                GlobalMouseHook.Start();
            }
            else
            {
                GlobalMouseHook.MouseDown -= OnMouseDown;
                GlobalMouseHook.Stop();
            }

            // Let the central lock logic re-evaluate
            await EvaluateAndApplyLockState(_currentSession);
        }
        private void CloseTrader_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                this.WindowState = WindowState.Minimized;
                if (targetProcess != null && !targetProcess.HasExited)
                {
                    targetProcess.Kill();
                    CleanupTarget();

                }
            }
            catch (Exception ex)
            {

                System.Windows.MessageBox.Show("Failed to close cTrader:\n" + ex.Message);
            }
        }

        private async void EventLogBtn_Click(object sender, RoutedEventArgs e)
        {
            var timeLine = await GetTimelineEvents(userId);

            EventLogs popup = new  EventLogs(timeLine)
            {
                Owner = this

            };

            popup.ShowDialog();
        }

        async Task< List<EventLog>> ReadTodayEventLog()
        {
            try
            {
                string today = DateTime.Today.ToString("yyyy-MM-dd");
                string filePath = Path.Combine(_positionLogDir, $"{today}.json");

                if (!File.Exists(filePath))
                    return new List<EventLog>();

                var data = JsonSerializer.Deserialize<DailyTradeData>(
                    File.ReadAllText(filePath));

                if (data?.events == null)
                    return new List<EventLog>();

                foreach (var ev in data.events)
                {
                    ev.DisplayDateTime = $"{data.date} {ev.Timestamp}";
                }

                return data.events;
            }
            catch
            {
                return new List<EventLog>();
            }
        }

        public async Task<List<EventDateGroup>> GetTimelineEvents(int userId)
        {
            var flatEvents = await _positionRepo.GetEventsByUser(userId);

            var grouped = flatEvents.GroupBy(e=> e.DisplayDateTime).OrderByDescending(g=> g.Key).Select(dateGroup=> new EventDateGroup
            {
                Date = dateGroup.Key,
                Hours = dateGroup.GroupBy(e => e.Timestamp.Substring(0,2)).OrderByDescending(h=> h.Key).Select(hourGroup => new EventHourGroup
                {
                    Hour = hourGroup.Key,
                    Events = hourGroup.ToList()
                }).ToList()
            }).ToList();
            return grouped;
        }



        #endregion

        #region Core Logic Methods

        private bool CheckAttached()
        {
            if (targetHandle == IntPtr.Zero)
            {
                System.Windows.MessageBox.Show("Attach to a target window first.");
                return false;
            }
            return true;
        }
        private void OnMouseDown(int x, int y)
        {
            if (!IsInsidePlaceOrder(x, y)) return;

            tradeCount++;

            Dispatcher.Invoke(() =>
            {
                StatusText.Text = $"Trades today: {tradeCount}";

                //if (tradeCount >= 2)
                //{
                //    CreateOverlayInstance();
                //}
            });
        }
        bool IsInsidePlaceOrder(int x, int y)
        {
            return x >= placeOrderbutton.Left &&
                   x <= placeOrderbutton.Right &&
                   y >= placeOrderbutton.Top &&
                   y <= placeOrderbutton.Bottom;
        }
        private void initialiseMT5Service()
        {
         
            _mt5Service.OnDataReceived += (account, positions) => OnMT5DataReceived(account, positions);
            _mt5Service.Start();
        }
        private void GetManifestresourcename()
        {
            var names = Assembly.GetExecutingAssembly().GetManifestResourceNames();
            foreach (var n in names)
                Debug.WriteLine(n);
        }

        private void MonitorProcess(object state)
        {
            // Check if MT5 is still alive
            if (targetProcess != null)
            {
                targetProcess.Refresh();
                if (targetProcess.HasExited)
                {
                    //_mt5Service.Stop();
                    _mT5AutoAttachService?.Reset();
                    CleanupTarget();
                    targetProcess = null;
                    return;
                }
            }

            UpdateSettingsButtonState();

            // Look for MT5 process (terminal64 = 64-bit MT5, terminal = 32-bit)
            if (targetProcess == null)
            {
                var processes = Process.GetProcessesByName("terminal64");
                if (processes.Length == 0)
                    processes = Process.GetProcessesByName("terminal");

                var validProcess = processes.FirstOrDefault(
                    p => p.MainWindowHandle != IntPtr.Zero);

                if (validProcess != null)
                {
                    targetProcess = validProcess;
                    targetHandle = validProcess.MainWindowHandle;

                    string exePath = MT5AutoAttachService.GetMT5ExePath(validProcess);
                    _mT5AutoAttachService.EnsureEAAttached(validProcess, exePath);
                    //if (!_mt5Service.IsConnected)
                    //    _mt5Service.Restart();

                    Dispatcher.Invoke(async () =>
                    {
                        this.Show();
                        StatusText.Text = "MT5 detected — waiting for EA...";
                        SettingsBtn.IsEnabled = true;

                        if (overlay == null)
                        {
                            overlay = new OverlayWindow(targetHandle);
                            overlay.Show();
                            await EvaluateAndApplyLockState(_currentSession);
                        }
                    });

                    // MT5Service is already listening on the pipe from app start.
                    // Status will update to "Connected" when EA sends first packet.
                }
            }

            // Overlay follow logic — completely unchanged from cTrader version
            if (targetProcess != null && overlay != null)
            {
                try
                {
                    Dispatcher.Invoke(() =>
                    {
                        if (!overlay.IsLoaded) return;

                        IntPtr foreground = GetForegroundWindow();
                        bool isTargetActive = foreground == targetHandle;

                        if (isTargetActive)
                        {
                            if (!overlay.IsVisible) overlay.Show();
                            overlay.FollowTargetZOrder();
                        }
                        else
                        {
                            if (!overlay.BlockClicks && overlay.IsVisible)
                                overlay.Hide();
                        }
                    });
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Overlay check failed: {ex.Message}");
                }
            }

            // Update status text to reflect pipe connection state
            if (targetProcess != null)
            {
                Dispatcher.Invoke(() =>
                {
                    if (_isCurrentlyLocked)
                        return;

                    if(!string.IsNullOrEmpty(_currentSession)&& _mt5Service.IsConnected) return;
                    if (_mt5Service.IsConnected)
                        StatusText.Text = "MT5 Connected ✓";
                    else
                        StatusText.Text = "MT5 detected — waiting for EA...";
                });
            }
        }



        private async Task OnMT5DataReceived(MT5AccountInfo account, List<MT5Position> positions)
        {
            if (targetProcess == null || targetProcess.HasExited) return;

            try
            {
                int currentPositionCount = positions.Count;
                var sessionTimeService = new TradingSessionTimeService(_config);
                string activeSessionName = sessionTimeService.GetActiveSession(DateTime.UtcNow);

                if (account != null) await CheckLossRule(account.Balance, account.Equity);
                if (_config == null) _config = _configHelper.Load();

                UpdateSettingsButtonState();

                // Detect Session Change
                if (!string.IsNullOrEmpty(activeSessionName) && activeSessionName != _currentSession)
                {
                    _currentSession = activeSessionName;
                    await  RegisterSession(activeSessionName);
                }

                HandleTradeSessionDayReset(currentPositionCount);

                // Re-evaluate limits based on incoming data
                await System.Windows.Application.Current.Dispatcher.Invoke(async () => await EvaluateAndApplyLockState(_currentSession));

                var positionRows = positions.ConvertAll(p =>
                                    $"{p.Symbol}|{p.Lots:F2}|{p.Direction}|{p.EntryPrice:F5}|" +
                                    $"{p.TakeProfit:F5}|{p.StopLoss:F5}|" +
                                    $"{p.OpenTimeUtc:dd/MM/yyyy HH:mm:ss}|{p.Profit:F2}|" +
                                    $"{p.Ticket}|" + // <-- Added Ticket here
                                    $"{activeSessionName}");

                await System.Windows.Application.Current.Dispatcher.BeginInvoke(async() =>
                     await SaveDailyData(positionRows, activeSessionName));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("OnMT5DataReceived error: " + ex.Message);
            }
        }





        private async Task RegisterSession(string sessionName)
        {
            try
            {
                if (string.IsNullOrEmpty(sessionName)) return;

                lock (_sessionLockThread)
                {
                    if (!_sessionToday.ContainsKey(sessionName))
                    {
                        _sessionToday[sessionName] = new TradingSession { Name = sessionName, Trades = 0 };
                    }
                }

                // Immediately evaluate the new session's lock state
                await Dispatcher.Invoke(async () => await EvaluateAndApplyLockState(sessionName));
            }
            catch (Exception ex)
            {
                Debug.WriteLine("RegisterSession error: " + ex);
            }
        }

        async Task SaveDailyData(List<string> positionRows, string currentSession, string eventMessage = null)
        {
            try
            {
                DateTime todayDate = DateTime.Today;

                if (!string.IsNullOrEmpty(eventMessage))
                   await _positionRepo.SaveEventAsync(userId, eventMessage);

                // 1. Fetch current database positions BEFORE processing live rows
                var todayPositions =await  _positionRepo.GetTodayPositionsAsync(userId);

                // Create a fast lookup for existing DB positions to prevent spam
                HashSet<string> existingDbKeys = new HashSet<string>(
                    todayPositions.Select(db => $"{db.Symbol}|{db.EntryPrice}|{db.CreatedUtc:o}")
                );

                HashSet<string> liveTradeKeys = new HashSet<string>();

                if (positionRows != null)
                {
                    foreach (var row in positionRows)
                    {
                        TradeEntry trade = ParseTrade(row);
                        if (trade == null) continue;

                        string key = $"{trade.Symbol}|{trade.Entry}|{trade.CreatedUtc:o}";
                        liveTradeKeys.Add(key);

                        DateTime rawTradeTime = trade.CreatedUtc;

                        bool isToday = (rawTradeTime.Year == todayDate.Year && rawTradeTime.DayOfYear == todayDate.DayOfYear);

                        if (!isToday)
                        {
                            // If MT5 server time crossed midnight but local hasn't (or vice versa), 
                            // we allow a fallback check to see if the trade is from the last 16 hours 
                            // to protect night sessions.
                            TimeSpan age = DateTime.UtcNow - rawTradeTime;
                            if (age.TotalHours > -12 && age.TotalHours < 16)
                            {
                                isToday = true;
                            }
                            else
                            {
                                Debug.WriteLine($"[SKIPPED] Old Trade Date: {rawTradeTime:dd-MM-yyyy} vs Today: {todayDate:dd-MM-yyyy}");
                                continue;
                            }
                        }

                        string session = row.Split('|').Last().Trim();
                        if (session == "OffSession") continue;

                        var position = new Positions
                        {
                            UserId = userId,
                            SessionName = session,
                            Symbol = trade.Symbol,
                            Lots = (decimal)trade.Lots,
                            Direction = trade.Direction,
                            EntryPrice = (decimal)trade.Entry,
                            TakeProfit = (decimal)(trade.TP ?? 0),
                            StopLoss = (decimal)(trade.SL ?? 0),
                            CreatedUtc = trade.CreatedUtc,
                            NetProfit = (decimal)trade.Net,
                            Status = trade.Status ?? "Open",
                            TradeDate = trade.CreatedUtc,
                            Ticket = trade.Ticket
                            
                        };

                        await _positionRepo.UpsertPosition(position);

                        // 2. LOGGING: ONLY IF TRULY NEW
                        if (!existingDbKeys.Contains(key))
                        {
                            await _positionRepo.SaveEventAsync(userId, $"➕ New {trade.Direction} on {trade.Symbol} in {session}");
                        }
                    }
                }

                // 3. LOGGING: IDENTIFY AND LOG CLOSURES
                foreach (var dbPosition in todayPositions)
                {
                    string key = $"{dbPosition.Symbol}|{dbPosition.EntryPrice}|{dbPosition.CreatedUtc:o}";
                    if (!liveTradeKeys.Contains(key))
                    {
                        await _positionRepo.MarkPositionDeletedAsync(userId, dbPosition.Symbol, dbPosition.EntryPrice, dbPosition.CreatedUtc,dbPosition.Ticket);

                        // Log the closure here
                        await _positionRepo.SaveEventAsync(userId, $"❌ Closed {dbPosition.Direction} on {dbPosition.Symbol} in {dbPosition.SessionName}");
                    }
                }

                var todayTradesBySession = await _positionRepo.GetTodayTradeCountBySessionAsync(userId);
                lock (_sessionLockThread)
                {
                    _tradesToday = todayTradesBySession.Values.Sum();
                    foreach (var kv in todayTradesBySession)
                    {
                        if (_sessionToday.ContainsKey(kv.Key))
                            _sessionToday[kv.Key].Trades = kv.Value;
                    }
                    SaveDailyState();
                }

               await  Dispatcher.Invoke(async () => await  EvaluateAndApplyLockState(currentSession));
            }
            catch (Exception ex)
            {
                System.Windows.MessageBox.Show($"Error saving daily data: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        TradeEntry ParseTrade(string row)
        {
            var p = row.Split('|');
            if (p.Length < 8)
                return null;

            DateTime createdUtc;

            string dateText = p[6].Trim();

            if (!DateTime.TryParseExact(dateText,
     new[] {
         "dd/MM/yyyy HH:mm:ss",
         "MM/dd/yyyy HH:mm:ss",
         "dd-MM-yyyy HH:mm:ss",   // ← add this
         "yyyy-MM-dd HH:mm:ss",   // ← and this
         "yyyy-MM-ddTHH:mm:ss"    // ← ISO format fallback
     },
     CultureInfo.InvariantCulture,
     DateTimeStyles.None,
     out createdUtc))
                return null;


            return new TradeEntry
            {
                Symbol = p[0].Trim(),

                Lots = ParseDoubleSafe(
                    p[1].Replace("Lots", "")
                ),

                Direction = p[2].Trim(),

                Entry = ParseDoubleSafe(p[3]),

                TP = p[4].Trim() == "-" || p[4].Trim().Equals("NA", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : ParseDoubleSafe(p[4]),

                SL = p[5].Trim() == "-" || p[5].Trim().Equals("NA", StringComparison.OrdinalIgnoreCase)
                    ? null
                    : ParseDoubleSafe(p[5]),

                CreatedUtc = DateTime.SpecifyKind(createdUtc, DateTimeKind.Utc),

                Net = ParseDoubleSafe(p[7]),
                // Replace this line inside the ParseTrade method:
                
                Ticket = long.TryParse(p[8].Trim(), out long t) ? (int)t : 0
                
            };
        }
        double ParseDoubleSafe(string raw)
        {
            if (string.IsNullOrWhiteSpace(raw))
                return 0;

            // Remove NBSP + normal spaces
            raw = raw
                .Replace("\u00A0", "")
                .Replace(" ", "")
                .Trim();

            // Remove currency symbols and junk
            raw = Regex.Replace(raw, @"[^0-9.,\-]", "");

            // Handle comma/dot conflicts
            if (raw.Contains(",") && raw.Contains("."))
            {
                if (raw.LastIndexOf(',') > raw.LastIndexOf('.'))
                    raw = raw.Replace(".", "").Replace(",", ".");
                else
                    raw = raw.Replace(",", "");
            }
            else if (raw.Contains(","))
            {
                raw = raw.Replace(",", ".");
            }

            return double.Parse(raw, CultureInfo.InvariantCulture);
        }
        private void SaveDailyState()
        {
            try
            {
                Models.DailyTradeState state;
                lock (_sessionLockThread)
                {
                    state = new Models.DailyTradeState
                    {
                        Date = DateTime.Today,
                        TradesToday = _tradesToday,
                        SessionTrades = _sessionToday.ToDictionary(s => s.Key, s => s.Value.Trades)
                    };
                }

                var dir = Path.GetDirectoryName(TradeStateFile);
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);

                File.WriteAllText(TradeStateFile, JsonSerializer.Serialize(state, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }

        void HandleTradeSessionDayReset(int currentPositions)
        {
            if (DateTime.Today != _sessionResetDate)
            {
                _sessionResetDate = DateTime.Today;
                _sessionToday.Clear();
                _currentSession = null;
                _tradesToday = 0;

                Dispatcher.Invoke(UpdateSettingsButtonState);

                overlay?.Close();
                overlay = null;

                StatusText.Text = "New trading day started. Limits reset.";
            }

            if (!string.IsNullOrEmpty(_currentSession) && !_sessionToday.ContainsKey(_currentSession))
            {
                RegisterSession(_currentSession);
            }
        }


        private async Task CheckLossRule(double balance, double equity)
        {
            if (balance <= 0 || equity <= 0) return;

            _lastBalance = balance;
            _lastEquity = equity;

            double lossPercent = ((balance - equity) / balance) * 100.0;
            Debug.WriteLine($"📊 Bal={balance:F2} Eq={equity:F2} | 📉 Loss%={lossPercent:F4}");

            if (lossPercent >= _config.LossPercentValue && !_closeInProgress)
                await TriggerTradeClosure();
        }
        private async Task TriggerTradeClosure()
        {
            _closeInProgress = true;
            try
            {
                await CloseHighestLossTrade();
                //CloseLosingTrades();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"❌ CloseLosingTrades error: {ex.Message}");
            }
            finally
            {
                _closeInProgress = false;
            }
        }

        private async Task CloseHighestLossTrade()
        {
            try
            {
                var positions = _mt5Service.GetOpenPositions();
                if (positions == null || positions.Count == 0) return;

                MT5Position worstPosition = null;
                double worstLoss = 0;

                foreach (var pos in positions)
                {
                    if (pos.Profit < worstLoss)
                    {
                        worstLoss = pos.Profit;
                        worstPosition = pos;
                    }
                }

                if (worstPosition == null) return;

                 _mt5Service.SendCommand($"{{\"CMD\":\"CLOSE\",\"Ticket\":{worstPosition.Ticket}}}");

                string positionRow = $"{worstPosition.Symbol}|{worstPosition.Direction}|{worstPosition.Lots:F2}|{worstPosition.EntryPrice:F5}|{worstPosition.StopLoss:F5}|{worstPosition.TakeProfit:F5}|{worstPosition.OpenTimeUtc:O}";

                await SaveDailyData(new List<string> { positionRow }, _currentSession, $"⚠️ Auto-closed worst trade. Loss: {worstLoss:F2}");
            }
            catch (Exception ex) { Debug.WriteLine($"CloseHighestLossTrade error: {ex.Message}"); }
        }


        // ── 6. CloseAllPositions  (replaces FlaUI button-clicking version) ─

        private void CloseAllPositions()
        {
            if (_closeInProgress) return;
            _closeInProgress = true;
            Debug.WriteLine("CloseAllPositions initiated... _closeInProgress " + _closeInProgress);

            Task.Run(() =>
            {
                try
                {
                    var positions = _mt5Service.GetOpenPositions();
                    if (positions.Count == 0) return;

                    Debug.WriteLine($"🔒 Closing {positions.Count} open positions via MT5...");

                    // MT5 close is done by sending an ORDER_TYPE_CLOSE_BY via the EA.
                    // We write a "CLOSE" command back through the pipe.
                    // The EA's OnChartEvent picks this up and calls OrderSend.
                    //
                    // For now we use the MQL5 ClosePosition via a reverse pipe message.
                    // See SentriXBridge.mq5 OnChartEvent section for the receive side.

                    foreach (var pos in positions)
                    {
                        ////SendCloseCommand(pos.Ticket);
                        //SendCloseOnce($"{{\"CMD\":\"CLOSE\",\"Ticket\":{pos.Ticket}}}");
                        //Thread.Sleep(200);   // small gap between closes

                        if (_closingTickets.Contains((long) pos.Ticket))
                            continue;

                        _closingTickets.Add((long)pos.Ticket);

                        _mt5Service.SendCommand($"{{\"CMD\":\"CLOSE\",\"Ticket\":{pos.Ticket}}}");
                        Task.Delay(200).Wait();   // small gap between closes

                        Debug.WriteLine($"🚀 CLOSE SENT ONCE for ticket {pos.Ticket}");
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"CloseAllPositions error: {ex.Message}");
                }
                finally
                {
                    _closeInProgress = false;
                }
            });
        }

       

        public void ShowBlockSessionAlert(string sessionName)
        {
            //string msg =
            //         $"You are blocked for the {sessionName} session.\n\nTrading is locked1.";

            string title = "Trading Locked";
            string msg =
                 $"You are blocked for the {sessionName} session.\n\nTrading is locked.";

            CancellationTokenSource localCts;

            lock (_alertLock)
            {
                // 🔥 Cancel previous alert completely
                _alertCts?.Cancel();
                _alertCts?.Dispose();

                _alertCts = new CancellationTokenSource();
                localCts = _alertCts;
            }

            Task.Run(() =>
            {
                try
                {
                    // If this alert was already replaced, stop
                    if (localCts.IsCancellationRequested)
                        return;

                    // 🔥 Remove previous alert from screen
                    CloseExistingAlert(title);

                    if (localCts.IsCancellationRequested)
                        return;

                    // 🔔 Show ONLY the latest alert

                    MessageBoxW(
                        IntPtr.Zero,
                        msg,
                        title,
                        0x00000010 | 0x00001000
                    );
                }
                catch
                {
                    // swallow safely
                }
            }, localCts.Token);


        }

        private void CloseExistingAlert(string title)
        {
            try
            {
                EnumWindows((hWnd, lParam) =>
                {
                    if (!IsWindowVisible(hWnd))
                        return true;

                    var sb = new StringBuilder(256);
                    GetWindowText(hWnd, sb, sb.Capacity);

                    if (sb.ToString() == title)
                    {
                        PostMessage(hWnd, WM_CLOSE, IntPtr.Zero, IntPtr.Zero);
                    }

                    return true;
                }, IntPtr.Zero);
            }
            catch (Exception)
            {

                throw;
            }

        }


        private void CleanupTarget()
        {
            if (targetHandle != IntPtr.Zero)
                EnableWindow(targetHandle, true);

            targetHandle = IntPtr.Zero;
            targetProcess = null;

            // FlaUI calls removed — nothing to dispose here for MT5

            Dispatcher.Invoke(() =>
            {
                StatusText.Text = "Waiting for MT5...";
                if (overlay != null)
                {
                    overlay.Close();
                    overlay = null;
                }
                this.Hide();
            });
        }




        private void SetupTrayIcon()
        {
            trayIcon = new System.Windows.Forms.NotifyIcon();

            trayIcon.Icon = SystemIcons.Shield;
            trayIcon.Text = "SentriX Guardian";
            trayIcon.Visible = true;

            var menu = new System.Windows.Forms.ContextMenuStrip();

            menu.Items.Add("Open", null, (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    Show();
                    WindowState = WindowState.Normal;
                    Activate();
                });
            });

            trayIcon.ContextMenuStrip = menu;

            trayIcon.DoubleClick += (s, e) =>
            {
                Dispatcher.Invoke(() =>
                {
                    Show();
                    WindowState = WindowState.Normal;
                    Activate();
                });
            };
        }

        private void PositionControllerBottomRight()
        {
            var workArea = SystemParameters.WorkArea;
            this.Left = workArea.Right - this.Width - 10; // 10px margin
            this.Top = workArea.Bottom - this.Height - 10;
            this.Topmost = true;
        }

        private void UpdateSettingsButtonState()
        {
            bool isWithinSession = _tradingSessioinTimeService.IsCurrentTimeWithinAnySession(DateTime.Now);

            bool isMetaraderRunning =
                targetProcess != null &&
                !targetProcess.HasExited &&
                targetHandle != IntPtr.Zero;

            Dispatcher.Invoke(() =>
            {
                SettingsBtn.IsEnabled = isWithinSession && !isMetaraderRunning;
            });
        }

        private void ApplyRolePermission()
        {
            if (string.Equals(userRole?.Trim(), "Admin User", StringComparison.Ordinal))
            {
                AdminPanelBtn.Visibility = Visibility.Visible;
            }
        }

        private void LoadDailyState()
        {

            try
            {
                if (!File.Exists(TradeStateFile))
                    return;

                var json = File.ReadAllText(TradeStateFile);
                if (string.IsNullOrEmpty(json))
                    return;
                var state = JsonSerializer.Deserialize<Models.DailyTradeState>(json);

                if (state == null)
                    return;


                if (state.Date != DateTime.Today)
                {
                    File.Delete(TradeStateFile);
                    return;
                }
                lock (_sessionLockThread)
                {

                    _tradesToday = state.TradesToday;
                    _sessionToday.Clear();

                    foreach (var kv in state.SessionTrades)
                    {
                        _sessionToday[kv.Key] = new TradingSession
                        {
                            Name = kv.Key,
                            Trades = kv.Value
                        };
                    }

                }
                Dispatcher.Invoke(() =>
                {

                    StatusText.Text = $"Restored {_tradesToday} trades from today.";
                });
            }
            catch (Exception ex)
            {

                throw;
            }

        }

        



        private const string PipeName = "SentriXBridge";

        private NamedPipeServerStream _pipe;
        private CancellationTokenSource _cts;
        private Task _readerTask;
        private readonly object _lock = new();

        // Latest snapshot — updated by background reader, read by Sentrix timer
        private MT5Payload _latest;

        // ── Public state ──────────────────────────────────────────────

        public bool IsConnected { get; private set; }

        /// <summary>Raised on the thread-pool whenever a fresh payload arrives.</summary>
        public event Action<MT5AccountInfo, List<MT5Position>> OnDataReceived;

        // ── Lifecycle ─────────────────────────────────────────────────

        /// <summary>
        /// Start listening.  Call once from MainWindow.  
        /// The EA connects as soon as MT5 is running.
        /// </summary>
       

       

        // ── Public data accessors (called by ExtractTradingData) ──────

        public MT5AccountInfo GetAccountInfo()
        {
            lock (_lock)
            {
                if (_latest == null) return null;
                return new MT5AccountInfo
                {
                    Login = _latest.Login,
                    Balance = _latest.Balance,
                    Equity = _latest.Equity,
                    Currency = _latest.Currency,
                    ServerTime = _latest.ServerTime
                };
            }
        }

        public List<MT5Position> GetOpenPositions()
        {
            lock (_lock)
            {
                if (_latest?.Positions == null) return new List<MT5Position>();
                // Convert Sentrix.MT5Position to Sentrix.MainWindow.MT5Position
                return _latest.Positions
                    .Select(p => new MT5Position
                    {
                        Symbol = p.Symbol,
                        Direction = p.Direction,
                        Lots = p.Lots,
                        EntryPrice = p.EntryPrice,
                        StopLoss = p.StopLoss,
                        TakeProfit = p.TakeProfit,
                        OpenTime = p.OpenTime,
                        Profit = p.Profit
                    })
                    .ToList();
            }
        }

        // ── Background reader loop ────────────────────────────────────

        private async Task ReaderLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                try
                {
                    // Create a new server-side pipe and wait for the EA to connect
                    _pipe = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        1,                              // max 1 EA instance
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    Debug.WriteLine("MT5Service: waiting for EA to connect...");
                    await _pipe.WaitForConnectionAsync(ct);

                    IsConnected = true;
                    Debug.WriteLine("MT5Service: EA connected.");

                    using var reader = new BinaryReader(_pipe, Encoding.UTF8, leaveOpen: true);

                    while (_pipe.IsConnected && !ct.IsCancellationRequested)
                    {
                        // EA sends: [int32 length][utf-8 json string]
                        int length = reader.ReadInt32();

                        if (length <= 0 || length > 1_048_576)   // sanity: max 1 MB
                        {
                            Debug.WriteLine($"MT5Service: bad packet length {length}, resetting.");
                            break;
                        }

                        byte[] buf = reader.ReadBytes(length);
                        if (buf.Length != length) break;

                        string json = Encoding.UTF8.GetString(buf);
                        ParseAndStore(json);
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (EndOfStreamException)
                {
                    Debug.WriteLine("MT5Service: pipe EOF — EA disconnected.");
                }
                catch (IOException ex)
                {
                    Debug.WriteLine($"MT5Service: IO error — {ex.Message}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MT5Service: unexpected error — {ex.Message}");
                }
                finally
                {
                    IsConnected = false;
                    try { _pipe?.Dispose(); } catch { }
                    _pipe = null;
                }

                if (!ct.IsCancellationRequested)
                {
                    // Wait 2 s before recreating the pipe (MT5 may still be launching)
                    await Task.Delay(2000, ct).ContinueWith(_ => { });
                }
            }

            Debug.WriteLine("MT5Service: reader loop exited.");
        }

     

        // ── JSON parsing ──────────────────────────────────────────────

        private void ParseAndStore(string json)
        {
            try
            {
                var payload = JsonSerializer.Deserialize<MT5Payload>(json,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (payload == null) return;

                lock (_lock)
                {
                    _latest = payload;
                }

                // Fire event on thread-pool so callers don't block the reader
                OnDataReceived?.Invoke(
                    GetAccountInfo(),
                    GetOpenPositions());
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"MT5Service: JSON parse error — {ex.Message}");
            }
        }


        private async Task EvaluateAndApplyLockState(string sessionName)
        {
            if (string.IsNullOrEmpty(sessionName)) return;

            bool isTimeAllowed = _tradingSessioinTimeService.IsTradingAllowed(sessionName, DateTime.Now);
            bool dayLimitReached = _tradesToday >= _config.MaxTradesPerDay;

            bool sessionLimitReached = false;
            lock (_sessionLockThread)
            {
                if (_sessionToday.TryGetValue(sessionName, out var sessionData))
                {
                    sessionLimitReached = sessionData.Trades >= _config.MaxTradesPerSession;
                }
            }

            bool shouldBeLocked = _isManualBlockActive || dayLimitReached || !isTimeAllowed || sessionLimitReached;

            if (shouldBeLocked)
            {
                string lockReason = "⛔ TRADING LOCKED ⛔";
                if (_isManualBlockActive) lockReason = "⛔ CLICKS BLOCKED (MANUAL) ⛔";
                else if (dayLimitReached) lockReason = "⛔ MAX TRADES FOR THE DAY REACHED ⛔";
                else if (!isTimeAllowed) lockReason = $"⛔ Outside allowed hours for {sessionName} ⛔";
                else if (sessionLimitReached) lockReason = $"⛔ MAX TRADES FOR {sessionName.ToUpper()} REACHED ⛔";

                // LOGGING: Log ONLY when transitioning into a blocked state
                if (!_isCurrentlyLocked)
                {
                   await  _positionRepo.SaveEventAsync(userId, lockReason);
                    _isCurrentlyLocked = true;
                }

                ApplyLock(lockReason);

                if (!isTimeAllowed && _config.CloseTradesOutsideSession)
                {
                    var positions = _mt5Service.GetOpenPositions();
                    if (positions != null && positions.Count > 0)
                    {
                        CloseAllPositions();
                    }
                }
            }
            else
            {
                // Reset state when unlocked
                if (_isCurrentlyLocked)
                {
                    _isCurrentlyLocked = false;
                }

                RemoveLock($"Session Active: {sessionName}");
            }
        }

        private void ApplyLock(string message)
        {
            //if (targetHandle != IntPtr.Zero) EnableWindow(targetHandle, false);

            if (overlay == null && targetHandle != IntPtr.Zero)
            {
                overlay = new OverlayWindow(targetHandle);
                overlay.Show();
            }

            if (overlay != null)
            {
                overlay.BlockClicks = true;
                overlay.FollowTargetZOrder();
            }

            StatusText.Text = message;
            BlockClicksBtn.Background = new SolidColorBrush(Colors.Red);
            BlockClicksBtn.Content = _isManualBlockActive ? "Unblock Clicks" : "Locked";
        }

        private void RemoveLock(string message)
        {
            if (targetHandle != IntPtr.Zero) EnableWindow(targetHandle, true);

            if (overlay != null)
            {
                overlay.BlockClicks = false;
                overlay.Hide();
            }

            StatusText.Text = message;
            BlockClicksBtn.Background = new SolidColorBrush(System.Windows.Media.Color.FromRgb(40, 160, 90));
            BlockClicksBtn.Content = "Block Clicks";
        }

        #region Models

        class TradingSession
        {
            public string Name { get; set; }
            public int Trades { get; set; }
        }


        public interface IMT5Service
        {
            bool Connect();
            void Disconnect();
            bool IsConnected { get; }
            MT5AccountInfo GetAccountInfo();
            List<MT5Position> GetOpenPositions();
            List<MT5Position> GetTodayClosedPositions();
        }


        #endregion

    }
}
#endregion