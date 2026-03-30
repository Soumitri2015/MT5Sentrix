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

int g_pipe    = INVALID_HANDLE;   // data pipe  — EA writes, C# reads
int g_cmdPipe = INVALID_HANDLE;   // command pipe — C# writes, EA reads

//+------------------------------------------------------------------+
int OnInit()
{
   Print("SentriXBridge: starting...");
   EventSetMillisecondTimer(INTERVAL_MS);
   return INIT_SUCCEEDED;
}

//+------------------------------------------------------------------+
void OnDeinit(const int reason)
{
   EventKillTimer();
   if(g_pipe    != INVALID_HANDLE) { FileClose(g_pipe);    g_pipe    = INVALID_HANDLE; }
   if(g_cmdPipe != INVALID_HANDLE) { FileClose(g_cmdPipe); g_cmdPipe = INVALID_HANDLE; }
   Print("SentriXBridge: stopped.");
}

//+------------------------------------------------------------------+
void OnTimer()
{
   // ── 1. Connect data pipe (EA → C#) ───────────────────────────
   if(g_pipe == INVALID_HANDLE)
   {
      g_pipe = FileOpen(PIPE_NAME, FILE_READ | FILE_WRITE | FILE_BIN | FILE_ANSI, 0, CP_UTF8);
      if(g_pipe == INVALID_HANDLE)
      {
         Print("SentriXBridge: data pipe not available, retrying...");
         return;
      }
      Print("SentriXBridge: data pipe connected.");
   }

   // ── 2. Connect command pipe (C# → EA) ────────────────────────
   if(g_cmdPipe == INVALID_HANDLE)
   {
      Print("SentriXBridge: attempting cmd pipe connection...");
      g_cmdPipe = FileOpen(PIPE_CMD_NAME, FILE_READ | FILE_WRITE | FILE_BIN | FILE_ANSI, 0, CP_UTF8);
      
      if(g_cmdPipe != INVALID_HANDLE)
         Print("SentriXBridge: cmd pipe connected.");
      else
         Print("SentriXBridge: cmd pipe not available yet - error=", GetLastError());
   }

   // ── 3. Read any incoming close commands from Sentrix ─────────
   if(g_cmdPipe != INVALID_HANDLE)
      ReadIncomingCommand();

   // ── 4. Build and send position data to Sentrix ───────────────
   string json = BuildJSON();
   int len = StringLen(json);

   // Write data to the pipe
   if(FileWriteInteger(g_pipe, len, INT_VALUE) < 0 ||
      FileWriteString(g_pipe, json, len)       < 0)
   {
      Print("SentriXBridge: data pipe write failed, resetting.");
      
      // Close data pipe
      FileClose(g_pipe);
      g_pipe = INVALID_HANDLE;
      
      // CRITICAL FIX: Only close command pipe if the data pipe fails (meaning C# disconnected)
      if(g_cmdPipe != INVALID_HANDLE) 
      {
         FileClose(g_cmdPipe);
         g_cmdPipe = INVALID_HANDLE;
      }
   }
}

void OnTick() { }

//+------------------------------------------------------------------+
void ReadIncomingCommand()
{
   if(g_cmdPipe == INVALID_HANDLE) return;

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
   
   // Set the filling mode. IOC (Immediate or Cancel) is standard for most modern brokers
   req.type_filling = ORDER_FILLING_IOC; 

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

   return StringFormat(
      "{\"Login\":%I64d,\"Balance\":%.2f,\"Equity\":%.2f,"
      "\"Currency\":\"%s\",\"ServerTime\":\"%s\","
      "\"Positions\":[%s]}",
      login, balance, equity, currency, serverTime, posArray);
}
//+------------------------------------------------------------------+