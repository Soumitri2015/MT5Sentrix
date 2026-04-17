#property strict

#define SERVICE_CHART_NAME "SENTRIX_BRIDGE_SERVICE"
#define TEMPLATE_NAME "\\Profiles\\Templates\\SentriXBridge.tpl"

long FindServiceChart()
{
   for(long chart=ChartFirst(); chart>=0; chart=ChartNext(chart))
      if(ChartIndicatorsTotal(chart, 0) > 0)
         return chart;
         
         //if(ChartGetString(chart, CHART_COMMENT) == SERVICE_CHART_NAME)
   return -1;
}

long CreateServiceChart()
{
   Print("Creating SentriX service chart...");

   long originalChart = ChartID();
   
   long firstChart=ChartFirst();
   
   if(firstChart > 0)
      return firstChart;

   // 2. Open the new chart (MT5 will force this to the front)
   long chart = ChartOpen("EURUSD", PERIOD_M1);
   if(chart == 0)
   {
      Print("Failed to open chart");
      return -1;
   }

   Sleep(2000); // wait until MT5 fully creates chart

   for(long chart=ChartFirst(); chart>=0; chart=ChartNext(chart))
      // 3. Force the original chart back to the front so the user isn't interrupted
      ChartSetInteger(chart, CHART_BRING_TO_TOP, true);
      
   ChartSetInteger(originalChart, CHART_BRING_TO_TOP, true);

   Print("Service chart ready");
   return chart;
}

void AttachBridgeToChart(long chart)
{
   Print("Applying template to service chart...");

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
      chart = CreateServiceChart();

   if(chart != -1)
      AttachBridgeToChart(chart);
}

void EnsureBridgeAlive()
{
   long chart = FindServiceChart();

   if(chart == -1)
   {
      Print("Service chart missing → restoring");
      DeployBridge();
      return;
   }

   if(ChartIndicatorsTotal(chart, 0) == 0)
   {
      Print("Bridge missing → redeploying");
      AttachBridgeToChart(chart);
   }
}

int OnInit()
{
   Print("SentriX Installer started");

   Sleep(5000); // MT5 startup delay
   DeployBridge();
   EventSetTimer(5);

   return(INIT_SUCCEEDED);
}

void OnTimer()
{
   EnsureBridgeAlive();
}

void OnDeinit(const int reason)
{
   EventKillTimer();
}

void NormalizeChart(long chart)
{
   //ChartSetInteger(chart, CHART_SHOW_CANDLES, true);
   ChartSetInteger(chart, CHART_SHOW_GRID, true);
   ChartSetInteger(chart, CHART_AUTOSCROLL, true);
   ChartSetInteger(chart, CHART_SHIFT, true);

   ChartNavigate(chart, CHART_END);
   ChartRedraw(chart);
}