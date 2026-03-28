using Sentrix.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics.Tracing;
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

namespace Sentrix.UIPages
{
    /// <summary>
    /// Interaction logic for EventLogs.xaml
    /// </summary>
    public partial class EventLogs : Window
    {
        public EventLogs(List<EventLog> events)
        {
            InitializeComponent();
            EventsList.ItemsSource = events;
        }
    }
}
