using Sentrix.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;

namespace Sentrix
{
    //public class MT5AccountInfo
    //{
    //    public long Login { get; set; }
    //    public double Balance { get; set; }
    //    public double Equity { get; set; }
    //    public string Currency { get; set; }
    //    public string ServerTime { get; set; }
    //}

    public class MT5Position
    {
        public ulong Ticket { get; set; }
        public string Symbol { get; set; }
        public string Direction { get; set; }   // "Buy" / "Sell"
        public double Lots { get; set; }
        public double EntryPrice { get; set; }
        public double StopLoss { get; set; }
        public double TakeProfit { get; set; }
        public double Profit { get; set; }
        public string OpenTime { get; set; }   // ISO string from EA

        // Convenience property — parsed from OpenTime string
        [JsonIgnore]
        public DateTime OpenTimeUtc
        {
            get
            {
                if (DateTime.TryParse(OpenTime, out var dt))
                    return DateTime.SpecifyKind(dt, DateTimeKind.Utc);
                return DateTime.UtcNow;
            }
        }
    }

    // Internal DTO that mirrors the EA's JSON root
    internal class MT5Payload
    {
        [JsonPropertyName("Login")] public long Login { get; set; }
        [JsonPropertyName("Balance")] public double Balance { get; set; }
        [JsonPropertyName("Equity")] public double Equity { get; set; }
        [JsonPropertyName("Currency")] public string Currency { get; set; }
        [JsonPropertyName("ServerTime")] public string ServerTime { get; set; }
        [JsonPropertyName("Positions")] public List<MT5Position> Positions { get; set; }
    }

    public class MT5Service 
    {
      
        public MT5Service() { }

        private const string PipeName = "SentriXBridge";

        private NamedPipeServerStream _pipe;
        private CancellationTokenSource _cts;
        private Task _readerTask;
        private Task _cmdTask;
        private readonly object _lock = new();

        // Latest snapshot — updated by background reader, read by Sentrix timer
        private MT5Payload _latest;

        // ── Public state ──────────────────────────────────────────────

        public bool IsConnected { get; private set; }

        /// <summary>Raised on the thread-pool whenever a fresh payload arrives.</summary>
        public event Action<MT5AccountInfo, List<MT5Position>> OnDataReceived;
        private const string CmdPipeName = "SentriXBridgeCmd";
        private NamedPipeServerStream _cmdPipe;
        private readonly object _cmdLock = new();
        

        // ── Lifecycle ─────────────────────────────────────────────────

        /// <summary>
        /// Start listening.  Call once from MainWindow.  
        /// The EA connects as soon as MT5 is running.
        /// </summary>
        public void Start()
        {
            _cts = new CancellationTokenSource();
            _cmdTask = Task.Run(() => CommandLoop(_cts.Token));
            _readerTask = Task.Run(() => ReaderLoop(_cts.Token));
        }

        private async Task CommandLoop(CancellationToken token)
        {
            Debug.WriteLine($"MT5Service: CommandLoop started at {DateTime.Now:HH:mm:ss}");
            while (!token.IsCancellationRequested)
            {
                NamedPipeServerStream pipe = null;
                Debug.WriteLine($"MT5Service: cmd pipe state — IsConnected={pipe?.IsConnected}, _cmdPipe null={_cmdPipe == null}");
                try
                {
                    pipe = new NamedPipeServerStream(CmdPipeName, PipeDirection.Out, 1, PipeTransmissionMode.Byte, PipeOptions.Asynchronous | PipeOptions.WriteThrough);
                    Debug.WriteLine($"MT5Service: cmd pipe server created at {DateTime.Now:HH:mm:ss}");
                    Debug.WriteLine("MT5Service: cmd pipe ready, waiting for EA...");

                    await pipe.WaitForConnectionAsync(token);
                    Debug.WriteLine($"MT5Service: cmd pipe EA connected at {DateTime.Now:HH:mm:ss}");

                    lock (_cmdLock)
                    {
                        _cmdPipe = pipe;
                    }
                    Debug.WriteLine("MT5Service: cmd pipe connected.");

                    while (pipe.IsConnected && !token.IsCancellationRequested)
                    {
                        await Task.Delay(200, token).ContinueWith(_ => { });

                        try
                        {
                            pipe.Write(Array.Empty<byte>(),0,0);

                        }
                        catch (IOException)
                        {

                            Debug.WriteLine("MT5Service: cmd pipe broken, reconnecting...");
                            break;
                        }

                    }
                }
                catch (OperationCanceledException) when (token.IsCancellationRequested)
                {
                    break;
                }
                catch (OperationCanceledException) { }
                catch (IOException ex)
                {
                    Debug.WriteLine($"MT5Service: cmd pipe IO error — {ex.Message}");
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MT5Service: cmd pipe error — {ex.Message}");
                }
                finally
                {
                    lock (_cmdLock)
                    {
                        _cmdPipe = null;
                        try
                        {
                            pipe?.Dispose();
                        }
                        catch 
                        {
                        }
                    }
                    Debug.WriteLine("MT5Service: cmd pipe disposed, recreating...");
                }
                if(!token.IsCancellationRequested)
                    await Task.Delay(500, token).ContinueWith(_ => { });

            }
        }

        public void Stop()
        {
            _cts?.Cancel();
            IsConnected = false;
            try { _pipe?.Close(); } catch { }
            try { _cmdPipe?.Dispose(); } catch { }
        }

        public void Dispose() => Stop();

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
                // return a copy so caller can iterate without holding the lock
                return new List<MT5Position>(_latest.Positions);
            }
        }

        // ── Background reader loop ────────────────────────────────────

        private async Task ReaderLoop(CancellationToken ct)
        {
            while (!ct.IsCancellationRequested)
            {
                NamedPipeServerStream pipe = null;
                try
                {
                    // Create a new server-side pipe and wait for the EA to connect
                    pipe = new NamedPipeServerStream(
                        PipeName,
                        PipeDirection.InOut,
                        1,                              // max 1 EA instance
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);
                    _pipe = pipe;   // for finally block dispose
                    Debug.WriteLine("MT5Service: waiting for EA to connect...");
                    await pipe.WaitForConnectionAsync(ct);

                    IsConnected = true;
                    Debug.WriteLine("MT5Service: EA connected.");

                    using var reader = new BinaryReader(pipe, Encoding.UTF8, leaveOpen: true);

                    while (pipe.IsConnected && !ct.IsCancellationRequested)
                    {
                        int length=0;
                        try
                        {

                             length = reader.ReadInt32();
                            if (length <= 0 || length > 1_048_576)   // sanity: max 1 MB
                            {
                                Debug.WriteLine($"MT5Service: bad packet length {length}, resetting.");
                                break;
                            }

                            byte[] buf = reader.ReadBytes(length);
                            if (buf.Length != length) 
                            { 
                                Debug.WriteLine("MT5Service: incomplete packet.");
                                break;
                            };

                            string json = Encoding.UTF8.GetString(buf);
                            ParseAndStore(json);
                        }
                        catch (EndOfStreamException)
                        {

                            Debug.WriteLine("MT5Service: pipe EOF while reading length — EA disconnected.");
                        }
                        // EA sends: [int32 length][utf-8 json string]

                    }
                }
                catch (OperationCanceledException) when (ct.IsCancellationRequested)
                {
                    Debug.WriteLine("MT5Service: shutdown requested, exiting loop.");
                    break;
                }
                catch (OperationCanceledException)
                {
                    Debug.WriteLine("MT5Service: connection dropped, will reconnect...");
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
                    _pipe = null;
                    try { pipe?.Dispose(); } catch { }
                    Debug.WriteLine($"MT5Service: pipe disposed at {DateTime.Now:HH:mm:ss}, recreating in 500ms...");
                }

                if (!ct.IsCancellationRequested)
                {
                    // Wait 2 s before recreating the pipe (MT5 may still be launching)
                    await Task.Delay(500, ct).ContinueWith(_ => { });
                }
            }

            Debug.WriteLine("MT5Service: reader loop exited.");
        }

        // ── Command sender (close positions) ─────────────────────────

        /// <summary>
        /// Sends a JSON command to the EA through the same pipe.
        /// Used by CloseAllPositions to request the EA close a ticket.
        /// </summary>
       
        public void SendCommand(string jsonCommand)
        {
            NamedPipeServerStream cmdPipe;

            // Grab reference outside lock to avoid holding lock during write
            lock (_cmdLock)
            {
                cmdPipe = _cmdPipe;
            }

            if (cmdPipe == null || !cmdPipe.IsConnected)
            {
                Debug.WriteLine($"MT5Service.SendCommand: cmd pipe not ready — " +
                                $"null={cmdPipe == null}, connected={cmdPipe?.IsConnected}");
                return;
            }

            try
            {
                byte[] data = Encoding.UTF8.GetBytes(jsonCommand);
                byte[] lengthBytes = BitConverter.GetBytes(data.Length);
                // using var writer = new BinaryWriter(cmdPipe, Encoding.UTF8, leaveOpen: true);
                //writer.Write(data.Length);
                //writer.Write(data);
                byte[] packet = new byte[4+ data.Length];
                Buffer.BlockCopy(lengthBytes, 0, packet, 0, 4);
                Buffer.BlockCopy(data, 0, packet, 4, data.Length);

                lock(_cmdLock)
                {
                   cmdPipe.Write(packet, 0, packet.Length);
                   cmdPipe.Flush();
                }

                //cmdPipe.Write(lengthBytes, 0, 4);
                //cmdPipe.Write(data, 0, data.Length);
                //cmdPipe.Flush();
                Debug.WriteLine($"MT5Service.SendCommand: sent — {jsonCommand}");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MT5Service.SendCommand error: {ex.Message}");
                lock (_cmdLock) { _cmdPipe = null; }
            }
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

        public void Restart()
        {
            _cts?.Cancel();
            try
            {
                _pipe?.Dispose();
            }
            catch
            {
            }
            try { _cmdPipe?.Dispose(); } catch { }
            _pipe = null;
            _cmdPipe = null;
            IsConnected = false;

            _cts = new CancellationTokenSource();
            _cmdTask = Task.Run(() => CommandLoop(_cts.Token));
            _readerTask = Task.Run(() => ReaderLoop(_cts.Token));
            Debug.WriteLine("MT5Service: Restarted reader loop.");
        }
    }
}
