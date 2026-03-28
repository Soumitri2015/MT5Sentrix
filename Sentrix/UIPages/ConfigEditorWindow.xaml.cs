using Sentrix.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using MessageBox = System.Windows.MessageBox;

namespace Sentrix.UIPages
{
    /// <summary>
    /// Interaction logic for ConfigEditorWindow.xaml
    /// </summary>
    public partial class ConfigEditorWindow : Window
    {
        private readonly ConfigHelper _configHelper;
        private AppConfigData config;

        public ConfigEditorWindow(ConfigHelper helper)
        {


            InitializeComponent();
            _configHelper = helper;
            config = _configHelper.Load();
            PopulateTimePickers();
            LoadSessions();
            try
            {
                MaxTradesBox.Text = config.MaxTradesPerDay.ToString();
                //IntervalBox.Text = config.ExtractionIntervalMs.ToString();
                LockMessageBox.Text = config.LockMessage;
                MaxTradesPerSessionBox.Text = config.MaxTradesPerSession.ToString();
                CloseTradesCheck.IsChecked = config.CloseTradesOutsideSession;
                MaxLossBox.Text = config.LossPercentValue.ToString();
            }
            catch (Exception)
            {

                throw;
            }



        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                config.MaxTradesPerDay = int.Parse(MaxTradesBox.Text);
                //config.ExtractionIntervalMs = int.Parse(IntervalBox.Text);
                config.LockMessage = LockMessageBox.Text;
                config.MaxTradesPerSession = int.Parse(MaxTradesPerSessionBox.Text);
                config.CloseTradesOutsideSession = CloseTradesCheck.IsChecked ?? false;
                config.LossPercentValue = double.Parse(MaxLossBox.Text);

                _configHelper.Save(config);
                System.Windows.MessageBox.Show("Configuration saved successfully. Restart the app to apply changes.",
                                "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                Close();
            }
            catch (Exception ex)
            {

                throw;
            }

        }

        private void SessionsList_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            try
            {
                if (SessionsList.SelectedItem == null)
                {
                    AddSessionBtn.Content = "Add Session";
                    SessionNameBox.IsEnabled = true;
                    return;
                }

                string text = SessionsList.SelectedItem.ToString();
                var parts = text.Split('-');

                if (parts.Length < 3)
                {
                    return;
                }

                string sessionName = parts[0].Trim();

                string startTime = parts[1].Trim();
                string endTime = parts[2].Trim();

                SessionNameBox.Text = sessionName;
                SessionNameBox.IsEnabled = false;

                StartTimePicker.SelectedItem = startTime;
                EndTimePicker.SelectedItem = endTime;
                AddSessionBtn.Content = "Update Session";
            }
            catch (Exception)
            {

                throw;
            }


        }




        private void AddSession_Click(object sender, RoutedEventArgs e)
        {
            if (config == null)
                config = _configHelper.Load();

            string name = SessionNameBox.Text?.Trim();

            bool isUpdate = SessionsList.SelectedItem != null;
            if (string.IsNullOrEmpty(name) && !isUpdate)
            {
                MessageBox.Show("Session name required");
                return;
            }

            string start = StartTimePicker.SelectedItem as string;
            string end = EndTimePicker.SelectedItem as string;

            if (start == null || end == null)
            {
                MessageBox.Show("Select start and end time");
                return;
            }

            if (config.TradingSessions == null)
                config.TradingSessions = new Dictionary<string, List<TimeWindow>>();


            if (isUpdate)
            {
                string item = SessionsList.SelectedItem.ToString();

                name = item.Split('-')[0].Trim();


                if (!config.TradingSessions.ContainsKey(name))
                {
                    MessageBox.Show("Session not found for update");
                    return;
                }

                config.TradingSessions[name][0].StartTime = start;
                config.TradingSessions[name][0].EndTime = end;
            }
            else
            {

                if (config.TradingSessions.ContainsKey(name))
                {
                    MessageBox.Show("Session already exists");
                    return;
                }

                config.TradingSessions[name] = new List<TimeWindow>
                {
                    new TimeWindow
                    {
                        StartTime = start,
                        EndTime = end
                    }
                };
            }

            _configHelper.Save(config);
            LoadSessions();


            SessionNameBox.Clear();
            SessionNameBox.IsEnabled = true;
            SessionsList.SelectedItem = null;
            AddSessionBtn.Content = "Add Session";

        }


        private void PopulateTimePickers()
        {
            for (int h = 0; h < 24; h++)
            {
                for (int m = 0; m < 60; m += 15)
                {
                    string time = $"{h:D2}:{m:D2}";
                    StartTimePicker.Items.Add(time);
                    EndTimePicker.Items.Add(time);
                }
            }

            StartTimePicker.SelectedIndex = 0;
            EndTimePicker.SelectedIndex = 0;
        }

        private void LoadSessions()
        {
            SessionsList.Items.Clear();

            if (config.TradingSessions == null)
                return;

            foreach (var kv in config.TradingSessions)
            {
                var window = kv.Value.FirstOrDefault();
                if (window == null) continue;

                SessionsList.Items.Add(
                    $"{kv.Key} - {window.StartTime} - {window.EndTime}");
            }
        }
    }
}
