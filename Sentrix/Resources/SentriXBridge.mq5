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

bool g_SessionActive = false;
int g_MaxTradesDaily = 3;
int g_CurrentDailyTrades = 0;
double g_MaxLossPercent = 2.0;
bool g_Manage1R = true;
ulong g_managedTickets[];

string g_eventQueue[];


//+------------------------------------------------------------------+
int OnInit()
{
   Print("SentriXBridge: starting...");
   EventSetMillisecondTimer(INTERVAL_MS);
   
   RecoverManagedTickets();
   
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


void PushDataToCSharp()
{
   if(g_pipe == INVALID_HANDLE) return;

   string json = BuildJSON();
   int len = StringLen(json);

   if(FileWriteInteger(g_pipe, len, INT_VALUE) < 0 ||
      FileWriteString(g_pipe, json, len)       < 0)
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
}

//+------------------------------------------------------------------+
void OnTimer()
{
   // ── 1. Connect data pipe (EA → C#) ───────────────────────────
   // 1. Connect data pipe (EA → C#)
   if(g_pipe == INVALID_HANDLE)
   {
      g_pipe = FileOpen(PIPE_NAME, FILE_READ | FILE_WRITE | FILE_BIN | FILE_ANSI, 0, CP_UTF8);
      if(g_pipe == INVALID_HANDLE) return;
   }

   // 2. Connect command pipe (C# → EA)
   if(g_cmdPipe == INVALID_HANDLE)
   {
      g_cmdPipe = FileOpen(PIPE_CMD_NAME, FILE_READ | FILE_WRITE | FILE_BIN | FILE_ANSI, 0, CP_UTF8);
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
   
      g_SessionActive = (StringFind(cmd,"\"SessionActive\":true") >=0);
      g_Manage1R = (StringFind(cmd,"\"Manage1R\":true") >= 0);
      
      g_MaxTradesDaily     = (int)ExtractNumber(cmd, "\"MaxTradesDaily\":");
      g_CurrentDailyTrades = (int)ExtractNumber(cmd, "\"CurrentDailyTrades\":");
      g_MaxLossPercent     = ExtractNumber(cmd, "\"MaxLossPercent\":");
      Print("️ Sentrix Rules Updated | Active: ", g_SessionActive, " | Trades: ", g_CurrentDailyTrades, "/", g_MaxTradesDaily);
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
               bool violateSession = !g_SessionActive;
               bool violateTrades  = (g_CurrentDailyTrades >= g_MaxTradesDaily);

               if(violateSession || violateTrades)
               {
                  LogEvent("🚨 SENTRIX BLOCK: Unauthorized trade instantly closed (Ticket: " + (string)trans.position + ")");
                  
                  // 2. RACE CONDITION FIX: Wait up to 200ms for MT5 to cache the position
                  for(int i = 0; i < 20; i++) 
                  {
                     if(PositionSelectByTicket(trans.position)) break;
                     Sleep(10); 
                  }
                  
                  // 3. Close it instantly
                  ClosePositionByTicket(trans.position);
                  PushDataToCSharp(); 
               }
               else
               {
                  Print("✅ Sentrix Tracking: New valid trade successfully opened. Ticket: ", trans.position);
                  PushDataToCSharp(); 
               }
            }
            else if(entryType == DEAL_ENTRY_OUT)
            {
               Print("🔒 Sentrix Tracking: Trade successfully closed. Ticket: ", trans.position);
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
   Print("Sentrix Event---"+ message);
   
   int size = ArraySize(g_eventQueue);
   
   ArrayResize(g_eventQueue, size+1);
   g_eventQueue[size]= message;
}

//+------------------------------------------------------------------+
