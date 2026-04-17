// 1. THIS KEYWORD CHANGES IT FROM AN EA TO A BACKGROUND SERVICE
#property service 

#property copyright "SentriX Guardian"
#property version   "1.03"

#define SERVICE_CHART_NAME "SENTRIX_BRIDGE_SERVICE"
#define TEMPLATE_NAME "\\Profiles\\Templates\\SentriXBridge.tpl"

long FindServiceChart()
{
   for(long chart=ChartFirst(); chart>=0; chart=ChartNext(chart))
      if(ChartGetString(chart, CHART_COMMENT) == SERVICE_CHART_NAME)
         return chart;
         
   return -1;
}

long CreateServiceChart()
{
   Print("SentriX Service: Creating hidden bridge chart...");

   long originalChart = ChartID(); // Might return 0 in a service, which is fine
   long chart = ChartOpen("EURUSD", PERIOD_M1);
   
   if(chart == 0) return -1;

   Sleep(2000); 

   // Attempt to push original chart back to front
   if(originalChart > 0)
      ChartSetInteger(originalChart, CHART_BRING_TO_TOP, true);
      
   return chart;
}

void AttachBridgeToChart(long chart)
{
   ResetLastError();
   bool ok = ChartApplyTemplate(chart, TEMPLATE_NAME);

   if(!ok)
      Print("SentriX Service: Template failed. Error=", GetLastError());
   else
      Print("SentriX Service: Template applied successfully.");
}

void DeployBridge()
{
   long chart = FindServiceChart();
   if(chart == -1) chart = CreateServiceChart();
   if(chart != -1) AttachBridgeToChart(chart);
}

void EnsureBridgeAlive()
{
   long chart = FindServiceChart();
   if(chart == -1)
   {
      Print("SentriX Service: Bridge missing → restoring silently");
      DeployBridge();
   }
}

// 2. SERVICES USE OnStart() WITH AN INFINITE LOOP
void OnStart()
{
   Print("🛡️ SentriX Background Watchdog Started.");

   // Delay slightly on startup to let MT5 load UI elements
   Sleep(5000); 

   // Infinite loop that runs until MT5 is closed
   while(!IsStopped())
   {
      EnsureBridgeAlive();
      
      // Watchdog checks every 1 second
      Sleep(1000); 
   }
   
   Print("SentriX Background Watchdog Stopped.");
}