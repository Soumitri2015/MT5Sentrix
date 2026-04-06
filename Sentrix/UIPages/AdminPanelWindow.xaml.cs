using Sentrix.Models;
using Sentrix.Repositories;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
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
using Button = System.Windows.Controls.Button;
using ComboBox = System.Windows.Controls.ComboBox;
using TextBox = System.Windows.Controls.TextBox;

namespace Sentrix.UIPages
{
    /// <summary>
    /// Interaction logic for AdminPanelWindow.xaml
    /// </summary>
   
        public class SessionRow : INotifyPropertyChanged
        {
            private string _sessionName = "";
            private string _startTime = "09:00";
            private string _endTime = "17:00";

            public string SessionName
            {
                get => _sessionName;
                set { _sessionName = value; Notify(nameof(SessionName)); }
            }
            public string StartTime
            {
                get => _startTime;
                set { _startTime = value; Notify(nameof(StartTime)); }
            }
            public string EndTime
            {
                get => _endTime;
                set { _endTime = value; Notify(nameof(EndTime)); }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            private void Notify(string name)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
            }
        }
        public static class TimeSlots
        {
            public static readonly List<string> All = BuildSlots();
            private static List<string> BuildSlots()
            {
                var slots = new List<string>();
                for (int h = 0; h < 24; h++)
                    for (int m = 0; m < 60; m += 15)
                        slots.Add($"{h:D2}:{m:D2}");
                return slots;
            }
        }

        public partial class AdminPanelWindow : Window
        {

            UserRepository _userRepo;
            ConfigRepository _configRepo;
            private List<Sentrix.EntityModel.Users> _allUsers = new();
            private Sentrix.EntityModel. Users _selectedUser;
            private AppConfigData? _loadConfig;

            private bool _supressChangeTracking;

            private bool _suppressChangeTracking;
            private ConfigHelper _configHelper;


            private ObservableCollection<SessionRow> _sessionRows = new ObservableCollection<SessionRow>();
            public List<string> TimeSlotsList => TimeSlots.All;
            public AdminPanelWindow(UserRepository userRepository, ConfigRepository configRepo, ConfigHelper helper)
            {
                InitializeComponent();
                _userRepo = userRepository;
                _configRepo = configRepo;
                _configHelper = helper;


                SessionsGrid.ItemsSource = _sessionRows;
                _sessionRows.CollectionChanged += (_, _) => EvaluateDirty();
                LoadUsers();
            }


            private void LoadUsers()
            {
                _allUsers = _userRepo.GetUsersByRoleId(1);
                ApplyuserFilter(UserSearchBox.Text);
            }

            private void ApplyuserFilter(string filter)
            {
                var filtered = string.IsNullOrWhiteSpace(filter)
                   ? _allUsers
                   : _allUsers.Where(u =>
                       u.UserName.Contains(filter, StringComparison.OrdinalIgnoreCase)).ToList();

                UsersList.ItemsSource = filtered;

                if (_selectedUser != null && filtered.Contains(_selectedUser))
                {
                    UsersList.SelectedItem = _selectedUser;
                }
            }

            private void UserSearchBox_TextChanged(object sender, TextChangedEventArgs e)
            {
                SearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(UserSearchBox.Text) ? Visibility.Visible : Visibility.Collapsed;

                ApplyuserFilter(UserSearchBox.Text);
            }

            private void UsersList_SelectionChanged(object sender, SelectionChangedEventArgs e)
            {
                if (UsersList.SelectedItem is not Sentrix.EntityModel.Users user) return;

                _selectedUser = user;
                LoadConfigForuser(user.Id);
            }

            private void LoadConfigForuser(int userId)
            {
                _supressChangeTracking = true;
                try
                {
                    var config = _configRepo.GetConfigDatabyUserId(userId);
                    _loadConfig = config;
                    SelectedUserText.Text = $"⚙  {_selectedUser?.UserName}";
                    ConfigPanel.IsEnabled = true;

                    
                    MaxTradesDayBox.Text = config.MaxTradesPerDay.ToString();
                    MaxTradesSessionBox.Text = config.MaxTradesPerSession.ToString();
                    LossPercentBox.Text = config.LossPercentValue.ToString("0.##");
                    LockMessageBox.Text = config.LockMessage ?? "";
                    CloseOutsideSessionCheck.IsChecked = config.CloseTradesOutsideSession;

                    //sesion grid.....
                    _sessionRows.Clear();
                    if (config.TradingSessions != null)
                    {
                        foreach (var kvp in config.TradingSessions)
                            foreach (var tw in kvp.Value)
                                _sessionRows.Add(new SessionRow
                                {
                                    SessionName = kvp.Key,
                                    StartTime = tw.StartTime,
                                    EndTime = tw.EndTime
                                });

                        SaveBtn.IsEnabled = false;
                        ClearAllErrors();
                    }
                }
                finally
                {
                    _suppressChangeTracking = false;
                }
            }

            private void Config_TextChanged(object sender, TextChangedEventArgs e)
            {
                ValidateField(sender as TextBox);
                EvaluateDirty();
            }

            private void Config_CheckChanged(object sender, RoutedEventArgs e) =>
                EvaluateDirty();

            private void SessionsGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) =>
                EvaluateDirty();

            private void EvaluateDirty()
            {
                if (_suppressChangeTracking || _loadConfig == null) return;

                bool dirty = HasUnsavedChanges();
                bool valid = !HasValidationErrors();

                SaveBtn.IsEnabled = dirty && valid;
            }

            private bool HasUnsavedChanges()
            {
                if (_loadConfig == null) return false;


                if (MaxTradesDayBox.Text != _loadConfig.MaxTradesPerDay.ToString()) return true;
                if (MaxTradesSessionBox.Text != _loadConfig.MaxTradesPerSession.ToString()) return true;
                if (LossPercentBox.Text != _loadConfig.LossPercentValue.ToString("0.##")) return true;
                if (LockMessageBox.Text != (_loadConfig.LockMessage ?? "")) return true;
                if (CloseOutsideSessionCheck.IsChecked != _loadConfig.CloseTradesOutsideSession) return true;

                // sessions
                var currentSessions = BuildSessionDictionary();
                if (currentSessions == null) return true; // parse error counts as change

                var loaded = _loadConfig.TradingSessions ?? new();
                if (currentSessions.Count != loaded.Count) return true;

                foreach (var kvp in currentSessions)
                {
                    if (!loaded.TryGetValue(kvp.Key, out var loadedWindows)) return true;
                    if (kvp.Value.Count != loadedWindows.Count) return true;

                    for (int i = 0; i < kvp.Value.Count; i++)
                        if (kvp.Value[i].StartTime != loadedWindows[i].StartTime ||
                            kvp.Value[i].EndTime != loadedWindows[i].EndTime)
                            return true;
                }

                return false;
            }


            private void ValidateField(TextBox? box)
            {
                if (box == null) return;

                if (box == MaxTradesDayBox)
                    ToggleError(MaxTradesDayError, !int.TryParse(box.Text, out _));

                else if (box == MaxTradesSessionBox)
                    ToggleError(MaxTradesSessionError, !int.TryParse(box.Text, out _));

                else if (box == LossPercentBox)
                    ToggleError(LossPercentError, !double.TryParse(box.Text, out _));
            }

            private void ValidateSessionTimes()
            {
                bool anyBad = _sessionRows.Any(r =>
                    !TimeSpan.TryParseExact(r.StartTime, @"hh\:mm", null, out _) ||
                    !TimeSpan.TryParseExact(r.EndTime, @"hh\:mm", null, out _));

                SessionTimeError.Visibility = anyBad ? Visibility.Visible : Visibility.Collapsed;
            }
            private bool HasValidationErrors()
            {
                ValidateSessionTimes();
                return MaxTradesDayError.Visibility == Visibility.Visible ||
                       MaxTradesSessionError.Visibility == Visibility.Visible ||
                       LossPercentError.Visibility == Visibility.Visible ||
                       SessionTimeError.Visibility == Visibility.Visible;
            }

            private void ToggleError(TextBlock errorBlock, bool show) =>
                errorBlock.Visibility = show ? Visibility.Visible : Visibility.Collapsed;

            private void ClearAllErrors()
            {
                MaxTradesDayError.Visibility = Visibility.Collapsed;
                MaxTradesSessionError.Visibility = Visibility.Collapsed;
                LossPercentError.Visibility = Visibility.Collapsed;
                SessionTimeError.Visibility = Visibility.Collapsed;
            }

            private void AddSession_Click(object sender, RoutedEventArgs e)
            {
                _sessionRows.Add(new SessionRow
                {
                    SessionName = "New Session",
                    StartTime = "09:00",
                    EndTime = "17:00"
                });

                // Start editing the new row immediately
                SessionsGrid.ScrollIntoView(_sessionRows.Last());
                SessionsGrid.SelectedItem = _sessionRows.Last();
            }

            private void DeleteSession_Click(object sender, RoutedEventArgs e)
            {
                if ((sender as Button)?.Tag is SessionRow row)
                    _sessionRows.Remove(row);
            }



            // Converts the observable rows into the dictionary format AppConfigData expects.
            private Dictionary<string, List<TimeWindow>>? BuildSessionDictionary()
            {
                var dict = new Dictionary<string, List<TimeWindow>>();

                foreach (var row in _sessionRows)
                {
                    if (!TimeSpan.TryParseExact(row.StartTime, @"hh\:mm", null, out _)) return null;
                    if (!TimeSpan.TryParseExact(row.EndTime, @"hh\:mm", null, out _)) return null;

                    if (!dict.TryGetValue(row.SessionName, out var list))
                    {
                        list = new List<TimeWindow>();
                        dict[row.SessionName] = list;
                    }
                    list.Add(new TimeWindow { StartTime = row.StartTime, EndTime = row.EndTime });
                }
                return dict;
            }




            private void Save_Click(object sender, RoutedEventArgs e)
            {
                if (_selectedUser == null) return;


                if (HasValidationErrors())
                {
                    System.Windows.MessageBox.Show("Please fix validation errors before saving.",
                                    "Validation Error", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var sessions = BuildSessionDictionary();
                if (sessions == null)
                {
                    System.Windows.MessageBox.Show("One or more session times are in an invalid format (use HH:mm).",
                                    "Invalid Time", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var config = new AppConfigData
                {
                    UserID = _selectedUser.Id,
                    MaxTradesPerDay = int.Parse(MaxTradesDayBox.Text),
                    MaxTradesPerSession = int.Parse(MaxTradesSessionBox.Text),
                    LossPercentValue = double.Parse(LossPercentBox.Text),
                    LockMessage = LockMessageBox.Text,
                    CloseTradesOutsideSession = CloseOutsideSessionCheck.IsChecked == true,
                    TradingSessions = sessions
                };

                try
                {
                    //_configRepo.SaveConfigByUserId(_selectedUser.Id, config);
                    _configHelper.SaveNewConfig(_selectedUser.Id, config);
                    
                    _loadConfig = config;          // update snapshot → no longer dirty
                    SaveBtn.IsEnabled = false;

                    // Brief visual feedback
                    SaveBtn.Content = "✔  Saved!";
                    var timer = new System.Windows.Threading.DispatcherTimer
                    { Interval = TimeSpan.FromSeconds(2) };
                    timer.Tick += (_, _) =>
                    {
                        SaveBtn.Content = "💾  Save Changes";
                        timer.Stop();
                    };
                    timer.Start();
                }
                catch (Exception ex)
                {
                    System.Windows.MessageBox.Show($"Failed to save: {ex.Message}",
                                    "Save Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }


            //private void TimeCombo_Loaded(object sender, RoutedEventArgs e)
            //{
            //    if (sender is not ComboBox combo) return;

            //    if (combo.Items.Count == 0)
            //        foreach (var slot in TimeSlots.All)
            //            combo.Items.Add(slot);

            //    // If the row's stored value is not an exact 15-min slot, snap to nearest
            //    if (combo.SelectedItem == null && combo.DataContext is SessionRow row)
            //    {
            //        string target = combo.Tag?.ToString() == "end" ? row.EndTime : row.StartTime;
            //        if (combo.Items.Contains(target))
            //        {
            //            combo.SelectedItem = target;
            //        }
            //        else if (TimeSpan.TryParse(target, out var ts))
            //        {
            //            int snapped = ((int)ts.TotalMinutes / 15) * 15;
            //            string nearest = $"{snapped / 60:D2}:{snapped % 60:D2}";
            //            combo.SelectedItem = combo.Items.Contains(nearest) ? nearest : "09:00";
            //        }
            //        else
            //        {
            //            combo.SelectedItem = "09:00";
            //        }
            //    }

            //    EvaluateDirty();
            //}


            //private void TimeCombo_GotFocus(object sender, RoutedEventArgs e)
            //{
            //    if (sender is ComboBox combo)
            //    {
            //        // Only trigger if the dropdown isn't already open.
            //        // This prevents the "infinite loop" that freezes the UI.
            //        if (!combo.IsDropDownOpen)
            //        {
            //            combo.Dispatcher.BeginInvoke(new Action(() => {
            //                combo.IsDropDownOpen = true;
            //            }), System.Windows.Threading.DispatcherPriority.Input);
            //        }
            //    }
            //}


            //private void TimeCombo_GotFocus(object sender, RoutedEventArgs e)
            //{
            //    if (sender is not ComboBox combo)
            //        return;

            //    string input = combo.Text;

            //    if (!TryNormalizeTime(input, out string corrected))
            //    {
            //        corrected = "09:00"; // safe fallback
            //    }

            //    combo.Text = corrected;

            //    if (combo.DataContext is SessionRow row)
            //    {
            //        row.StartTime = corrected;
            //    }

            //    EvaluateDirty();
            //}

            //private bool TryNormalizeTime(string input, out string normalized)
            //{
            //    normalized = null;

            //    if (!TimeSpan.TryParse(input, out var ts))
            //        return false;

            //    // clamp limits
            //    if (ts.TotalHours < 0) ts = TimeSpan.Zero;
            //    if (ts.TotalHours >= 24) ts = new TimeSpan(23, 59, 0);

            //    // snap to nearest 15 minutes
            //    int totalMinutes = (int)Math.Round(ts.TotalMinutes / 15.0) * 15;

            //    int hours = totalMinutes / 60;
            //    int minutes = totalMinutes % 60;

            //    normalized = $"{hours:D2}:{minutes:D2}";
            //    return true;
            //}
        }
    }
