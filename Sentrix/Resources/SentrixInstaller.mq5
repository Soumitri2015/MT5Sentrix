// 1. THIS MUST BE A SERVICE TO RUN FOREVER IN THE BACKGROUND
#property service 
#property strict

#define SERVICE_CHART_NAME "SENTRIX_BRIDGE_SERVICE"
#define TEMPLATE_NAME "\\Profiles\\Templates\\SentriXBridge.tpl"

long FindServiceChart()
{
   // 2. MUST CHECK COMMENT, NOT INDICATORS
   for(long chart=ChartFirst(); chart>=0; chart=ChartNext(chart))
   {
      if(ChartGetString(chart, CHART_COMMENT) == SERVICE_CHART_NAME)
         return chart;
   }
   return -1;
}

void NormalizeChart(long chart)
{
   ChartSetInteger(chart, CHART_SHOW_GRID, true);
   ChartSetInteger(chart, CHART_AUTOSCROLL, true);
   ChartSetInteger(chart, CHART_SHIFT, true);

   ChartNavigate(chart, CHART_END);
   ChartRedraw(chart);
}

void AttachBridgeToChart(long chart)
{
   Print("SentriX Service: Applying template to hijacked chart...");

   ResetLastError();
   bool ok = ChartApplyTemplate(chart, TEMPLATE_NAME);
   
   NormalizeChart(chart);

   if(!ok)
      Print("Template failed. Error=", GetLastError());
   else
      Print("Template applied successfully. Waiting for Bridge to claim chart...");
}

void DeployBridge()
{
   long chart = FindServiceChart();
   
   if(chart == -1)
   {
      // Find the very first chart the user has open
      long firstChart = ChartFirst();
      
      // ONLY attach if they actually have a chart open!
      if(firstChart >= 0)
      {
         AttachBridgeToChart(firstChart);
      }
      // If no charts are open, do absolutely nothing.
   }
}

void EnsureBridgeAlive()
{
   long chart = FindServiceChart();
   
   if(chart == -1)
   {
      // No log spam here! Just silently try to deploy.
      DeployBridge();
   }
}

// 3. SERVICES USE OnStart(), NOT OnInit()
void OnStart()
{
   Print("🛡️ SentriX Predator Watchdog Started (Background Service).");

   Sleep(5000); // MT5 startup delay

   // Infinite loop that runs until MT5 is closed
   while(!IsStopped())
   {
      EnsureBridgeAlive();
      
      // Check every 2 seconds
      Sleep(2000); 
   }
   
   Print("SentriX Watchdog Stopped.");
}