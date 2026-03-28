using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Brush = System.Windows.Media.Brush;
using Brushes = System.Windows.Media.Brushes;
using Color = System.Windows.Media.Color;

namespace Sentrix.UIPages
{
    public partial class OverlayWindow : Window
    {
        private IntPtr targetHandle;
        private DispatcherTimer trackingTimer;

        private bool _blockClicks;
        private bool _lastClickThroughState = true;
        private uint _targetProcessId;
        private bool _isAlertShowing = false;

        // Background Brush: Alpha 50 makes it slightly visible and hit-testable
        private static readonly Brush BlockBrush = new SolidColorBrush(System.Windows.Media.Color.FromArgb(50, 0, 0, 0));

        public bool BlockClicks
        {
            get => _blockClicks;
            set
            {
                _blockClicks = value;
                //Background = value ? InvisibleBlockerBrush : Brushes.Transparent;
                Background = value ? new SolidColorBrush(Color.FromArgb(0, 0, 0, 0)) : System.Windows.Media.Brushes.Transparent;
                // If TRUE (Block): Remove Transparent flag (catch clicks)
                // If FALSE (Allow): Add Transparent flag (click-through)
                SetClickThrough(!value);

                // Update background: When blocking, we need a "hit-testable" background
                //commmented
                if (value) { Topmost = true; FollowTargetZOrder(); }
            }
        }

        public OverlayWindow(IntPtr target)
        {
            InitializeComponent();
            targetHandle = target;

            WindowStyle = WindowStyle.None;
            AllowsTransparency = true;
            ShowInTaskbar = false;
            Topmost = true;
            Background = Brushes.Transparent;

            StartTrackingTarget();
            Loaded += OnOverlayLoaded;
        }

        private void OnOverlayLoaded(object sender, RoutedEventArgs e)
        {
            SetClickThrough(!_blockClicks);
            StartTrackingTarget();
        }

        private void StartTrackingTarget()
        {
            trackingTimer = new DispatcherTimer();
            // Increased interval slightly to 100ms to reduce CPU load/flicker
            trackingTimer.Interval = TimeSpan.FromMilliseconds(50);
            trackingTimer.Tick += (s, e) => UpdateOverlayPosition();
            trackingTimer.Start();
        }

        public void ForceUpdateLayout()
        {
            UpdateOverlayPosition();
        }

        private void UpdateOverlayPosition()
        {
            if (targetHandle == IntPtr.Zero || !IsWindow(targetHandle))
            {
                this.Close();
                return;
            }

            if (GetWindowRect(targetHandle, out RECT rect))
            {
                // 1. FLICKER FIX: Only update Layout if values actually changed
                // This prevents the visual tree from rebuilding constantly
                int newWidth = Math.Max(0, rect.Right - rect.Left);
                int newHeight = Math.Max(0, rect.Bottom - rect.Top);

                if (Math.Abs(this.Left - rect.Left) > 2 ||
                    Math.Abs(this.Top - rect.Top) > 2 ||
                    Math.Abs(this.Width - newWidth) > 2 ||
                    Math.Abs(this.Height - newHeight) > 2)
                {
                    this.Left = rect.Left;
                    this.Top = rect.Top;
                    this.Width = newWidth;
                    this.Height = newHeight;
                }

                // 2. Ensure Z-Order (Keep us above the target)
                // We don't spam HWND_TOPMOST here anymore, we insert ourselves specifically above the target
                IntPtr myHandle = new WindowInteropHelper(this).Handle;

                // If we are blocking clicks, we MUST be physically above the target
                if (_blockClicks)
                {
                    // SWP_NOACTIVATE is critical to prevent stealing focus (flickering)
                    SetWindowPos(myHandle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
                }
            }
        }

        public void SetClickThrough(bool transparent)
        {
            IntPtr hwnd = new WindowInteropHelper(this).Handle;
            if (hwnd == IntPtr.Zero) return;

            int extendedStyle = GetWindowLong(hwnd, GWL_EXSTYLE);

            if (transparent)
            {
                // Add Transparent flag (Clicks pass through to target below)
                SetWindowLong(hwnd, GWL_EXSTYLE, extendedStyle | WS_EX_TRANSPARENT | WS_EX_LAYERED);
            }
            else
            {
                // Remove Transparent flag (Overlay catches the clicks)
                SetWindowLong(hwnd, GWL_EXSTYLE, (extendedStyle | WS_EX_LAYERED) & ~WS_EX_TRANSPARENT);
            }
        }

        // Logic to intercept the click and show the alert
        protected override void OnPreviewMouseDown(MouseButtonEventArgs e)
        {
            if (BlockClicks )
            {
                ShowTempAlert("Max trades for this session reached – trading locked until next session");

                // e.Handled = true tells WPF the event is processed and stops it from propagating downward
                e.Handled = true;
            }
            base.OnPreviewMouseDown(e);
        }

        public async void ShowTempAlert(string message)
        {
            if (_isAlertShowing) return;
            _isAlertShowing = true;

            await Dispatcher.InvokeAsync(() =>
            {
                // Ensure AlertText and AlertBox exist in your XAML
                 if (AlertText != null) AlertText.Text = message;
                 if (AlertBox != null) AlertBox.Visibility = Visibility.Visible;
                Topmost = true;
            });

            await Task.Delay(3000);

            await Dispatcher.InvokeAsync(() =>
            {
                 if (AlertBox != null) AlertBox.Visibility = Visibility.Hidden;
            });

            _isAlertShowing = false;
        }
        //private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);

        public void FollowTargetZOrder()
        {
            IntPtr myHandle = new WindowInteropHelper(this).Handle;
            if (myHandle == IntPtr.Zero) return;

            // By passing HWND_TOPMOST (-1), we force the overlay ABOVE MetaTrader.
            SetWindowPos(myHandle, HWND_TOPMOST, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
        }
        protected override void OnClosed(EventArgs e)
        {
            trackingTimer?.Stop();
            base.OnClosed(e);
        }

        // --- Win32 API Definitions ---
        private const int GWL_EXSTYLE = -20;
        private const int WS_EX_TRANSPARENT = 0x20;
        private const int WS_EX_LAYERED = 0x80000;
        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOACTIVATE = 0x0010;

        [DllImport("user32.dll")]
        static extern int GetWindowLong(IntPtr hWnd, int nIndex);

        [DllImport("user32.dll")]
        static extern int SetWindowLong(IntPtr hWnd, int nIndex, int dwNewLong);

        [DllImport("user32.dll")]
        static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll")]
        static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint processId);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT { public int Left, Top, Right, Bottom; }
        [DllImport("user32.dll")]
        static extern bool IsWindow(IntPtr hWnd);
    }
}