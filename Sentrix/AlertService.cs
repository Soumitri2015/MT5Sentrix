using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace Sentrix
{
    public class AlertService
    {
        private static CancellationTokenSource _alertCts;
        private static readonly object _alertLock = new();
        public static void ShowBlockSessionAlert(string sessionName)
        {
            string title = "Trading Locked";
            string msg = $"You are blocked for the {sessionName} session.\n\nTrading is locked.";

            CancellationTokenSource localCts;

            lock (_alertLock)
            {
                _alertCts?.Cancel();
                _alertCts?.Dispose();

                _alertCts = new CancellationTokenSource();
                localCts = _alertCts;
            }

            Task.Run(() =>
            {
                try
                {
                    if (localCts.IsCancellationRequested)
                        return;

                    CloseExistingAlert(title);

                    if (localCts.IsCancellationRequested)
                        return;

                    MessageBoxW(
                        IntPtr.Zero,
                        msg,
                        title,
                        0x00000010 | 0x00001000
                    );
                }
                catch
                {
                    // swallow
                }
            }, localCts.Token);
        }

        private static void CloseExistingAlert(string title)
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
            catch
            {
                // ignore safely
            }
        }


        public static void Show(string title, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
                return;

            CancellationTokenSource localCts;

            lock (_alertLock)
            {
                _alertCts?.Cancel();
                _alertCts?.Dispose();

                _alertCts = new CancellationTokenSource();
                localCts = _alertCts;
            }

            Task.Run(() =>
            {
                try
                {
                    if (localCts.IsCancellationRequested)
                        return;

                    CloseExistingAlert(title);

                    if (localCts.IsCancellationRequested)
                        return;

                    MessageBoxW(
                        IntPtr.Zero,
                        message,
                        title ?? "Alert",
                        0x00000010 | 0x00001000
                    );
                }
                catch
                {
                    // swallow safely
                }
            }, localCts.Token);
        }

        public static string NormaliZeSession(string value)
        {
            return value?.Replace(" ", "").Trim().ToLowerInvariant();
        }


        private const int WM_CLOSE = 0x0010;

        private delegate bool EnumWindowsProc(IntPtr hWnd, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool EnumWindows(EnumWindowsProc lpEnumFunc, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder text, int count);

        [DllImport("user32.dll")]
        private static extern bool PostMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int MessageBoxW(IntPtr hWnd, string text, string caption, uint type);
    }
}

