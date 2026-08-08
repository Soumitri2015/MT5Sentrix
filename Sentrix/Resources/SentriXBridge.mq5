//+------------------------------------------------------------------+
//|  SentriXBridge.mq5                                               |
//|  Sentrix Guardian — MT5 Data Bridge                              |
//+------------------------------------------------------------------+
#property copyright "Sentrix Guardian"
#property version   "1.02"
#property strict

#define PIPE_NAME      "\\\\.\\pipe\\SentrixDataPipe"
#define PIPE_CMD_NAME  "\\\\.\\pipe\\SentrixCmdPipe"
#define INTERVAL_MS    1000
#define SERVICE_CHART_NAME "SENTRIX_BRIDGE_SERVICE"
#define INTERVAL_MS 250
#import "user32.dll"
   long GetDesktopWindow();
   long GetWindow(long hWnd, uint uCmd);
   int  GetWindowTextW(long hWnd, ushort &lpString[], int nMaxCount);
   long SendMessageW(long hWnd, uint Msg, long wParam, long lParam);
#import

#define GW_CHILD 5
#define GW_HWNDNEXT 2
#define WM_CLOSE 0x0010

int g_pipe    = INVALID_HANDLE;   // data pipe  — EA writes, C# reads
int g_cmdPipe = INVALID_HANDLE;   // command pipe — C# writes, EA reads

bool g_SessionActive = false;
int g_MaxTradesDaily = 3;
int g_CurrentDailyTrades = 0;
double g_MaxLossPercent = 2.0;
bool g_Manage1R = true;
ulong g_managedTickets[];
int g_CurrentSessionTrades = 0;
string g_ActiveSessionName = "None";
int g_MaxTradeSession =0;
int g_UtcOffsetMinutes = -300;
int g_LocalHour   = 0;
int g_LocalMinute = 0;
bool g_OffsetCalibrated = false;

int g_TradesLondon = 0;
int g_TradesNewYork = 0;
bool g_UIRestricted = false;


string g_eventQueue[];

string CONFIG_FILE = "Sentrix_LastConfig.txt";
string STATE_FILE = "Sentrix_TradeState.txt";


string g_AllowedSessions = "";
string OFFLINE_QUEUE_FILE = "Sentrix_OfflineQueue.txt";


bool IServiceChart()
{
   string comment = ChartGetString(0,CHART_COMMENT);
   return comment == SERVICE_CHART_NAME;
  
}

//+------------------------------------------------------------------+
int OnInit()
{
  // if(!IServiceChart()){
    //  Print("SentriXBridgeCore: Not service chart → shutting down.");
     //    return(INIT_FAILED);
   //}
   ChartSetString(0, CHART_COMMENT, "SENTRIX_BRIDGE_SERVICE");
   ChartSetInteger(0, CHART_SHOW, true);
   Print("SentriXBridge: starting...");
   EventSetMillisecondTimer(INTERVAL_MS);
   
   LoadConfigOffline();
   LoadStateOffline();
   
   RecoverManagedTickets();
   
   if(g_MaxTradeSession >0){
      printf(g_MaxTradeSession);
      }
      
   
   datetime from, to;
MqlDateTime t;
TimeToStruct(TimeCurrent(), t);
ENUM_DAY_OF_WEEK day = (ENUM_DAY_OF_WEEK)t.day_of_week;

for(int i = 0; i < 10; i++)
{
   if(SymbolInfoSessionTrade(_Symbol, day, i, from, to))
      Print("Broker session index=", i, " from=", TimeToString(from), " to=", TimeToString(to));
}
   
   return INIT_SUCCEEDED;
}




//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   EventKillTimer();
   if(g_pipe    != INVALID_HANDLE) { FileClose(g_pipe);    g_pipe    = INVALID_HANDLE; }
   if(g_cmdPipe != INVALID_HANDLE) { FileClose(g_cmdPipe); g_cmdPipe = INVALID_HANDLE; }
   ChartSetString(0, CHART_COMMENT, "");
   Print("SentriXBridge: stopped.");
}


void PushDataToCSharp()
{
   if(MQLInfoInteger(MQL_TESTER)) return;
   if(g_pipe == INVALID_HANDLE) return;

   string json = BuildJSON();
   int len = StringLen(json); 
   
   ResetLastError();

   if(FileWriteInteger(g_pipe, len, INT_VALUE) == 0 ||
      FileWriteString(g_pipe, json, len)      == 0 || GetLastError() != 0)
   {
      Print("SentriXBridge: data pipe write failed, resetting.");
      FileClose(g_pipe);
      g_pipe = INVALID_HANDLE;
      
      if(g_cmdPipe != INVALID_HANDLE) 
      {
         FileClose(g_cmdPipe);
         g_cmdPipe = INVALID_HANDLE;
      }
   }
   
   FileFlush(g_pipe);
}

//+------------------------------------------------------------------+
void OnTimer()
{

   CheckDailyReset();
   UpdateUIProtection();
   KillNewOrderWindow();
   //RunOfflineSessionWatchdog();
   // ── 1. Connect data pipe (EA → C#) ───────────────────────────
   // 1. Connect data pipe (EA → C#)
   if(g_pipe == INVALID_HANDLE)
   {
      g_pipe = FileOpen(PIPE_NAME, FILE_READ | FILE_WRITE | FILE_BIN | FILE_ANSI|FILE_SHARE_READ|FILE_SHARE_WRITE| 0, CP_UTF8);
      if(g_pipe == INVALID_HANDLE) return;
   }

   // 2. Connect command pipe (C# → EA)
   if(g_cmdPipe == INVALID_HANDLE)
   {
      g_cmdPipe = FileOpen(PIPE_CMD_NAME, FILE_READ | FILE_WRITE | FILE_BIN | FILE_ANSI | FILE_SHARE_READ | FILE_SHARE_WRITE, 0, CP_UTF8);
      
      if(g_pipe != INVALID_HANDLE){
      
         SyncOfflineEvents();
      }
      else return;
   }

   // 3. Read incoming commands
   if(g_cmdPipe != INVALID_HANDLE) ReadIncomingCommand();

   // 4. Send the 1-second heartbeat data
   PushDataToCSharp();
}

void OnTick() 
{
            bool timeWindowAllowed = IsCurrentTimeAllowedWindow();
         string activeSession   = GetActiveSession();
         g_SessionActive        = g_SessionActive && timeWindowAllowed;
         //Print("g_SessionActive is ------"+g_SessionActive);

         int activeSessionTradeCount = 0;
         if(activeSession == "London")        activeSessionTradeCount = g_TradesLondon;
         else if(activeSession == "NewYork")  activeSessionTradeCount = g_TradesNewYork;

         bool violateSession       = !g_SessionActive;
         bool violateDailyTrades   = (g_MaxTradesDaily  > 0 && g_CurrentDailyTrades    >= g_MaxTradesDaily);
         bool violateSessionTrades = (g_MaxTradeSession > 0 && activeSessionTradeCount >= g_MaxTradeSession);
         if(violateSession || violateDailyTrades || violateSessionTrades)
         {
               return;
         }
   
   if(g_SessionActive && g_MaxLossPercent >0){
      double balance = AccountInfoDouble(ACCOUNT_BALANCE);
      double equity = AccountInfoDouble(ACCOUNT_EQUITY);
      
      if(balance >0){
         double lossPercent = ((balance - equity)/ balance)*100.0;
         if(lossPercent >= g_MaxLossPercent)
         {
            LogEvent("🚨 SENTRIX FATAL: Daily Max Loss (" + DoubleToString(g_MaxLossPercent, 2) + "%) reached! Liquidating...");
            
            CloseAllPositions();     // You will need to write this helper function
            g_SessionActive = false; // Lock out further trades immediately
         }
         
      }
      
   }
   
   if(g_Manage1R)
   {
      ManagePositions1R(); // You will need to write this helper function
   }
   
   ENUM_TIMEFRAMES tfs[] = {PERIOD_M1,PERIOD_M5,PERIOD_M15, PERIOD_M30,PERIOD_H1,PERIOD_H4};
   for(int i=0;i<ArraySize(tfs);i++)
     {
         if(IsNewBar(tfs[i]))
         {
            int signal = CheckCandlePattern(_Symbol, tfs[i]);
   
            if(signal == 1)
            {
               //Print("📈 Phase 2: Bullish Pattern Detected on ", EnumToString(tfs[i]));
               ExecuteAutomatedTrade(_Symbol, 1, tfs[i]); 
            }
            else if(signal == -1)
            {
               //Print("📉 Phase 2: Bearish Pattern Detected on ", EnumToString(tfs[i]));
               ExecuteAutomatedTrade(_Symbol, -1, tfs[i]);
            }
         }
     }
   
   /*if(IsNewBar(PERIOD_CURRENT))
   {
      int signal = CheckCandlePattern(_Symbol,PERIOD_CURRENT);

      if(signal == 1)
      {
         Print("📈 Phase 2: Bullish Pattern Detected! (Up-Down-Up)");
         // TODO: Add H4/Daily Trend Filter check here
         ExecuteAutomatedTrade(_Symbol,1);
      }
      else if(signal == -1)
      {
         Print("📉 Phase 2: Bearish Pattern Detected! (Down-Up-Down)");
         ExecuteAutomatedTrade(_Symbol,-1);
         // TODO: Add H4/Daily Trend Filter check here
      }
   }*/
}

//+------------------------------------------------------------------+
void ReadIncomingCommand()
{
   if(g_cmdPipe == INVALID_HANDLE) return;
   
   //if(FileIsEnding(g_cmdPipe))
   //{
     // Print("SentriXBridge: Connection dropped by C#. Resetting handles...");
      //FileClose(g_cmdPipe);
      //g_cmdPipe = INVALID_HANDLE;
      
      //if(g_pipe != INVALID_HANDLE){
        // FileClose(g_pipe);
       //  g_pipe = INVALID_HANDLE;
      //}
      //return;
  // }

   uint available = (uint)FileSize(g_cmdPipe);
   if(available < 4) return;

   ResetLastError();
   int len = FileReadInteger(g_cmdPipe, INT_VALUE);
   
   // CRITICAL FIX: Do NOT close the pipe here! 
   // If FileSize lied to us and the pipe is empty, len will be 0. 
   // Just return and try again next tick so we don't drop the connection.
   if(len <= 0 || len > 1024)
   {
      return;
   }

   string cmd = FileReadString(g_cmdPipe, len);
   if(StringLen(cmd) == 0) return;

   //Print("SentriXBridge: received command — ", cmd);
   
   
   if(StringFind(cmd, "\"CMD\":\"UPDATE_CONFIG\"")>=0){
   
      //g_SessionActive = (StringFind(cmd,"\"SessionActive\":true") >=0);
      g_Manage1R = (StringFind(cmd,"\"Manage1R\":true") >= 0);
      
      string incommingSession = ExtractString(cmd,"\"ActiveSessionName\":");
      if(incommingSession != g_ActiveSessionName && incommingSession != "None" && g_ActiveSessionName != "None")
      {
         Print("🔄 Session shifted from ", g_ActiveSessionName, " to ", incommingSession, ". Resetting session trade count.");
         g_CurrentSessionTrades = 0;
         SaveStateOffline(); // Save the reset
      }
      //g_ActiveSessionName = incommingSession;
      g_MaxTradeSession =(int)ExtractNumber(cmd,"\"MaxTradesPerSession\":");
      
      g_MaxTradesDaily     = (int)ExtractNumber(cmd, "\"MaxTradesDaily\":");
      
      int SentrixDailyTrades = (int)ExtractNumber(cmd,"\"CurrentDailyTrades\":");
      int csharpSessionTrades = (int)ExtractNumber(cmd, "\"CurrentSessionTrades\":");
      
      g_CurrentDailyTrades = (int)ExtractNumber(cmd, "\"CurrentDailyTrades\":");
      g_MaxLossPercent     = ExtractNumber(cmd, "\"MaxLossPercent\":");
      g_AllowedSessions = ExtractString(cmd,"\"AllowedSession\":");
      g_UtcOffsetMinutes = (int)(ExtractNumber(cmd,"\"UTCTimeOffsetMinutes\":") * 60);
      int localHour   = (int)ExtractNumber(cmd, "\"LocalTimeHour\":");
      int localMinute = (int)ExtractNumber(cmd, "\"LocalTimeMinute\":");
      
      //Print("️ Sentrix Rules Updated | Active: ", g_SessionActive, " | Trades: ", g_CurrentDailyTrades, "/", g_MaxTradesDaily);
      
      SaveConfigOffline();
      SaveStateOffline();
      
      return;
   }

   // Parse the command
   if(StringFind(cmd, "\"CMD\":\"CLOSE\"") >= 0)
   {
      int ticketPos = StringFind(cmd, "\"Ticket\":");
      if(ticketPos >= 0)
      {
         string ticketStr = StringSubstr(cmd, ticketPos + 9);
         ulong ticket = (ulong)StringToInteger(ticketStr);
         if(ticket > 0)
            ClosePositionByTicket(ticket);
      }
   }
}

double ExtractNumber(string json, string key){
   int startPos = StringFind(json, key);
   
   if(startPos <0) return 0.0;
   
   startPos += StringLen(key);
   
   int endPos = StringFind(json ,",", startPos);
   if(endPos <0) endPos = StringFind(json,"}",startPos);
   
   if(endPos > startPos)
   {
      string val = StringSubstr(json,startPos,endPos-startPos);
      return StringToDouble(val);
   }
   
   return 0.0;
}


//+------------------------------------------------------------------+
void ClosePositionByTicket(ulong ticket)
{
   if(!PositionSelectByTicket(ticket))
   {
      Print("SentriXBridge: ticket ", ticket, " not found.");
      return;
   }

   string symbol = PositionGetString(POSITION_SYMBOL);
   double volume = PositionGetDouble(POSITION_VOLUME);
   int    type   = (int)PositionGetInteger(POSITION_TYPE);

   // Get the absolute latest tick data
   MqlTick tick;
   if(!SymbolInfoTick(symbol, tick))
   {
      Print("SentriXBridge: Failed to get latest prices for ", symbol);
      return;
   }

   MqlTradeRequest req = {};
   MqlTradeResult  res = {};

   req.action    = TRADE_ACTION_DEAL;
   req.position  = ticket;
   req.symbol    = symbol;
   req.volume    = volume;
   req.type      = (type == POSITION_TYPE_BUY) ? ORDER_TYPE_SELL : ORDER_TYPE_BUY;
   req.price     = (type == POSITION_TYPE_BUY) ? tick.bid : tick.ask;
   req.deviation = 20;
   req.comment   = "Sentrix close";
   
   // --- FIX: Dynamic Filling Mode ---
   uint filling = (uint)SymbolInfoInteger(symbol, SYMBOL_FILLING_MODE);
   
   if((filling & SYMBOL_FILLING_FOK) != 0) 
   {
      req.type_filling = ORDER_FILLING_FOK;
   }
   else if((filling & SYMBOL_FILLING_IOC) != 0) 
   {
      req.type_filling = ORDER_FILLING_IOC;
   }
   else 
   {
      req.type_filling = ORDER_FILLING_RETURN;
   }
   // ---------------------------------

   if(OrderSend(req, res))
      Print("SentriXBridge: closed ticket ", ticket, " retcode=", res.retcode);
   else
      Print("SentriXBridge: close FAILED ticket ", ticket, " error=", GetLastError(), " retcode=", res.retcode);
}

//+------------------------------------------------------------------+
string BuildJSON()
{
   double balance  = AccountInfoDouble(ACCOUNT_BALANCE);
   double equity   = AccountInfoDouble(ACCOUNT_EQUITY);
   string currency = AccountInfoString(ACCOUNT_CURRENCY);
   long   login    = AccountInfoInteger(ACCOUNT_LOGIN);

   string posArray = "";
   int total = PositionsTotal();
   for(int i = 0; i < total; i++)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      if(!PositionSelectByTicket(ticket)) continue;

      string symbol    = PositionGetString(POSITION_SYMBOL);
      int    posType   = (int)PositionGetInteger(POSITION_TYPE);
      string direction = (posType == POSITION_TYPE_BUY) ? "Buy" : "Sell";
      double lots      = PositionGetDouble(POSITION_VOLUME);
      double entry     = PositionGetDouble(POSITION_PRICE_OPEN);
      double sl        = PositionGetDouble(POSITION_SL);
      double tp        = PositionGetDouble(POSITION_TP);
      double profit    = PositionGetDouble(POSITION_PROFIT);
      datetime openTime = (datetime)PositionGetInteger(POSITION_TIME);

      string timeStr = TimeToString(openTime, TIME_DATE | TIME_SECONDS);
      StringReplace(timeStr, ".", "-");

      string pos = StringFormat(
         "{\"Ticket\":%I64u,\"Symbol\":\"%s\",\"Direction\":\"%s\","
         "\"Lots\":%.2f,\"EntryPrice\":%.5f,\"StopLoss\":%.5f,"
         "\"TakeProfit\":%.5f,\"Profit\":%.2f,\"OpenTime\":\"%s\"}",
         ticket, symbol, direction,
         lots, entry, sl, tp, profit, timeStr);

      if(StringLen(posArray) > 0) posArray += ",";
      posArray += pos;
   }

   string serverTime = TimeToString(TimeTradeServer(), TIME_DATE | TIME_SECONDS);
   StringReplace(serverTime, ".", "-");
   
   string eventArray ="";
   
   for(int i=0;i<ArraySize(g_eventQueue);i++)
     {
         if(i>0){
            eventArray += ",";
            
         }
         eventArray += "\"" + g_eventQueue[i] + "\"";
      
     }
     
     ArrayResize(g_eventQueue,0);

    return StringFormat(
         "{\"Login\":%I64d,\"Balance\":%.2f,\"Equity\":%.2f,"
         "\"Currency\":\"%s\",\"ServerTime\":\"%s\","
         "\"Positions\":[%s],\"Events\":[%s]}", // Notice the "Events" added here
         login, balance, equity, currency, serverTime, posArray, eventArray);
}

void OnTradeTransaction(const MqlTradeTransaction &trans,
                        const MqlTradeRequest &request,
                        const MqlTradeResult &result)
{
   // ✅ GATE 1: Intercept at ORDER level — fires before the deal executes
   if(trans.type == TRADE_TRANSACTION_ORDER_ADD)
   {
      // Only block market orders (ORDER_TYPE_BUY / ORDER_TYPE_SELL)
      // Pending orders (limit/stop) are intentionally placed for future — don't block
      if(trans.order_type == ORDER_TYPE_BUY || trans.order_type == ORDER_TYPE_SELL)
      {
         bool timeWindowAllowed = IsCurrentTimeAllowedWindow();
         string activeSession   = GetActiveSession();
         g_SessionActive        = g_SessionActive && timeWindowAllowed;
         Print("g_SessionActive is ------"+g_SessionActive);

         int activeSessionTradeCount = 0;
         if(activeSession == "London")        activeSessionTradeCount = g_TradesLondon;
         else if(activeSession == "NewYork")  activeSessionTradeCount = g_TradesNewYork;

         bool violateSession       = !g_SessionActive;
         bool violateDailyTrades   = (g_MaxTradesDaily  > 0 && g_CurrentDailyTrades    >= g_MaxTradesDaily);
         bool violateSessionTrades = (g_MaxTradeSession > 0 && activeSessionTradeCount >= g_MaxTradeSession);

         if(violateSession || violateDailyTrades || violateSessionTrades)
         {
            string reason = violateSession       ? "Outside Session Window"
                          : violateDailyTrades   ? "Daily Limit Reached"
                          : "Session Limit Reached";

            string blockMsg = "🚨 SentriX PRE-BLOCK: Order rejected before execution | Reason: "
                            + reason + " | Order: " + (string)trans.order;
            Print(blockMsg);
            LogEvent(blockMsg);
            TriggerMT5Alert(blockMsg);

            // ✅ Delete the pending order before it fills
            MqlTradeRequest deleteReq = {};
            MqlTradeResult  deleteRes = {};
            deleteReq.action = TRADE_ACTION_REMOVE;
            deleteReq.order  = trans.order;
            OrderSend(deleteReq, deleteRes);

            if(deleteRes.retcode == TRADE_RETCODE_DONE)
               Print("✅ SentriX: Order successfully deleted before execution. Order: ", trans.order);
            else
               Print("⚠️ SentriX: Order delete failed (retcode: ", deleteRes.retcode, ") — falling back to deal-level close.");

            PushDataToCSharp();
            return;
         }
         
         string tradeSymbol = trans.symbol; // Get the exact pair they are trading
      int tradeDirection = (trans.order_type == ORDER_TYPE_BUY) ? 1 : -1;
      
      // Scan the allowed timeframes for this specific symbol
      int sigM15 = CheckCandlePattern(tradeSymbol, PERIOD_M15);
      int sigM30 = CheckCandlePattern(tradeSymbol, PERIOD_M30);
      int sigH1  = CheckCandlePattern(tradeSymbol, PERIOD_H1);
      int sigM1 = CheckCandlePattern(tradeSymbol, PERIOD_M1);
      int sigM5 = CheckCandlePattern(tradeSymbol, PERIOD_M5);
      int sigH4 = CheckCandlePattern(tradeSymbol, PERIOD_H4);
      
      // Did ANY of the allowed timeframes have the correct pattern?
      bool hasValidSetup = false;
      if(sigM1 == tradeDirection || sigM5 == tradeDirection ||sigM15 == tradeDirection || sigM30 == tradeDirection || sigH1 == tradeDirection || sigH4 == tradeDirection)
      {
         hasValidSetup = true;
      }

      // If they are missing the 3-candle setup, BLOCK THE TRADE
      if(!hasValidSetup)
      {
         string blockMsg = "🚨 SentriX PRE-BLOCK: No valid 3-Candle Pullback setup found for " + tradeSymbol + " on M15, M30, or H1.";
         Print(blockMsg);
         LogEvent(blockMsg);
         TriggerMT5Alert(blockMsg);

         // Delete the order (Your existing Gate 1 delete logic)
         MqlTradeRequest deleteReq = {};
         MqlTradeResult  deleteRes = {};
         deleteReq.action = TRADE_ACTION_REMOVE;
         deleteReq.order  = trans.order;
         OrderSend(deleteReq, deleteRes);
         
         PushDataToCSharp();
         return; // Stop processing
      }
      }
   }

   // ── GATE 2: Deal-level fallback (market orders fill too fast for Gate 1) ──
   // For instant market orders, ORDER_ADD and DEAL_ADD fire almost simultaneously.
   // Gate 1 may not win the race, so we keep the close-after-open as a safety net.
  if(trans.type == TRADE_TRANSACTION_DEAL_ADD && trans.position > 0)
   {
      if(HistoryDealSelect(trans.deal))
      {
         long dealType  = HistoryDealGetInteger(trans.deal, DEAL_TYPE);
         long entryType = HistoryDealGetInteger(trans.deal, DEAL_ENTRY);

         if(dealType == DEAL_TYPE_BUY || dealType == DEAL_TYPE_SELL)
         {
            if(entryType == DEAL_ENTRY_IN)
            {
               // 1. Check Session Limits
               bool timeWindowAllowed = IsCurrentTimeAllowedWindow();
               string activeSession   = GetActiveSession();
               g_SessionActive        = g_SessionActive && timeWindowAllowed;

               int activeSessionTradeCount = 0;
               if(activeSession == "London")       activeSessionTradeCount = g_TradesLondon;
               else if(activeSession == "NewYork") activeSessionTradeCount = g_TradesNewYork;

               bool violateSession       = !g_SessionActive;
               bool violateDailyTrades   = (g_MaxTradesDaily  > 0 && g_CurrentDailyTrades    >= g_MaxTradesDaily);
               bool violateSessionTrades = (g_MaxTradeSession > 0 && activeSessionTradeCount >= g_MaxTradeSession);
               
               // ✅ 2. NEW: Add the Phase 2 Pattern Check to Gate 2!
               string tradeSymbol = trans.symbol; 
               int tradeDirection = (dealType == DEAL_TYPE_BUY) ? 1 : -1;
               int sigM1 = CheckCandlePattern(tradeSymbol, PERIOD_M1);
               int sigM5 = CheckCandlePattern(tradeSymbol, PERIOD_M5);
               int sigM15 = CheckCandlePattern(tradeSymbol, PERIOD_M15);
               int sigM30 = CheckCandlePattern(tradeSymbol, PERIOD_M30);
               int sigH1  = CheckCandlePattern(tradeSymbol, PERIOD_H1);
               
              
               int sigH4 = CheckCandlePattern(tradeSymbol, PERIOD_H4);
               
               bool hasValidSetup = (sigM1 == tradeDirection||sigM5== tradeDirection||sigM15 == tradeDirection || sigM30 == tradeDirection || sigH1 == tradeDirection|| sigH4 == tradeDirection);

               // 3. MASTER BLOCK CHECK: If limits are broken OR pattern is invalid, KILL IT.
               if(sigM1 == tradeDirection || sigM5 == tradeDirection ||sigM15 == tradeDirection || sigM30 == tradeDirection || sigH1 == tradeDirection || sigH4 == tradeDirection)
               {
                  string reason = violateSession       ? "Outside Session Window"
                                : violateDailyTrades   ? "Daily Limit Reached"
                                : violateSessionTrades ? "Session Limit Reached"
                                : "Invalid 3-Candle Pattern";

                  string blockMsg = "🚨 SentriX BLOCK (fallback close) | Reason: " + reason
                                  + " | Ticket: " + (string)trans.position;
                  Print(blockMsg);
                  LogEvent(blockMsg);
                  TriggerMT5Alert(blockMsg);

                  // Wait a few milliseconds to ensure MT5 registers the position fully
                  for(int i = 0; i < 20; i++)
                  {
                     if(PositionSelectByTicket(trans.position)) break;
                     Sleep(10);
                  }

                  // ✅ Instant Close
                  ClosePositionByTicket(trans.position);
                  PushDataToCSharp();
               }
               else
               {
                  Print("✅ SentriX Tracking: Valid trade opened | Ticket: ", trans.position);
                  g_CurrentDailyTrades++;
                  if(activeSession == "London")       g_TradesLondon++;
                  else if(activeSession == "NewYork") g_TradesNewYork++;
                  
                  EnforceManualTradeRisk(trans.position,tradeSymbol,tradeDirection);
                  SaveStateOffline();
                  PushDataToCSharp();
                  
               }
            }
            else if(entryType == DEAL_ENTRY_OUT)
            {
               Print("🔒 SentriX Tracking: Trade closed | Ticket: ", trans.position);
               PushDataToCSharp();
            }
         }
      }
   }
}
bool IsManaged(ulong tickets){
   for(int i=0;i<ArraySize(g_managedTickets);i++)
     {
         if(g_managedTickets[i] == tickets){
            return true;
         }
         
     }
     return false;
}

void MarkAsManaged(ulong ticket)
{
   int size = ArraySize(g_managedTickets);
   ArrayResize(g_managedTickets, size + 1);
   g_managedTickets[size] = ticket;
}

//+------------------------------------------------------------------+
void CloseAllPositions()
{
   Print(" SentriX: Initiating absolute liquidation of all positions...");
   
   // Iterate backward so index doesn't shift when a position is removed
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket > 0)
      {
         ClosePositionByTicket(ticket);
      }
   }
}
//+------------------------------------------------------------------+
void ManagePositions1R()
{
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;

      // Skip if we already took partial profits on this ticket
      if(IsManaged(ticket)) continue; 

      double openPrice    = PositionGetDouble(POSITION_PRICE_OPEN);
      double currentPrice = PositionGetDouble(POSITION_PRICE_CURRENT);
      double sl           = PositionGetDouble(POSITION_SL);
      int    type         = (int)PositionGetInteger(POSITION_TYPE);
      double volume       = PositionGetDouble(POSITION_VOLUME);

      // If the user hasn't set an SL yet, we cannot calculate 1R
      if(sl == 0) continue; 

      // Calculate absolute risk in price points
      double risk = MathAbs(openPrice - sl);
      if(risk == 0) continue;

      bool hit1R = false;
      
      // Check if price has moved 1R in our favor
      if(type == POSITION_TYPE_BUY)
      {
         if(currentPrice >= openPrice + risk) hit1R = true;
      }
      else if(type == POSITION_TYPE_SELL)
      {
         if(currentPrice <= openPrice - risk) hit1R = true;
      }

      if(hit1R)
      {
         LogEvent("🎯 1R Target hit for ticket " + (string)ticket + ". Executing 50% close and BE.");
         
         // Calculate exactly 50% of the current lot size
         double halfVolume = volume / 2.0;
         
         if(PartialCloseAndBE(ticket, halfVolume, openPrice))
         {
            MarkAsManaged(ticket); // Ensure we never touch it again
         }
      }
   }
}

//+------------------------------------------------------------------+
bool PartialCloseAndBE(ulong ticket, double closeVolume, double openPrice)
{
   string symbol = PositionGetString(POSITION_SYMBOL);
   int    type   = (int)PositionGetInteger(POSITION_TYPE);
   
   // 1. Normalize volume to broker's allowed step sizes (e.g., 0.01)
   double volStep = SymbolInfoDouble(symbol, SYMBOL_VOLUME_STEP);
   double minVol  = SymbolInfoDouble(symbol, SYMBOL_VOLUME_MIN);
   
   closeVolume = MathFloor(closeVolume / volStep) * volStep;
   if(closeVolume < minVol) closeVolume = minVol;

   MqlTick tick;
   SymbolInfoTick(symbol, tick);

   // --- STEP 1: Execute Partial Close ---
   MqlTradeRequest reqClose = {};
   MqlTradeResult  resClose = {};
   
   reqClose.action    = TRADE_ACTION_DEAL;
   reqClose.position  = ticket;
   reqClose.symbol    = symbol;
   reqClose.volume    = closeVolume;
   reqClose.type      = (type == POSITION_TYPE_BUY) ? ORDER_TYPE_SELL : ORDER_TYPE_BUY;
   reqClose.price     = (type == POSITION_TYPE_BUY) ? tick.bid : tick.ask;
   reqClose.deviation = 20;
   
   // Use your dynamic filling mode fix!
   uint filling = (uint)SymbolInfoInteger(symbol, SYMBOL_FILLING_MODE);
   if((filling & SYMBOL_FILLING_FOK) != 0)      reqClose.type_filling = ORDER_FILLING_FOK;
   else if((filling & SYMBOL_FILLING_IOC) != 0) reqClose.type_filling = ORDER_FILLING_IOC;
   else                                         reqClose.type_filling = ORDER_FILLING_RETURN;

   if(!OrderSend(reqClose, resClose))
   {
      Print("SentriX: Partial Close failed for ticket ", ticket, " | Error: ", GetLastError());
      return false; // Stop here if the close fails
   }

   // --- STEP 2: Move SL to Break-Even ---
   // Note: We run a slight delay or just execute immediately. Immediate is usually fine for Hedging accounts.
   MqlTradeRequest reqMod = {};
   MqlTradeResult  resMod = {};
   
   reqMod.action   = TRADE_ACTION_SLTP;
   reqMod.position = ticket;
   reqMod.symbol   = symbol;
   reqMod.sl       = openPrice; // Move SL to exactly the entry price
   reqMod.tp       = PositionGetDouble(POSITION_TP); // Keep existing TP where it is

   if(!OrderSend(reqMod, resMod))
   {
      Print("SentriX: Move to BE failed for ticket ", ticket, " | Error: ", GetLastError());
      // We return true anyway because the partial close succeeded, and we don't want to loop forever.
      return true; 
   }

   return true;
}

void RecoverManagedTickets()
{
   int recoveredCount = 0;
   
   for(int i = PositionsTotal() - 1; i >= 0; i--)
   {
      ulong ticket = PositionGetTicket(i);
      if(ticket == 0) continue;
      
      string symbol    = PositionGetString(POSITION_SYMBOL);
      double openPrice = PositionGetDouble(POSITION_PRICE_OPEN);
      double sl        = PositionGetDouble(POSITION_SL);
      
      if(sl == 0) continue; // No SL set at all
      
      // We use a tiny tolerance (2 points) for floating point comparison safety
      double point = SymbolInfoDouble(symbol, SYMBOL_POINT);
      
      if(MathAbs(openPrice - sl) <= (point * 2)) 
      {
         // If the SL is basically identical to the Open Price, it's at Break-Even
         if(!IsManaged(ticket)) 
         {
            MarkAsManaged(ticket);
            recoveredCount++;
         }
      }
   }
   
   if(recoveredCount > 0)
   {
      Print("️ SentriX Recovery: Restored memory for ", recoveredCount, " managed tickets (already at BE).");
   }
}
//+------------------------------------------------------------------+


void LogEvent(string message){
   string timeStr = TimeToString(TimeCurrent(), TIME_DATE | TIME_SECONDS);
   string stampedMessage = "[" + timeStr + "] " + message;

   Print("Sentrix Event---" + stampedMessage);
   
   //Route the data based on connection status
   if(g_pipe == INVALID_HANDLE)
   {
      SaveEventOffline(stampedMessage); //Save to disk!
   }
   else
   {
      int size = ArraySize(g_eventQueue);
      ArrayResize(g_eventQueue, size + 1);
      g_eventQueue[size] = stampedMessage; //Send instantly!
   }
}

//+------------------------------------------------------------------+


void  SaveConfigOffline(){

   int handle = FileOpen(CONFIG_FILE, FILE_WRITE | FILE_TXT | FILE_ANSI);
   
   if(handle != INVALID_HANDLE)
   {
      
       string data = StringFormat("%d^%d^%d^%.2f^%d^%s^%s^%d^%d^%d", 
   g_SessionActive, g_MaxTradesDaily, g_CurrentDailyTrades, 
   g_MaxLossPercent, g_Manage1R, g_AllowedSessions, 
   g_ActiveSessionName, g_MaxTradeSession,
   g_UtcOffsetMinutes,
   g_OffsetCalibrated); 
         
      FileWrite(handle, data);
      FileClose(handle);
   }
   
   
}

void LoadConfigOffline(){
   if(FileIsExist(CONFIG_FILE))
   {
      int handle = FileOpen(CONFIG_FILE, FILE_READ | FILE_TXT | FILE_ANSI);
      if(handle != INVALID_HANDLE)
      {
         string data = FileReadString(handle);
         string parts[];
         
         // FIX 2: Split by ^ to match the Save function
         StringSplit(data, '^', parts);
         
         if(ArraySize(parts) >= 10)
         {
            //g_SessionActive = (bool)StringToInteger(parts[0]);
            g_MaxTradesDaily =  (int)StringToInteger(parts[1]);
            //g_CurrentDailyTrades = (int)StringToInteger(parts[2]);
            g_MaxLossPercent = StringToDouble(parts[3]);
            g_Manage1R = (bool)StringToInteger(parts[4]);
            g_AllowedSessions = parts[5];
            
            g_ActiveSessionName = parts[6];
            g_MaxTradeSession = (int)StringToInteger(parts[7]);
            g_UtcOffsetMinutes = (int)StringToInteger(parts[8]);
            g_OffsetCalibrated = (bool)StringToInteger(parts[9]);
           // if(g_OffsetCalibrated)
                 // Print("SentriX: Restored calibrated offset from disk: ", 
                            //  g_UtcOffsetMinutes, " min. C# not needed for time.");
          // if(ArraySize(parts) >= 10)
              // g_UtcOffsetMinutes = (int)StringToInteger(parts[8]); 
            Print("SentriX: Loaded offline config. Sessions: ", g_AllowedSessions,
         " | Offset: ", g_UtcOffsetMinutes, " min | Calibrated: ", g_OffsetCalibrated);

                        //TriggerWindowsToast("Config Loaded","SentriX: Loaded offline rules from local disk.");
            
         }
         FileClose(handle);
      }
   }
}


string ExtractString(string json, string key)
{
   int startPos = StringFind(json, key);
   
   // FIX 3: Must return empty string ""
   if(startPos < 0) return ""; 
   
   startPos += StringLen(key);
   
   // FIX 4: Fixed spelling to quoteStart
   int quoteStart = StringFind(json, "\"", startPos);
   if(quoteStart < 0) return "";
   
   // FIX 5: Added the completely missing quoteEnd calculation line!
   int quoteEnd = StringFind(json, "\"", quoteStart + 1);
   if(quoteEnd < 0) return "";
   
   return StringSubstr(json, quoteStart + 1, quoteEnd - quoteStart - 1);
}



//+------------------------------------------------------------------+

// 1. Writes a single event to the offline text file
void SaveEventOffline(string message)
{
   int handle = FileOpen(OFFLINE_QUEUE_FILE, FILE_WRITE | FILE_READ | FILE_TXT | FILE_ANSI);
   if(handle != INVALID_HANDLE)
   {
      FileSeek(handle, 0, SEEK_END); 
      FileWrite(handle, message);
      FileClose(handle);
   }
}


// 2. Reads the file, dumps it into the live queue, and deletes the file
void SyncOfflineEvents()
{
   if(FileIsExist(OFFLINE_QUEUE_FILE))
   {
      int handle = FileOpen(OFFLINE_QUEUE_FILE, FILE_READ | FILE_TXT | FILE_ANSI);
      if(handle != INVALID_HANDLE)
      {
         Print("SentriX: Syncing offline events to C#...");
         while(!FileIsEnding(handle))
         {
            string msg = FileReadString(handle);
            if(StringLen(msg) > 0)
            {
               // Add it back to the live array to be sent to C#
               int size = ArraySize(g_eventQueue);
               ArrayResize(g_eventQueue, size + 1);
               g_eventQueue[size] = msg; 
            }
         }
         FileClose(handle);
         FileDelete(OFFLINE_QUEUE_FILE); 
      }
   }
}


//+----------------------------------------------------------

void TriggerMT5Alert(string message)
{
   Print("TriggerMT5Alert is triggered");
   Alert("SentriX Alert: ", message);
}

void TriggerWindowsToast(string title, string message)
{
   // Formats a distinct string that your C# app can recognize and separate from standard logs
   string toastCommand = "TOAST_NOTIFICATION|" + title + "|" + message;
   
   // We reuse your existing event queue system to send this instantly to C#
   LogEvent(toastCommand);
}
//+-------------------------------------------------------

void SaveStateOffline(){
   int handle = FileOpen(STATE_FILE, FILE_WRITE | FILE_TXT | FILE_ANSI);
   if(handle != INVALID_HANDLE){
      MqlDateTime timeStruct;
      TimeToStruct(TimeCurrent(),timeStruct);
      int currentDay = timeStruct.day;
      // Format: DailyTrades ^ SessionTrades ^ LastRecordedDay
      //string data = StringFormat("%d^%d^%d", g_CurrentDailyTrades, g_CurrentSessionTrades, currentDay );
      string data = StringFormat("%d^%d^%d^%d", g_CurrentDailyTrades, g_TradesLondon, g_TradesNewYork, currentDay);
      FileWrite(handle, data);
      FileClose(handle);
   }
}

void LoadStateOffline(){
   
   if(FileIsExist(STATE_FILE))
   {
      int handle = FileOpen(STATE_FILE, FILE_READ | FILE_TXT|FILE_ANSI);
      if(handle != INVALID_HANDLE)
     {
        string data = FileReadString(handle);
        string parts[];
        
        StringSplit(data, '^', parts);
         
         // FIX 1: Expect 4 parts now (Daily, London, NY, Day)
         if(ArraySize(parts) >= 4)
         {
            // FIX 2: The day is now at index 3
            int savedDay   = (int)StringToInteger(parts[3]); 
            
            MqlDateTime timeStruct;
            TimeToStruct(TimeCurrent(), timeStruct);
            int currentDay = timeStruct.day;
            
            // If the day matches, load the counts. Otherwise, start fresh.
            if(savedDay == currentDay)
            {
               g_CurrentDailyTrades = (int)StringToInteger(parts[0]);
               g_TradesLondon       = (int)StringToInteger(parts[1]);
               g_TradesNewYork      = (int)StringToInteger(parts[2]);
               
               Print("🛡️ SentriX State Loaded | Daily: ", g_CurrentDailyTrades, " | London: ", g_TradesLondon, " | NY: ", g_TradesNewYork);
            }
            else
            {
               Print("🛡️ SentriX State: New day detected on load. Resetting counters.");
               g_CurrentDailyTrades = 0;
               g_TradesLondon       = 0;
               g_TradesNewYork      = 0;
               SaveStateOffline();
            }
         }
         FileClose(handle);
      }
   }
}


void CheckDailyReset()
{
   static int lastDay = -1; 
   
   // --- CORRECT MQL5 WAY TO GET THE CURRENT DAY ---
   MqlDateTime timeStruct;
   TimeToStruct(TimeCurrent(), timeStruct);
   int currentDay = timeStruct.day; 
   // -----------------------------------------------

   if(lastDay != -1 && currentDay != lastDay)
   {
      Print("🛡️ SentriX Guardian: 🌅 New trading day detected. Resetting local MT5 counters.");
      
      g_CurrentDailyTrades = 0;
      //g_CurrentSessionTrades = 0;
      g_TradesLondon       = 0;
      g_TradesNewYork      = 0;
      
      SaveStateOffline(); 
   }
   
   lastDay = currentDay;
}

//+-------------------------------------------------------------








//+-------------------------------------------------------------



string GetActiveSession()
{
   if(MQLInfoInteger(MQL_TESTER)) 
      {
         g_SessionActive = true;
         return "London";
      }
   datetime etTime = TimeEastern();
   //printf(etTime);
   MqlDateTime etStruct;
   TimeToStruct(etTime, etStruct);
   
   // Convert current ET time into total minutes for easy comparison
   int currentMinutesET = (etStruct.hour * 60) + etStruct.min;
   //printf(currentMinutesET);
   
   // Rule 1: London Session (2:00 AM to 7:59 AM ET)
   // 2:00 = 120 mins | 7:59 = 479 mins
   if(currentMinutesET >= 120 && currentMinutesET < 480)
   {
      g_SessionActive = true;
      //printf("Current session name London");
      return "London";
      
   }
   
   // Rule 2: New York Session (8:00 AM to 11:30 AM ET)
   // 8:00 = 480 mins | 11:30 = 690 mins
   if(currentMinutesET >= 480 && currentMinutesET <= 690)
   {
      g_SessionActive= true;
      //printf("Current Session Name NewYork");
      return "NewYork"; // Matches your C# exact string
      
   }
   
   // Outside allowed hours
   //printf("outiside allowed hour");
   g_SessionActive = false;
   return "None";
   
}

void CheckSession(string name, int startMin, int endMin, int nowMin,
                  int &latestStart, string &selected)
{
   bool inside = false;

   if(startMin <= endMin)
      inside = (nowMin >= startMin && nowMin <= endMin);
   else // overnight
      inside = (nowMin >= startMin || nowMin <= endMin);

   if(inside && startMin > latestStart)
   {
      latestStart = startMin;
      selected    = name;
   }
}



//+-------------------------------------------------------------

datetime TimeEastern()
{
   datetime gmt = TimeGMT();
   MqlDateTime dt;
   TimeToStruct(gmt, dt);
   
   int offsetHours = -5; // Default to EST (UTC-5)
   
   // US DST Rule: Starts 2nd Sunday in March, Ends 1st Sunday in November
   bool isDST = false;
   
   if(dt.mon > 3 && dt.mon < 11) 
   {
      isDST = true;
   }
   else if(dt.mon == 3) 
   {
      // March: Check if past 2nd Sunday
      int previousSunday = dt.day - dt.day_of_week;
      if(previousSunday >= 8) isDST = true; 
   }
   else if(dt.mon == 11) 
   {
      // November: Check if before 1st Sunday
      int previousSunday = dt.day - dt.day_of_week;
      if(previousSunday <= 0) isDST = true;
   }
   
   if(isDST) offsetHours = -4; // Switch to EDT (UTC-4)
   
   return gmt + (offsetHours * 3600);
}

bool IsCurrentTimeAllowedWindow()
{
   if(MQLInfoInteger(MQL_TESTER)) return true;
   // 1. If no sessions are configured, block trading
   if(StringLen(g_AllowedSessions) == 0 || g_AllowedSessions == "None") 
      return false; 
      
   int currentMin = GetLocalTimeMinutes();
   string sessions[];
   
   //printf("Allowed Session "+ g_AllowedSessions);
   // Split by the pipe character (e.g., london:11:30-12:55 | newyork:15:56-14:30)
   int count = StringSplit(g_AllowedSessions, '|', sessions);
   
   for(int i = 0; i < count; i++)
   {
      int firstColon = StringFind(sessions[i], ":");
      if(firstColon < 0) continue;
      
      string sessionName = StringSubstr(sessions[i], 0, firstColon);
      string times = StringSubstr(sessions[i], firstColon + 1); 
      
      int dashPos = StringFind(times, "-");
      if(dashPos < 0) continue;
      
      string startStr = StringSubstr(times, 0, dashPos); 
      string endStr   = StringSubstr(times, dashPos + 1); 
      
      int startColon = StringFind(startStr, ":");
      int endColon   = StringFind(endStr, ":");
      if(startColon < 0 || endColon < 0) continue;
      
      // Convert HH:mm to absolute minutes
      int startTotal = (int)StringToInteger(StringSubstr(startStr, 0, startColon)) * 60 + 
                       (int)StringToInteger(StringSubstr(startStr, startColon + 1));
                       
      int endTotal   = (int)StringToInteger(StringSubstr(endStr, 0, endColon)) * 60 + 
                       (int)StringToInteger(StringSubstr(endStr, endColon + 1));
      
      // Check if current time falls within this window
      if(startTotal <= endTotal)
      {
         // Standard daytime session
         if(currentMin >= startTotal && currentMin <= endTotal)
         {
            //Print("✅ SentriX: Trade allowed by Daytime Session [", sessionName, "]");
            return true;
         }
      }
      else
      {
         // Midnight cross session (starts before midnight, ends the next day)
         if(currentMin >= startTotal || currentMin <= endTotal)
         {
            //Print("✅ SentriX: Trade allowed by Midnight-Cross Session [", sessionName, "]");
            return true;
         }
      }
   }
   
   // If the loop finishes and no window matched the current time, block it
   //Print("🚨 SentriX: Time [", currentMin, " mins] is outside all allowed windows.");
   return false; 
}

int GetLocalTimeMinutes()
{
   // Because C# and MT5 are on the same machine, TimeLocal() exactly matches C# DateTime.Now
   MqlDateTime t;
   TimeToStruct(TimeLocal(), t); 
   
   int localMinutes = t.hour * 60 + t.min;
   //PrintFormat("Current Local Time: %02d:%02d", t.hour, t.min);
   //Print("Localtime my machine---"+localMinutes);
   return localMinutes;
}
//+--------------------------------------------------------------

void UpdateUIProtection()
{
   // 1. Determine active session counts
   string activeSession = GetActiveSession();
   int activeSessionTradeCount = 0;
   if(activeSession == "London")       activeSessionTradeCount = g_TradesLondon;
   else if(activeSession == "NewYork") activeSessionTradeCount = g_TradesNewYork;

   // 2. Evaluate the exact same rules used in OnTradeTransaction
   bool timWindowAllowed     = IsCurrentTimeAllowedWindow();
   bool violateSession       = !(g_SessionActive && timWindowAllowed); 
   bool violateDailyTrades   = (g_MaxTradesDaily > 0 && g_CurrentDailyTrades >= g_MaxTradesDaily);
   bool violateSessionTrades = (g_MaxTradeSession > 0 && activeSessionTradeCount >= g_MaxTradeSession);
   static bool wasGloballyBlocked = false;
   // 3. Apply or Remove UI blocks based on the rules
   if(violateSession || violateDailyTrades || violateSessionTrades)
   {
      if(!wasGloballyBlocked) // If it wasn't blocked a second ago, print the reason now!
      {
         string reason = violateSession ? "Outside Allowed Session Time" : 
                        (violateDailyTrades ? "Daily Max Trades Reached" : "Session Max Trades Reached");
         
         Print("🔒 SentriX: Global UI Lock Activated. Reason: ", reason);
         wasGloballyBlocked = true;
      }
      //Print("Applying UI block method");
      ApplyUIBlocks();
   }
   else
   {
      if(wasGloballyBlocked)
      {
         Print("🔓 SentriX: Global UI Lock Lifted. Trading is allowed.");
         wasGloballyBlocked = false;
      }
      RemoveUIBlocks();
   }
}

//+--------------------------------------------------------------


void ApplyUIBlocks()
{
   if(MQLInfoInteger(MQL_TESTER)) return;
   long currChart = ChartFirst();
   
   while(currChart >= 0)
   {
      string chartSym = ChartSymbol(currChart);
      
      // 1. Check if this specific chart has a valid setup on any timeframe
      int sigM1 = CheckCandlePattern(chartSym, PERIOD_M1);
      int sigM5 = CheckCandlePattern(chartSym, PERIOD_M5);
      int sigM15 = CheckCandlePattern(chartSym, PERIOD_M15);
      int sigM30 = CheckCandlePattern(chartSym, PERIOD_M30);
      int sigH1  = CheckCandlePattern(chartSym, PERIOD_H1);
       int sigH4 = CheckCandlePattern(chartSym, PERIOD_H4);

      bool hasAnySetup = (sigM1 != 0||sigM5!= 0||sigM15 != 0 || sigM30 != 0 || sigH1 != 0 || sigH4 !=0);
      
      // 2. Check your existing session limits
      bool violateSession = !(g_SessionActive && IsCurrentTimeAllowedWindow()); 
      bool violateDaily   = (g_MaxTradesDaily > 0 && g_CurrentDailyTrades >= g_MaxTradesDaily);
      
      string activeSession = GetActiveSession();
      int sessionTrades = (activeSession == "London") ? g_TradesLondon : ((activeSession == "NewYork") ? g_TradesNewYork : 0);
      bool violateSessionTrades = (g_MaxTradeSession > 0 && sessionTrades >= g_MaxTradeSession);

      // 3. THE MASTER RULE: If limits are hit OR there is NO setup, lock the chart!
      if(violateSession || violateDaily || violateSessionTrades || !hasAnySetup)
      {
         if(ChartGetInteger(currChart, CHART_SHOW_ONE_CLICK))
            ChartSetInteger(currChart, CHART_SHOW_ONE_CLICK, false);
            
         ChartSetInteger(currChart, CHART_DRAG_TRADE_LEVELS, false);
         ChartSetInteger(currChart, CHART_CONTEXT_MENU, false);
      }
      else
      {
         // If we are in a session AND a setup exists, unlock the chart so they can trade!
         ChartSetInteger(currChart, CHART_DRAG_TRADE_LEVELS, true);
         ChartSetInteger(currChart, CHART_CONTEXT_MENU, true);
         // (We don't force One-Click open, we just leave it alone)
      }
      
      currChart = ChartNext(currChart);
   }

   g_UIRestricted = true;
}

void RemoveUIBlocks()
{
   if(!g_UIRestricted) return; // Already lifted

   long currChart = ChartFirst();
   
   while(currChart >= 0)
   {
      // Restore Drag & Drop trading levels on all charts
      ChartSetInteger(currChart, CHART_DRAG_TRADE_LEVELS, true);
      
      // (We intentionally do NOT auto-reopen the One-Click panel here, 
      // because the user might have had it closed manually before the block)
      ChartSetInteger(currChart,CHART_CONTEXT_MENU, true);
      currChart = ChartNext(currChart);
   }
   
   g_UIRestricted = false;
}

//---------------------------------------------------------------------


void KillNewOrderWindow()
{
   if(MQLInfoInteger(MQL_TESTER)) return;
   // Check Session Limits
   bool violateSession = !(g_SessionActive && IsCurrentTimeAllowedWindow()); 
   bool violateDaily   = (g_MaxTradesDaily > 0 && g_CurrentDailyTrades >= g_MaxTradesDaily);
   
   string activeSession = GetActiveSession();
   int sessionTrades = (activeSession == "London") ? g_TradesLondon : ((activeSession == "NewYork") ? g_TradesNewYork : 0);
   bool violateSessionTrades = (g_MaxTradeSession > 0 && sessionTrades >= g_MaxTradeSession);

   // Check if the CURRENT chart they are looking at has a setup
   int sigM1 = CheckCandlePattern(_Symbol, PERIOD_M1);
   int sigM5 = CheckCandlePattern(_Symbol, PERIOD_M5);
   int sigM15 = CheckCandlePattern(_Symbol, PERIOD_M15);
   int sigM30 = CheckCandlePattern(_Symbol, PERIOD_M30);
   int sigH1  = CheckCandlePattern(_Symbol, PERIOD_H1);
   int sigH4 = CheckCandlePattern(_Symbol, PERIOD_H4);
   bool hasAnySetup = (sigM1 != 0 || sigM5 != 0 || sigM15 != 0 || sigM30 != 0 || sigH1 != 0 || sigH4 != 0);
      string blockReason = "";
   if(violateSession) blockReason = "Outside Trading Session"; 
   else if(violateDaily) blockReason = "Daily Max Trades Reached";
   else if(violateSessionTrades) blockReason = "Session Max Trades Reached";
   else if(!hasAnySetup) blockReason = "No 3-Candle Setup on " + _Symbol;
   // If limits hit OR NO setup exists, kill the window!
   if(violateSession || violateDaily || violateSessionTrades || !hasAnySetup)
   {
      long hwnd = GetWindow(GetDesktopWindow(), GW_CHILD);
      ushort titleBuffer[256]; 
      
      while(hwnd != 0)
      {
         int len = GetWindowTextW(hwnd, titleBuffer, 256);
         if(len > 0)
         {
            string winTitle = ShortArrayToString(titleBuffer);
            if(winTitle == "Order" || StringFind(winTitle, "Order: ") == 0)
            {
               Print("🚨 SentriX: Intercepted F9 window. Reason: Limits hit OR No 3-Candle Setup.");
               Print("🚨 SentriX: Intercepted F9 window. Reason: ", blockReason);
               SendMessageW(hwnd, WM_CLOSE, 0, 0); 
            }
         }
         hwnd = GetWindow(hwnd, GW_HWNDNEXT);
      }
   }
}
//---------------------------------------------------------------------

bool IsNewBar(ENUM_TIMEFRAMES timeframe)
{
   static datetime lastTimeM5 =0;
   static datetime lastTimeM1 = 0;
   static datetime lastTimeM15 = 0;
   static datetime lastTimeM30 = 0;
   static datetime LastTimeH1 = 0;
   static datetime lastTimeh4 = 0;
   static 
   datetime current_time = iTime(_Symbol,timeframe,0);
   
   if(timeframe == PERIOD_M1)
   {
      if(current_time != lastTimeM1)
      {
         lastTimeM1 = current_time;
         return true;
      }
   }
   if(timeframe == PERIOD_M5)
   {
      if(current_time != lastTimeM5)
        {
         lastTimeM5 = current_time;
         return true;
        }
   }
   if(timeframe == PERIOD_M15)
   {
      if(current_time != lastTimeM15)
        {
            lastTimeM15= current_time;
            return true;
        }
        
   }
   if(PERIOD_M30)
     {
         if(current_time != lastTimeM30)
           {
               lastTimeM30 = current_time;
               return true;
           }
     }
     if(PERIOD_H1){
         if(current_time != LastTimeH1)
         {
               LastTimeH1 = current_time;
               return true;
         }
     }
     if(PERIOD_H4)
     {
      if(current_time != lastTimeh4)
        {
            lastTimeh4 = current_time;
            return true;
         
        }
     }
     
     return false;
}

//------------------------------------------------------------------------

int CheckCandlePattern(string sym, ENUM_TIMEFRAMES tf)
{
   int trendDirection = CheckHigherTimeframeTrend(sym);
   
   if(trendDirection == 0) return 0;
   // We use 'sym' instead of '_Symbol' so it can check any pair in the world
   double O1 = iOpen(sym, tf, 1);  double C1 = iClose(sym, tf, 1);
   double O2 = iOpen(sym, tf, 2);  double C2 = iClose(sym, tf, 2);
   double O3 = iOpen(sym, tf, 3);  double C3 = iClose(sym, tf, 3);
   
   

   // --- CHECK BULLISH SETUP: Up, Down, Up ---
   bool isBullish3 = (C3 > O3); 
   bool isBearish2 = (C2 < O2); 
   bool isBullish1 = (C1 > O1) && IsStrongCandle(sym,tf,1,0.40); 
   
   if(isBullish3 && isBearish2 && isBullish1 && trendDirection ==1) return 1; // 1 = BUY

   // --- CHECK BEARISH SETUP: Down, Up, Down ---
   bool isBearish3 = (C3 < O3); 
   bool isBullish2 = (C2 > O2); 
   bool isBearish1 = (C1 < O1) && IsStrongCandle(sym,tf,1,0.40); 
   
   if(isBearish3 && isBullish2 && isBearish1 && trendDirection == -1) return -1; // -1 = SELL

   return 0; // 0 = No valid pattern
}
//-----------------------------------------------------------------------

int CheckHigherTimeframeTrend(string sym)
{
   // 1. Get the Open and Close of the last closed 4-Hour candle
   double h4Open  = iOpen(sym, PERIOD_H4, 1);
   double h4Close = iClose(sym, PERIOD_H4, 1);
   
   // 2. Get the Open and Close of the last closed Daily candle
   double d1Open  = iOpen(sym, PERIOD_D1, 1);
   double d1Close = iClose(sym, PERIOD_D1, 1);
   
   // 3. Determine directions
   bool isH4Bullish = (h4Close > h4Open);
   bool isD1Bullish = (d1Close > d1Open);
   
   bool isH4Bearish = (h4Close < h4Open);
   bool isD1Bearish = (d1Close < d1Open);
   
   // 4. If they agree perfectly, return the direction!
   if(isH4Bullish && isD1Bullish) return 1;  // Trend is UP (Allowed to Buy)
   if(isH4Bearish && isD1Bearish) return -1; // Trend is DOWN (Allowed to Sell)
   
   return 0; // Mixed trend (e.g., H4 is Up but Daily is Down). Do not trade.
}

//---------------------------------------------------------------------------

//Weak candle pattern

bool IsStrongCandle(string sym, ENUM_TIMEFRAMES tf, int index, double minBodyPercent = 0.40)
{
   double open  = iOpen(sym, tf, index);
   double close = iClose(sym, tf, index);
   double high  = iHigh(sym, tf, index);
   double low   = iLow(sym, tf, index);
   
   double totalSize = high - low;
   double bodySize  = MathAbs(open - close); // MathAbs ensures it's always a positive number
   
   // If the candle is a completely flat line, totalSize is 0. Avoid divide-by-zero error!
   if(totalSize == 0) return false; 
   
   double currentBodyPercent = bodySize / totalSize;
   
   // Return true if the body is bigger than or equal to our required percentage
   return (currentBodyPercent >= minBodyPercent);
}

//------------------------------------------------------------------------------------


bool HasOpenPosition(string sym){
   for(int i= PositionsTotal() -1 ;i>=0;i--)
     {
         if(PositionGetSymbol(i) ==sym)
            return true;
     }
     return false;
}

//------------------------------------------------------------------------------------

//Automated The Trade Entry and risk management

void ExecuteAutomatedTrade(string sym, int tradeDirection,ENUM_TIMEFRAMES tfs = PERIOD_CURRENT)
{
   // 1. Safety Check: Only 1 trade per symbol at a time!
   if(HasOpenPosition(sym))
   {
      //Print("SentriX Auto: Skipped valid setup. A trade is already running on ", sym);
      return;
   }

   // 2. Get current market prices
   MqlTick tick;
   if(!SymbolInfoTick(sym, tick)) return;

   double entryPrice = (tradeDirection == 1) ? tick.ask : tick.bid;
   int orderType     = (tradeDirection == 1) ? ORDER_TYPE_BUY : ORDER_TYPE_SELL;

   // 3. Calculate Smart Stop Loss
   double stopLoss = GetStructureStopLoss(sym, tfs, tradeDirection, 20);
   if(stopLoss == 0) return;

   // 4. Calculate exactly 1% Risk Lot Size
   double lotSize = CalculateRiskLotSize(sym, entryPrice, stopLoss, 1.0);
   if(lotSize == 0) return;

   // 5. Build the Broker Request
   MqlTradeRequest req = {};
   MqlTradeResult  res = {};

   req.action    = TRADE_ACTION_DEAL;
   req.symbol    = sym;
   req.volume    = lotSize;
   req.type      = (ENUM_ORDER_TYPE)orderType;
   req.price     = entryPrice;
   req.sl        = stopLoss;
   req.tp        = 0; 
   req.deviation = 20;
   req.magic     = 7777; 
   req.comment   = "SentriX Auto Entry";

   // Dynamic Filling Mode
   uint filling = (uint)SymbolInfoInteger(sym, SYMBOL_FILLING_MODE);
   if((filling & SYMBOL_FILLING_FOK) != 0)      req.type_filling = ORDER_FILLING_FOK;
   else if((filling & SYMBOL_FILLING_IOC) != 0) req.type_filling = ORDER_FILLING_IOC;
   else                                         req.type_filling = ORDER_FILLING_RETURN;

   // 6. Pull the Trigger!
   if(OrderSend(req, res))
   {
      string dirStr = (tradeDirection == 1) ? "BUY" : "SELL";
      string msg = "SentriX AUTOMATED ENTRY: Placed " + dirStr + " on " + sym + 
                   " | Lots: " + DoubleToString(lotSize, 2) + 
                   " | SL: " + DoubleToString(stopLoss, 5);
      Print(msg);
      LogEvent(msg);
   }
   else
   {
      Print("SentriX Auto Failed: Error ", GetLastError(), " | Retcode: ", res.retcode);
   }
}

/*{
   double point = SymbolInfoDouble(sym, SYMBOL_POINT);
   double bufferPriced = bufferPoints * point; 
   
   if(tradeDirection == 1) // BULLISH (BUY) - We need the Swing Low
   {
      int lowestIndex = iLowest(sym, tf, MODE_LOW, lookbackCandles, 1);
      if(lowestIndex<0) lowestIndex =1;
      
      double swingLow = iLow(sym, tf, lowestIndex);
      return swingLow - bufferPriced;
   }
   else if(tradeDirection == -1) // BEARISH (SELL) - We need the Swing High
   {
      int highestIndex = iHighest(sym, tf, MODE_HIGH, lookbackCandles, 1);
      if(highestIndex<0) highestIndex =1;
      double swingHigh = iHigh(sym, tf, highestIndex);
      return swingHigh + bufferPriced;
   }
   
   return 0.0;
}*/

double GetStructureStopLoss(string sym, ENUM_TIMEFRAMES tf, int tradeDirection, int bufferPoints = 20)
{
   double point = SymbolInfoDouble(sym, SYMBOL_POINT);
   double bufferPriced = bufferPoints * point; 
   
   if(tradeDirection == 1) // BULLISH (BUY) - Find most recent Swing Low
   {
      // Scan back up to 15 candles to find a true structural swing low (a "V" shape)
      for(int i = 1; i <= 15; i++)
      {
         double currentLow = iLow(sym, tf, i);
         double prevLow    = iLow(sym, tf, i+1);
         double nextLow    = iLow(sym, tf, i-1);
         
         // If the current low is lower than the candle before it and after it, it's a swing
         if(currentLow < prevLow && currentLow < nextLow)
         {
            return currentLow - bufferPriced;
         }
      }
      
      // Fallback: If no swing is found, use the lowest point of the 3-candle setup itself
      int lowestIndex = iLowest(sym, tf, MODE_LOW, 3, 1);
      if(lowestIndex < 0) lowestIndex = 1;
      return iLow(sym, tf, lowestIndex) - bufferPriced;
   }
   else if(tradeDirection == -1) // BEARISH (SELL) - Find most recent Swing High
   {
      // Scan back up to 15 candles to find a true structural swing high (an inverted "V" shape)
      for(int i = 1; i <= 15; i++)
      {
         double currentHigh = iHigh(sym, tf, i);
         double prevHigh    = iHigh(sym, tf, i+1);
         double nextHigh    = iHigh(sym, tf, i-1);
         
         // If the current high is higher than the candle before it and after it, it's a swing
         if(currentHigh > prevHigh && currentHigh > nextHigh)
         {
            return currentHigh + bufferPriced;
         }
      }
      
      // Fallback: If no swing is found, use the highest point of the 3-candle setup itself
      int highestIndex = iHighest(sym, tf, MODE_HIGH, 3, 1);
      if(highestIndex < 0) highestIndex = 1;
      return iHigh(sym, tf, highestIndex) + bufferPriced;
   }
   
   return 0.0;
}

double CalculateRiskLotSize(string sym, double entryPrice, double stopLossPrice, double riskPercent = 1.0)
{
   double balance = AccountInfoDouble(ACCOUNT_BALANCE);
   double riskMoney = balance * (riskPercent / 100.0);
   
   double tickValue = SymbolInfoDouble(sym, SYMBOL_TRADE_TICK_VALUE);
   double tickSize  = SymbolInfoDouble(sym, SYMBOL_TRADE_TICK_SIZE);
   
   double slDistance = MathAbs(entryPrice - stopLossPrice);
   if(slDistance == 0) return 0.0; 
   
   double riskPerOneLot = (slDistance / tickSize) * tickValue;
   double calculatedLots = riskMoney / riskPerOneLot;
   
   double minLot  = SymbolInfoDouble(sym, SYMBOL_VOLUME_MIN);
   double maxLot  = SymbolInfoDouble(sym, SYMBOL_VOLUME_MAX);
   double lotStep = SymbolInfoDouble(sym, SYMBOL_VOLUME_STEP);
   
   calculatedLots = MathFloor(calculatedLots / lotStep) * lotStep;
   
   if(calculatedLots < minLot) calculatedLots = minLot;
   if(calculatedLots > maxLot) calculatedLots = maxLot;
   
   return calculatedLots;
}


// auto correct manual trades for 1% risk and stop loss

void EnforceManualTradeRisk(ulong ticket, string sym, int tradeDirection)
{
   if(!PositionSelectByTicket(ticket)) return;
   
   double openPrice  = PositionGetDouble(POSITION_PRICE_OPEN);
   double currentVol = PositionGetDouble(POSITION_VOLUME);
   
   // 1. Calculate the Structure SL
   //double structureSL = GetStructureStopLoss(sym, PERIOD_CURRENT, tradeDirection, 10, 20);
   
   double structureSL = GetStructureStopLoss(sym, PERIOD_CURRENT, tradeDirection, 20);
   if(structureSL == 0) return;
   
   // 2. Calculate the maximum allowed lot size for exactly 1% risk
   double maxAllowedVol = CalculateRiskLotSize(sym, openPrice, structureSL, 1.0);
   if(maxAllowedVol == 0) return;
   
   // 3. LOT SIZE AUTO-CORRECTION (The Magic Slice)
   if(currentVol > maxAllowedVol)
   {
      double excessVolume = currentVol - maxAllowedVol;
      
      // Normalize volume to broker limits
      double volStep = SymbolInfoDouble(sym, SYMBOL_VOLUME_STEP);
      excessVolume = MathFloor(excessVolume / volStep) * volStep;
      
      if(excessVolume >= SymbolInfoDouble(sym, SYMBOL_VOLUME_MIN))
      {
         Print(" SentriXrisk: Manual trade (", currentVol, " lots) exceeds 1% risk. Auto-closing ", excessVolume, " lots.");
         
         MqlTick tick;
         SymbolInfoTick(sym, tick);
         
         MqlTradeRequest reqClose = {};
         MqlTradeResult  resClose = {};
         reqClose.action    = TRADE_ACTION_DEAL;
         reqClose.position  = ticket;
         reqClose.symbol    = sym;
         reqClose.volume    = excessVolume;
         int type = (int)PositionGetInteger(POSITION_TYPE);
         reqClose.type      = (type == POSITION_TYPE_BUY) ? ORDER_TYPE_SELL : ORDER_TYPE_BUY;
         reqClose.price     = (type == POSITION_TYPE_BUY) ? tick.bid : tick.ask;
         
         uint filling = (uint)SymbolInfoInteger(sym, SYMBOL_FILLING_MODE);
         if((filling & SYMBOL_FILLING_FOK) != 0)      reqClose.type_filling = ORDER_FILLING_FOK;
         else if((filling & SYMBOL_FILLING_IOC) != 0) reqClose.type_filling = ORDER_FILLING_IOC;
         else                                         reqClose.type_filling = ORDER_FILLING_RETURN;
         
         OrderSend(reqClose, resClose); // Instantly dump the excess risk!
      }
   }
   
   // 4. APPLY THE STRUCTURE STOP LOSS
   MqlTradeRequest reqMod = {};
   MqlTradeResult  resMod = {};
   reqMod.action   = TRADE_ACTION_SLTP;
   reqMod.position = ticket;
   reqMod.symbol   = sym;
   reqMod.sl       = structureSL; 
   reqMod.tp       = PositionGetDouble(POSITION_TP); // Keep their Take Profit if they set one!
   
   if(OrderSend(reqMod, resMod))
   {
      Print("️ SentriXrisk : Successfully enforced Structure Stop Loss at ", structureSL);
   }
}

