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
int g_UtcOffsetMinutes = 0;
int g_LocalHour   = 0;
int g_LocalMinute = 0;
bool g_OffsetCalibrated = false;

int g_TradesLondon = 0;
int g_TradesNewYork = 0;


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

   Print("SentriXBridge: received command — ", cmd);
   
   
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
      //g_UtcOffsetMinutes = (int)(ExtractNumber(cmd,"\"UTCTimeOffsetHours\":") * 60);
      //int localHour   = (int)ExtractNumber(cmd, "\"LocalTimeHour\":");
      //int localMinute = (int)ExtractNumber(cmd, "\"LocalTimeMinute\":");
      
      Print("️ Sentrix Rules Updated | Active: ", g_SessionActive, " | Trades: ", g_CurrentDailyTrades, "/", g_MaxTradesDaily);
      
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
               // 1. Evaluate the rules FIRST (before trusting MT5's position cache)
               int activeSessionTradeCount = 0;
               string activeSession = GetActiveSession();
               if(activeSession == "London")       activeSessionTradeCount = g_TradesLondon;
               else if(activeSession == "NewYork") activeSessionTradeCount = g_TradesNewYork;
               printf(g_SessionActive);
            
               // 2. Evaluate limits
               bool violateSession       = !g_SessionActive || !IsCurrentTimeAllowedWindow(); 
               bool violateDailyTrades   = (g_MaxTradesDaily > 0 && g_CurrentDailyTrades >= g_MaxTradesDaily);
               bool violateSessionTrades = (g_MaxTradeSession > 0 && activeSessionTradeCount >= g_MaxTradeSession);
               if(violateSession || violateDailyTrades || violateSessionTrades)
               {
                  string reason = violateSession ? "Time Blocked" : (violateDailyTrades ? "Daily Limit" : "Session Limit");
                  string blockMsg = "🚨 MetaTrader BLOCK: Unauthorized trade Reason: " + reason + " (Ticket: " + (string)trans.position + ")";
                  
                  printf(blockMsg);
                  LogEvent(blockMsg);
                  //TriggerMT5Alert("🚨 SENTRIX BLOCK: Unauthorized trade  (Ticket: " + (string)trans.position + ")");
                  TriggerMT5Alert(blockMsg);
                  
                  // 2. RACE CONDITION FIX: Wait up to 200ms for MT5 to cache the position
                  for(int i = 0; i < 20; i++) 
                  {
                     if(PositionSelectByTicket(trans.position)) break;
                     Sleep(10); 
                  }
                  
                  printf("Close" + trans.position);
                  // 3. Close it instantly
                  ClosePositionByTicket(trans.position);
                  PushDataToCSharp(); 
               }
               else
               {
                  Print("✅ Sentrix Tracking: New valid trade successfully opened. Ticket: ", trans.position);
                 // TriggerMT5Alert(" Sentrix Tracking: New valid trade successfully opened. Ticket:---"+  trans.position);
                  
                  g_CurrentDailyTrades++;
                  if(activeSession == "London")       g_TradesLondon++;
                  else if(activeSession == "NewYork") g_TradesNewYork++;
                  SaveStateOffline();
                  PushDataToCSharp(); 
               }
            }
            else if(entryType == DEAL_ENTRY_OUT)
            {
               Print("🔒 Sentrix Tracking: Trade successfully closed. Ticket: ", trans.position);
      
               // We keep this so the C# UI updates instantly when a trade closes!
               PushDataToCSharp();
                  //Print("🔒 Sentrix Tracking: Trade successfully closed. Ticket: ", trans.position);
               //TriggerMT5Alert(" Sentrix Tracking: Trade successfully closed. Ticket:-- "+ trans.position);
               
               //SaveStateOffline();
               //PushDataToCSharp();
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


void SaveConfigOffline(){

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
           // g_UtcOffsetMinutes = (int)StringToInteger(parts[8]);
            //g_OffsetCalibrated = (bool)StringToInteger(parts[9]);
           // if(g_OffsetCalibrated)
                 // Print("SentriX: Restored calibrated offset from disk: ", 
                            //  g_UtcOffsetMinutes, " min. C# not needed for time.");
          // if(ArraySize(parts) >= 10)
              // g_UtcOffsetMinutes = (int)StringToInteger(parts[8]); 
            
            Print(" SentriX: Loaded offline rules from local disk. Sessions: ", g_AllowedSessions);
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
   datetime etTime = TimeEastern();
   printf(etTime);
   MqlDateTime etStruct;
   TimeToStruct(etTime, etStruct);
   
   // Convert current ET time into total minutes for easy comparison
   int currentMinutesET = (etStruct.hour * 60) + etStruct.min;
   printf(currentMinutesET);
   
   // Rule 1: London Session (2:00 AM to 7:59 AM ET)
   // 2:00 = 120 mins | 7:59 = 479 mins
   if(currentMinutesET >= 120 && currentMinutesET < 480)
   {
      g_SessionActive = true;
      printf("Current session name London");
      return "London";
      
   }
   
   // Rule 2: New York Session (8:00 AM to 11:30 AM ET)
   // 8:00 = 480 mins | 11:30 = 690 mins
   if(currentMinutesET >= 480 && currentMinutesET <= 690)
   {
      g_SessionActive= true;
      printf("Current Session Name NewYork");
      return "NewYork"; // Matches your C# exact string
      
   }
   
   // Outside allowed hours
   printf("outiside allowed hour");
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

int GetLocalTimeMinutes()
{
   if(!g_OffsetCalibrated)
   {
      Print("SentriX WARNING: UTC offset not yet calibrated. Using broker time as fallback.");
   }
   
   MqlDateTime t;
   TimeToStruct(TimeCurrent(), t);
   int brokerMinutes = t.hour * 60 + t.min;
   
   int localMinutes = brokerMinutes + g_UtcOffsetMinutes;
   
   if(localMinutes >= 1440) localMinutes -= 1440;
   if(localMinutes < 0)     localMinutes += 1440;
   
   return localMinutes;
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
   // If no sessions are configured, block trading
   if(StringLen(g_AllowedSessions) == 0 || g_AllowedSessions == "None") 
      return false; 
      
   int currentMin = GetLocalTimeMinutes();
   string sessions[];
   
   // Split by the pipe character (e.g., london:11:30-12:55 | newyork:15:56-14:30)
   int count = StringSplit(g_AllowedSessions, '|', sessions);
   
   for(int i = 0; i < count; i++)
   {
      int firstColon = StringFind(sessions[i], ":");
      if(firstColon < 0) continue;
      
      string times = StringSubstr(sessions[i], firstColon + 1); // Extract "11:30-12:55"
      int dashPos = StringFind(times, "-");
      if(dashPos < 0) continue;
      
      string startStr = StringSubstr(times, 0, dashPos); // Extract "11:30"
      string endStr   = StringSubstr(times, dashPos + 1); // Extract "12:55"
      
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
         if(currentMin >= startTotal && currentMin <= endTotal) return true;
      }
      else
      {
         // Midnight cross session (starts before midnight, ends the next day)
         if(currentMin >= startTotal || currentMin <= endTotal) return true;
      }
   }
   
   // If the loop finishes and no window matched the current time, block it
   return false; 
}
//+--------------------------------------------------------------

