using Microsoft.Win32;
using Sentrix.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;

namespace Sentrix
{
    public class SentrixInstallerService
    {
        // ── Entry point — call this from your installer / first-run setup ──
        public InstallResult InstallMT5Integration()
        {
            string mt5DataPath = FindMT5DataPath();

            if (mt5DataPath == null)
            {
                return new InstallResult
                {
                    Success = false,
                    Message = "MetaTrader 5 installation not found.\n" +
                              "Please install MT5 first, then run Sentrix again."
                };
            }

            var eaResult = CopyExpertAdvisor(mt5DataPath);
            if (!eaResult.Success)
                return eaResult;

            // Write the auto-load config so MT5 attaches the EA on startup
            WriteAutoLoadConfig(mt5DataPath);

            return new InstallResult
            {
                Success = true,
                Message = $"MT5 integration installed.\n" +
                          $"EA copied to: {mt5DataPath}\\MQL5\\Experts\\\n\n" +
                          $"Next time MT5 opens, click 'Allow AutoTrading' when prompted."
            };
        }

        // ── Find MT5's MQL5 data folder ───────────────────────────────────
        //
        //  MT5 stores its working data (MQL5 folder, profiles, config) in
        //  AppData\Roaming\MetaQuotes\Terminal\<HASH>\
        //  NOT in Program Files — that's read-only on most machines.
        //
        public string FindMT5DataPathPublic() => FindMT5DataPath();

        private string FindMT5DataPath()
        {
            // 1. Check AppData\Roaming — this is where MQL5\Experts lives
            string roaming = Environment.GetFolderPath(
                Environment.SpecialFolder.ApplicationData);
            string terminalRoot = Path.Combine(roaming, "MetaQuotes", "Terminal");

            if (Directory.Exists(terminalRoot))
            {
                foreach (var dir in Directory.GetDirectories(terminalRoot))
                {
                    string expertsPath = Path.Combine(dir, "MQL5", "Experts");
                    if (Directory.Exists(expertsPath))
                    {
                        Debug.WriteLine($"MT5 data path found: {dir}");
                        return dir;
                    }
                }
            }

            // 2. Fallback — check registry for install path
            string[] keys = {
                @"SOFTWARE\MetaQuotes\MetaTrader 5",
                @"SOFTWARE\WOW6432Node\MetaQuotes\MetaTrader 5"
            };

            foreach (var keyPath in keys)
            {
                using var key = Registry.LocalMachine.OpenSubKey(keyPath);
                var path = key?.GetValue("Path")?.ToString();
                if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
                    return path;
            }

            // 3. Last resort — common install locations
            string[] defaults = {
                Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFiles), "MetaTrader 5"),
                Path.Combine(Environment.GetFolderPath(
                    Environment.SpecialFolder.ProgramFilesX86), "MetaTrader 5"),
            };

            foreach (var d in defaults)
                if (Directory.Exists(Path.Combine(d, "MQL5", "Experts")))
                    return d;

            return null;
        }

        // ── Copy SentriXBridge.mq5 to MT5's Experts folder ───────────────
        private InstallResult CopyExpertAdvisor(string mt5DataPath)
        {
            try
            {
                string expertsFolder = Path.Combine(mt5DataPath, "MQL5", "Experts");
                Directory.CreateDirectory(expertsFolder);

                string destPath = Path.Combine(expertsFolder, "SentriXBridge.mq5");

                // Extract the .mq5 file that is embedded as a resource in Sentrix.exe
                // In your .csproj add:
                //   <EmbeddedResource Include="Resources\SentriXBridge.mq5" />
                ExtractEmbeddedResource("Sentrix.Resources.SentriXBridge.mq5", destPath);

                Debug.WriteLine($"EA copied to: {destPath}");
                return new InstallResult { Success = true };
            }
            catch (UnauthorizedAccessException)
            {
                return new InstallResult
                {
                    Success = false,
                    Message = "Permission denied copying EA file.\n" +
                              "Please run Sentrix as Administrator once to complete setup."
                };
            }
            catch (Exception ex)
            {
                return new InstallResult
                {
                    Success = false,
                    Message = $"Failed to copy EA: {ex.Message}"
                };
            }
        }

        // ── Embed SentriXBridge.mq5 inside Sentrix.exe ───────────────────
        private void ExtractEmbeddedResource(string resourceName, string destPath)
        {
            var assembly = Assembly.GetExecutingAssembly();
            using var stream = assembly.GetManifestResourceStream(resourceName);

            if (stream == null)
                throw new FileNotFoundException(
                    $"Embedded resource '{resourceName}' not found. " +
                    $"Make sure SentriXBridge.mq5 is set to EmbeddedResource in .csproj.");

            using var file = new FileStream(destPath, FileMode.Create, FileAccess.Write);
            stream.CopyTo(file);
        }

        // ── Write MT5 auto-load config ────────────────────────────────────
        //
        //  MT5 reads a "startup" profile on launch. We write an experts.ini
        //  that tells MT5 to auto-attach SentriXBridge to the default chart.
        //
        private void WriteAutoLoadConfig(string mt5DataPath)
        {
            try
            {
                // MT5 config folder
                string configFolder = Path.Combine(mt5DataPath, "config");
                Directory.CreateDirectory(configFolder);

                // experts.ini controls EA auto-load behaviour
                string iniPath = Path.Combine(configFolder, "experts.ini");

                // Read existing if present so we don't overwrite other settings
                string existing = File.Exists(iniPath)
                    ? File.ReadAllText(iniPath)
                    : "";

                // Only write if our entry isn't already there
                if (!existing.Contains("SentriXBridge"))
                {
                    File.AppendAllText(iniPath,
                        "\r\n[Experts]\r\n" +
                        "AllowLiveTrading=1\r\n" +
                        "AllowDllImport=0\r\n" +
                        "Enabled=1\r\n" +
                        "Account=0\r\n");
                }

                // Also write a chart profile that auto-loads the EA
                WriteChartProfile(mt5DataPath);
            }
            catch (Exception ex)
            {
                // Non-fatal — EA can still be attached manually
                Debug.WriteLine($"WriteAutoLoadConfig warning: {ex.Message}");
            }
        }

        // ── Write a default chart profile with EA pre-attached ───────────
        private void WriteChartProfile(string mt5DataPath)
        {
            try
            {
                string profilesFolder = Path.Combine(
                    mt5DataPath, "profiles", "default");
                Directory.CreateDirectory(profilesFolder);

                // chart01.chr is the first chart MT5 opens by default
                string chartFile = Path.Combine(profilesFolder, "chart01.chr");

                // Only create if it doesn't exist — don't overwrite trader's setup
                if (File.Exists(chartFile)) return;

                File.WriteAllText(chartFile,
                    "<chart>\r\n" +
                    "symbol=EURUSD\r\n" +
                    "period=1\r\n" +        // M1
                    "<expert>\r\n" +
                    "name=SentriXBridge\r\n" +
                    "window=0\r\n" +
                    "login=0\r\n" +
                    "symbol=EURUSD\r\n" +
                    "</expert>\r\n" +
                    "</chart>\r\n");

                Debug.WriteLine("Chart profile written with SentriXBridge pre-attached.");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WriteChartProfile warning: {ex.Message}");
            }
        }
    }
}
