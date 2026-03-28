using Google.Protobuf;
using OpenAPI.Net;
using OpenAPI.Net.Helpers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Reactive.Linq;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace Sentrix
{
    public class CtraderOpenApiService
    {
       


        
        private const string ClientId = "YOUR_CLIENT_ID";
        private const string ClientSecret = "YOUR_CLIENT_SECRET";
        private const string AccessToken = "YOUR_ACCESS_TOKEN";
        private const long AccountId = 123456789;

        public class CTraderAccountInfo
        {
            public long AccountId { get; set; }
            public bool IsLive { get; set; }
            public string AccountName { get; set; }   // session / broker label
        }

        public class CTraderSessionData
        {
            // Identity
            public long AccountId { get; set; }
            public string AccountName { get; set; }
            public bool IsLive { get; set; }
            public string BrokerName { get; set; }

            // Financials  (already divided by moneyDigits)
            public double Balance { get; set; }
            public double Equity { get; set; }
            public double UsedMargin { get; set; }
            public double FreeMargin { get; set; }
            public string Currency { get; set; }

            // Live positions
            public List<CTraderPosition> Positions { get; set; } = new();

            // Pending orders
            public List<CTraderOrder> PendingOrders { get; set; } = new();

            public DateTime LastUpdated { get; set; }
            public long SymbolId { get; set; }
           // public double UsedMargin { get; set; }
            public double Swap { get; set; }
            public double Commission { get; set; }
            public double UnrealizedPnL { get; set; }
        }

        public class CTraderPosition
        {
            public long PositionId { get; set; }
            public string Symbol { get; set; }
            public string Side { get; set; }   // BUY / SELL
            public double Volume { get; set; }   // in lots
            public double EntryPrice { get; set; }
            public double StopLoss { get; set; }
            public double TakeProfit { get; set; }
            public double UnrealizedPnL { get; set; }
            public DateTime OpenTime { get; set; }
            public long SymbolId { get; set; }
            public double UsedMargin { get; set; }
            public double Swap { get; set; }
            public double Commission { get; set; }
            //public double UnrealizedPnL { get; set; }
        }

        public class CTraderOrder
        {
            public long OrderId { get; set; }
            public string Symbol { get; set; }
            public string OrderType { get; set; }
            public string Side { get; set; }
            public double Volume { get; set; }
            public double LimitPrice { get; set; }
            public double StopPrice { get; set; }
            public double StopLoss { get; set; }
            public double TakeProfit { get; set; }
            public DateTime CreateTime { get; set; }
        }


        private readonly string _clientId;
        private readonly string _clientSecret;

        // ── OAuth2 endpoints ─────────────────────────────────────────────────
        private const string AuthBaseUrl = "https://connect.spotware.com/apps/auth";
        private const string TokenUrl = "https://connect.spotware.com/apps/token";

        // ── Localhost redirect (RFC 8252 — production standard for desktop) ──
        //    Register exactly this URI in your cTrader app portal
        private const string RedirectUri = "http://localhost:5000/callback";

        // ── Runtime state ─────────────────────────────────────────────────────
        private OpenClient _client;
        private string _accessToken;
        private string _refreshToken;
        private DateTime _tokenExpiry;
        private long _selectedAccountId;
        private int _moneyDigits = 2;        // default, updated per account
        private readonly SemaphoreSlim _connectLock = new(1, 1);
        private readonly HttpClient _http = new();

        // Symbol cache:  symbolId → symbolName
        private readonly Dictionary<long, string> _symbolMap = new();

        // ── Public events ─────────────────────────────────────────────────────
        public event Action<CTraderSessionData> OnSessionDataReady;
        public event Action<string> OnError;
        public event Action OnDisconnected;
        private readonly Dictionary<long, (ulong bid, ulong ask)> _spotCache = new();
        private readonly HashSet<long> _subscribedSymbols = new();


        public CtraderOpenApiService(string clientId, string clientSecret)
        {
            _clientId = clientId;
            _clientSecret = clientSecret;
        }

        // ═════════════════════════════════════════════════════════════════════
        //  STEP 1 ── OAuth2 Login  (opens browser, listens on localhost:5000)
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Opens the cTrader login page in the default browser and waits for
        /// the OAuth2 callback on localhost:5000.  Returns the access token.
        /// </summary>
        public async Task<string> LoginAsync(CancellationToken ct = default)
        {
            // Build authorisation URL
            var authUrl = $"{AuthBaseUrl}" +
                          $"?client_id={Uri.EscapeDataString(_clientId)}" +
                          $"&redirect_uri={Uri.EscapeDataString(RedirectUri)}" +
                          $"&response_type=code";

            // Open browser
            Process.Start(new ProcessStartInfo(authUrl) { UseShellExecute = true });

            // Listen for redirect
            var code = await WaitForAuthCodeAsync(ct);

            // Exchange code for tokens
            await ExchangeCodeAsync(code);

            return _accessToken;
        }

        private async Task<string> WaitForAuthCodeAsync(CancellationToken ct)
        {
            using var listener = new HttpListener();
            listener.Prefixes.Add("http://localhost:5000/");
            listener.Start();

            // GetContextAsync does not accept CancellationToken directly
            // so we wrap it
            var contextTask = listener.GetContextAsync();
            var cancelTask = Task.Delay(Timeout.Infinite, ct);

            var completed = await Task.WhenAny(contextTask, cancelTask);
            if (completed == cancelTask)
            {
                listener.Stop();
                throw new OperationCanceledException("Login cancelled.");
            }

            var context = await contextTask;
            var code = context.Request.QueryString["code"];
            var error = context.Request.QueryString["error"];

            // Return a friendly page to the browser
            string html = string.IsNullOrEmpty(error)
                ? "<html><body style='font-family:sans-serif;padding:40px'>" +
                  "<h2 style='color:#27ae60'>✅ Sentrix Connected!</h2>" +
                  "<p>You can close this tab and return to Sentrix.</p></body></html>"
                : $"<html><body style='font-family:sans-serif;padding:40px'>" +
                  $"<h2 style='color:#e74c3c'>❌ Login Failed</h2><p>{error}</p></body></html>";

            var buf = Encoding.UTF8.GetBytes(html);
            context.Response.ContentLength64 = buf.Length;
            context.Response.ContentType = "text/html";
            await context.Response.OutputStream.WriteAsync(buf, ct);
            context.Response.OutputStream.Close();
            listener.Stop();

            if (!string.IsNullOrEmpty(error))
                throw new Exception($"cTrader auth error: {error}");

            if (string.IsNullOrEmpty(code))
                throw new Exception("No auth code received from cTrader.");

            return code;
        }

        private async Task ExchangeCodeAsync(string code)
        {
            var body = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string,string>("grant_type",    "authorization_code"),
                new KeyValuePair<string,string>("code",          code),
                new KeyValuePair<string,string>("redirect_uri",  RedirectUri),
                new KeyValuePair<string,string>("client_id",     _clientId),
                new KeyValuePair<string,string>("client_secret", _clientSecret),
            });

            var res = await _http.PostAsync(TokenUrl, body);
            var json = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
                throw new Exception($"Token exchange failed: {json}");

            var obj = JsonSerializer.Deserialize<JsonElement>(json);
            _accessToken = obj.GetProperty("accessToken").GetString();
            _refreshToken = obj.TryGetProperty("refreshToken", out var rt) ? rt.GetString() : null;
            int expiresIn = obj.TryGetProperty("expiresIn", out var ei) ? ei.GetInt32() : 3600;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60); // 60s buffer
        }

        // ═════════════════════════════════════════════════════════════════════
        //  STEP 2 ── Token Refresh
        // ═════════════════════════════════════════════════════════════════════

        public async Task RefreshTokenIfNeededAsync()
        {
            if (DateTime.UtcNow < _tokenExpiry) return;
            if (string.IsNullOrEmpty(_refreshToken))
                throw new Exception("No refresh token available. Re-login required.");

            var body = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string,string>("grant_type",    "refresh_token"),
                new KeyValuePair<string,string>("refresh_token", _refreshToken),
                new KeyValuePair<string,string>("client_id",     _clientId),
                new KeyValuePair<string,string>("client_secret", _clientSecret),
            });

            var res = await _http.PostAsync(TokenUrl, body);
            var json = await res.Content.ReadAsStringAsync();

            if (!res.IsSuccessStatusCode)
                throw new Exception($"Token refresh failed: {json}");

            var obj = JsonSerializer.Deserialize<JsonElement>(json);
            _accessToken = obj.GetProperty("accessToken").GetString();
            _refreshToken = obj.TryGetProperty("refreshToken", out var rt) ? rt.GetString() : _refreshToken;
            int expiresIn = obj.TryGetProperty("expiresIn", out var ei) ? ei.GetInt32() : 3600;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(expiresIn - 60);
        }

        // ═════════════════════════════════════════════════════════════════════
        //  STEP 3 ── TCP Connect + Application Auth
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Connects the TCP socket to cTrader and authenticates the application.
        /// Call this once after login.
        /// </summary>
        public async Task ConnectAsync(bool useLive = false)
        {
            await _connectLock.WaitAsync();
            try
            {
                var host = useLive ? ApiInfo.LiveHost : ApiInfo.DemoHost;

                _client = new OpenClient(host, ApiInfo.Port, TimeSpan.FromSeconds(10));

                // Wire up global disconnect handler
                _client.Subscribe(
                    onNext: _ => { },
                    onError: ex => { OnError?.Invoke(ex.Message); OnDisconnected?.Invoke(); },
                    onCompleted: () => OnDisconnected?.Invoke()
                );

                await _client.Connect();

                // Authenticate the application
                var appAuth = new ProtoOAApplicationAuthReq
                {
                    ClientId = _clientId,
                    ClientSecret = _clientSecret
                };

                var appAuthTcs = new TaskCompletionSource<bool>();
                using var sub = _client
                    .Where(m => m.GetPayloadType() == (int)ProtoOAPayloadType.ProtoOaApplicationAuthRes)
                    .Take(1)
                    .Subscribe(_ => appAuthTcs.TrySetResult(true));

                await _client.SendMessage(appAuth, ProtoOAPayloadType.ProtoOaApplicationAuthReq);
                await appAuthTcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
            }
            finally
            {
                _connectLock.Release();
            }
        }

        // ═════════════════════════════════════════════════════════════════════
        //  STEP 4 ── Get All Accounts for This Token
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Returns all cTrader accounts linked to the logged-in user's token.
        /// </summary>
        public async Task<List<CTraderAccountInfo>> GetAccountsAsync()
        {
            await RefreshTokenIfNeededAsync();

            var req = new ProtoOAGetAccountListByAccessTokenReq
            {
                AccessToken = _accessToken
            };

            var tcs = new TaskCompletionSource<List<CTraderAccountInfo>>(); 
          // Replace this block inside GetAccountsAsync:
            using var sub = _client
                .Where(m => m.GetPayloadType() == (int)ProtoOAPayloadType.ProtoOaGetAccountsByAccessTokenRes)
                .Take(1)
                .Subscribe(msg =>
                {
                    // Fix: Use msg.Data instead of msg.GetPayload()
                    // If OpenAPI.Net IMessage exposes the payload as a property called Data (byte[]), use that.
                    // If not, you need to check your OpenClient/IMessage implementation for the correct property.
                    // Here, we assume msg.Data is the payload.

                    var res = ProtoOAGetAccountListByAccessTokenRes.Parser.ParseFrom(msg.ToByteArray());
                    var accounts = new List<CTraderAccountInfo>();

                    foreach (var a in res.CtidTraderAccount)
                    {
                        accounts.Add(new CTraderAccountInfo
                        {
                            AccountId = (long)a.CtidTraderAccountId,
                            IsLive = a.IsLive,
                            AccountName = a.IsLive ? $"Live #{a.CtidTraderAccountId}"
                                                   : $"Demo #{a.CtidTraderAccountId}"
                        });
                    }
                    tcs.TrySetResult(accounts);
                });

            await _client.SendMessage(req, ProtoOAPayloadType.ProtoOaGetAccountsByAccessTokenReq);
            return await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        // ═════════════════════════════════════════════════════════════════════
        //  STEP 5 ── Authenticate the Selected Account
        // ═════════════════════════════════════════════════════════════════════

        public async Task AuthenticateAccountAsync(long accountId)
        {
            await RefreshTokenIfNeededAsync();

            _selectedAccountId = accountId;

            var req = new ProtoOAAccountAuthReq
            {
                CtidTraderAccountId = accountId,
                AccessToken = _accessToken
            };

            var tcs = new TaskCompletionSource<bool>();
            using var sub = _client
                .Where(m => m.GetPayloadType() == (int)ProtoOAPayloadType.ProtoOaAccountAuthRes)
                .Take(1)
                .Subscribe(_ => tcs.TrySetResult(true));

            await _client.SendMessage(req, ProtoOAPayloadType.ProtoOaAccountAuthReq);
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));

            // Pre-load symbol map for this account
            await LoadSymbolMapAsync();
        }

        // ═════════════════════════════════════════════════════════════════════
        //  STEP 6 ── Load Symbol Map  (symbolId → name)
        // ═════════════════════════════════════════════════════════════════════

        private async Task LoadSymbolMapAsync()
        {
            var req = new ProtoOASymbolsListReq
            {
                CtidTraderAccountId = _selectedAccountId
            };

            var tcs = new TaskCompletionSource<bool>();
            using var sub = _client
                .Where(m => m.GetPayloadType() == (int)ProtoOAPayloadType.ProtoOaSymbolsListRes)
                .Take(1)
                .Subscribe(msg =>
                {
                    var res = ProtoOASymbolsListRes.Parser.ParseFrom(msg.ToByteString());
                    _symbolMap.Clear();
                    foreach (var s in res.Symbol)
                        _symbolMap[s.SymbolId] = s.SymbolName;
                    tcs.TrySetResult(true);
                });

            await _client.SendMessage(req, ProtoOAPayloadType.ProtoOaSymbolsListReq);
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        private void WireSpotEventListener()
        {
            _client
                .Where(m => m.GetPayloadType() == (int)ProtoOAPayloadType.ProtoOaSpotEvent)
                .Subscribe(msg =>
                {
                    var ev = ProtoOASpotEvent.Parser.ParseFrom(msg.ToByteString());
                    if (ev.CtidTraderAccountId != _selectedAccountId) return;

                    _spotCache.TryGetValue(ev.SymbolId, out var prev);
                    _spotCache[ev.SymbolId] = (
                        ev.HasBid ? ev.Bid : prev.bid,
                        ev.HasAsk ? ev.Ask : prev.ask
                    );
                });
        }

        private async Task SubscribeSpotsAsync(IEnumerable<long> symbolIds)
        {
            var toSub = symbolIds.Distinct()
                                 .Where(id => !_subscribedSymbols.Contains(id))
                                 .ToList();
            if (toSub.Count == 0) return;

            var req = new ProtoOASubscribeSpotsReq { CtidTraderAccountId = _selectedAccountId };
            req.SymbolId.AddRange(toSub);

            await _client.SendMessage(req, ProtoOAPayloadType.ProtoOaSubscribeSpotsReq);

            foreach (var id in toSub)
                _subscribedSymbols.Add(id);
        }

        // ═════════════════════════════════════════════════════════════════════
        //  STEP 7 ── Fetch Session Data  (balance, equity, positions, orders)
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Fetches the complete session snapshot and fires OnSessionDataReady.
        /// Call this on a timer (e.g. every 5–30 seconds) for your guardian logic.
        /// </summary>
        public async Task<CTraderSessionData> FetchSessionDataAsync()
        {
            await RefreshTokenIfNeededAsync();

            var session = new CTraderSessionData
            {
                AccountId = _selectedAccountId,
                LastUpdated = DateTime.UtcNow
            };

            await FetchTraderInfoAsync(session);
            await FetchPositionsAndOrdersAsync(session);
            //await FetchUnrealizedPnLAsync(session);
            BuildEquityAndMargin(session);

            OnSessionDataReady?.Invoke(session);
            return session;
        }

        // ── Trader info: balance, equity, margin, account name ───────────────
        //private async Task FetchTraderInfoAsync(CTraderSessionData session)
        //{
        //    var req = new ProtoOATraderReq
        //    {
        //        CtidTraderAccountId = _selectedAccountId
        //    };

        //    var tcs = new TaskCompletionSource<bool>();
        //    using var sub = _client
        //        .Where(m => m.GetPayloadType() == (int)ProtoOAPayloadType.ProtoOaTraderRes)
        //        .Take(1)
        //        .Subscribe(msg =>
        //        {
        //            var res = ProtoOATraderRes.Parser.ParseFrom(msg.ToByteString());
        //            var trader = res.Trader;

        //            // Store moneyDigits for future use
        //            _moneyDigits = (int)(trader.MoneyDigits > 0 ? trader.MoneyDigits : 2);
        //            double divisor = Math.Pow(10, _moneyDigits);

        //            session.AccountName = trader.BrokerName ?? $"Account #{_selectedAccountId}";
        //            session.BrokerName = trader.BrokerName ?? string.Empty;
        //            session.IsLive = trader.AccountType == ProtoOAAccountType.Live;
        //            session.Currency = trader.DepositAssetId.ToString(); // use asset name if you map it
        //            session.Balance = trader.Balance / divisor;
        //            session.Equity = trader.Balance / divisor;    // will be overwritten by PnL calc
        //            //session.UsedMargin = trader. / divisor;
        //            //session.FreeMargin = trader.FreeMargin / divisor;

        //            tcs.TrySetResult(true);
        //        });

        //    await _client.SendMessage(req, ProtoOAPayloadType.ProtoOaTraderReq);
        //    await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        //}

        private async Task FetchTraderInfoAsync(CTraderSessionData session)
        {
            var tcs = new TaskCompletionSource<bool>();
            using var sub = _client
                .Where(m => m.GetPayloadType() == (int)ProtoOAPayloadType.ProtoOaTraderRes)
                .Take(1)
                .Subscribe(msg =>
                {
                    var res = ProtoOATraderRes.Parser.ParseFrom(msg.ToByteString());
                    var trader = res.Trader;

                    // moneyDigits controls how to interpret monetary int64 values
                    _moneyDigits = trader.HasMoneyDigits ? (int)trader.MoneyDigits : 2;
                    double div = Math.Pow(10, _moneyDigits);

                    session.BrokerName = trader.BrokerName ?? string.Empty;
                    session.AccountName = !string.IsNullOrEmpty(trader.BrokerName)
                        ? trader.BrokerName
                        : $"Account #{_selectedAccountId}";

                    // DepositAssetId identifies the account currency asset.
                    // You can resolve the name by calling ProtoOAAssetListReq.
                    session.Currency = trader.DepositAssetId.ToString();
                    session.Balance = trader.Balance / div;

                    tcs.TrySetResult(true);
                });

            await _client.SendMessage(
                new ProtoOATraderReq { CtidTraderAccountId = _selectedAccountId },
                ProtoOAPayloadType.ProtoOaTraderReq);

            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        // ── Open positions + pending orders ───────────────────────────────────
        private async Task FetchPositionsAndOrdersAsync(CTraderSessionData session)
        {
            var req = new ProtoOAReconcileReq
            {
                CtidTraderAccountId = _selectedAccountId,
                
            };

            var tcs = new TaskCompletionSource<bool>();
            using var sub = _client
                .Where(m => m.GetPayloadType() == (int)ProtoOAPayloadType.ProtoOaReconcileRes)
                .Take(1)
                .Subscribe(msg =>
                {
                    var res = ProtoOAReconcileRes.Parser.ParseFrom(msg.ToByteString());
                    double div = Math.Pow(10, _moneyDigits);

                    // Positions
                    session.Positions.Clear();
                    foreach (var p in res.Position)
                    {
                        string symbolName = _symbolMap.TryGetValue(p.TradeData.SymbolId, out var sn)
                                            ? sn : p.TradeData.SymbolId.ToString();

                        session.Positions.Add(new CTraderPosition
                        {
                            PositionId = p.PositionId,
                            Symbol = symbolName,
                            Side = p.TradeData.TradeSide.ToString(),
                            Volume = p.TradeData.Volume / 100.0,   // convert to lots
                            EntryPrice = p.Price,
                            StopLoss = p.StopLoss,
                            TakeProfit = p.TakeProfit,
                            OpenTime = DateTimeOffset
                                            .FromUnixTimeMilliseconds(p.TradeData.OpenTimestamp)
                                            .UtcDateTime
                        });
                    }

                    // Pending orders
                    session.PendingOrders.Clear();
                    foreach (var o in res.Order)
                    {
                        string symbolName = _symbolMap.TryGetValue(o.TradeData.SymbolId, out var sn)
                                            ? sn : o.TradeData.SymbolId.ToString();

                        session.PendingOrders.Add(new CTraderOrder
                        {
                            OrderId = o.OrderId,
                            Symbol = symbolName,
                            OrderType = o.OrderType.ToString(),
                            Side = o.TradeData.TradeSide.ToString(),
                            Volume = o.TradeData.Volume / 100.0,
                            LimitPrice = o.LimitPrice,
                            StopPrice = o.StopPrice,
                            StopLoss = o.StopLoss,
                            TakeProfit = o.TakeProfit,
                            CreateTime = DateTimeOffset
                                           .FromUnixTimeMilliseconds(o.UtcLastUpdateTimestamp)
                                           .UtcDateTime
                        });
                    }

                    tcs.TrySetResult(true);
                });

            await _client.SendMessage(req, ProtoOAPayloadType.ProtoOaReconcileReq);
            await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        }

        private void BuildEquityAndMargin(CTraderSessionData session)
        {
            double totalPnL = 0;
            double totalUsedMargin = 0;

            foreach (var pos in session.Positions)
            {
                totalUsedMargin += pos.UsedMargin;

                if (_spotCache.TryGetValue(pos.SymbolId, out var spot))
                {
                    const double priceDiv = 100_000.0;
                    double bid = spot.bid / priceDiv;
                    double ask = spot.ask / priceDiv;

                    // Volume: cTrader stores in 0.01 units (cents of a lot)
                    // 1 standard lot = 100,000 units
                    // pos.Volume is already in lots after / 100.0 above
                    double volumeInUnits = pos.Volume * 100_000.0;

                    double rawPnL = pos.Side == "BUY"
                        ? (bid - pos.EntryPrice) * volumeInUnits
                        : (pos.EntryPrice - ask) * volumeInUnits;

                    pos.UnrealizedPnL = rawPnL + pos.Swap + pos.Commission;
                    totalPnL += pos.UnrealizedPnL;
                }
                else
                {
                    // No spot price yet; include swap/commission only
                    pos.UnrealizedPnL = pos.Swap + pos.Commission;
                    totalPnL += pos.UnrealizedPnL;
                }
            }

            session.UsedMargin = totalUsedMargin;
            session.Equity = session.Balance + totalPnL;
            session.FreeMargin = session.Equity - session.UsedMargin;
        }


        // ── Unrealized PnL → used to calculate real Equity ───────────────────
        //private async Task FetchUnrealizedPnLAsync(CTraderSessionData session)
        //{
        //    if (session.Positions.Count == 0) return; 

        //    var req = new 
        //    {
        //        CtidTraderAccountId = _selectedAccountId
        //    };

        //    var tcs = new TaskCompletionSource<bool>();
        //    using var sub = _client
        //        .Where(m => m.ToByteString() == (int)ProtoOAPayloadType.ProtoOaGetPositionUnrealizedPnLRes)
        //        .Take(1)
        //        .Subscribe(msg =>
        //        {
        //            var res = ProtoOAGetPositionUnrealizedPnLRes.Parser.ParseFrom(msg.Payload);
        //            double div = Math.Pow(10, (int)res.MoneyDigits);

        //            double totalGross = 0;
        //            foreach (var pnl in res.PositionUnrealizedPnL)
        //            {
        //                totalGross += pnl.GrossUnrealizedPnL / div;

        //                // Update the matching position object
        //                var pos = session.Positions.Find(p => p.PositionId == pnl.PositionId);
        //                if (pos != null)
        //                    pos.UnrealizedPnL = pnl.GrossUnrealizedPnL / div;
        //            }

        //            // Equity = Balance + total unrealized PnL
        //            session.Equity = session.Balance + totalGross;
        //            tcs.TrySetResult(true);
        //        });

        //    await _client.SendMessage(req, ProtoOAPayloadType.ProtoOaGetPositionUnrealizedPnLReq);
        //    await tcs.Task.WaitAsync(TimeSpan.FromSeconds(10));
        //}

        // ═════════════════════════════════════════════════════════════════════
        //  FULL CONNECT HELPER
        //  One-liner to go from zero → ready to fetch data
        // ═════════════════════════════════════════════════════════════════════

        /// <summary>
        /// Full flow:
        ///   Login (browser) → TCP Connect → App Auth → Account List → Account Auth
        ///
        /// If the user has more than one account, <paramref name="accountPicker"/>
        /// is called so your UI can let them choose.  If null, the first account
        /// is used automatically.
        /// </summary>
        public async Task<CTraderSessionData> FullConnectAsync(
            bool useLive = false,
            Func<List<CTraderAccountInfo>, Task<long>> accountPicker = null,
            CancellationToken ct = default)
        {
            // 1. OAuth2 login
            await LoginAsync(ct);

            // 2. TCP + App auth
            await ConnectAsync(useLive);

            // 3. Get all accounts
            var accounts = await GetAccountsAsync();
            if (accounts.Count == 0)
                throw new Exception("No cTrader accounts found for this user.");

            // 4. Pick account
            long accountId;
            if (accounts.Count == 1 || accountPicker == null)
                accountId = accounts[0].AccountId;
            else
                accountId = await accountPicker(accounts);

            // 5. Auth account
            await AuthenticateAccountAsync(accountId);

            // 6. Fetch initial data
            return await FetchSessionDataAsync();
        }

        // ═════════════════════════════════════════════════════════════════════
        //  Disconnect & Dispose
        // ═════════════════════════════════════════════════════════════════════

        public async Task DisconnectAsync()
        {
            if (_client == null) return;
            try
            {
                var req = new ProtoOAAccountLogoutReq
                {
                    CtidTraderAccountId = _selectedAccountId
                };
                await _client.SendMessage(req, ProtoOAPayloadType.ProtoOaAccountLogoutReq);
                await Task.Delay(500); // let the message send
            }
            catch { /* ignore on disconnect */ }
            finally
            {
                _client?.Dispose();
                _client = null;
            }
        }

        public void Dispose()
        {
            _client?.Dispose();
            _http?.Dispose();
            _connectLock?.Dispose();
        }


        //public void Connect()
        //{
        //    _client = new OpenClient(
        //        ApiInfo.DemoHost,
        //        ApiInfo.Port,
        //        TimeSpan.FromSeconds(5)
        //        );

        //    _client.Connect();

        //    var appAuthReq = new ProtoOAApplicationAuthReq
        //    {
        //        ClientId = ClientId,
        //        ClientSecret = ClientSecret,
        //    };

        //    _client.SendMessage(appAuthReq, ProtoOAPayloadType.ProtoOaApplicationAuthReq);

        //    var accountAuthReq = new ProtoOAAccountAuthReq
        //    {
        //        CtidTraderAccountId = AccountId,
        //        AccessToken = AccessToken,
        //    }
        //}

    }
}
