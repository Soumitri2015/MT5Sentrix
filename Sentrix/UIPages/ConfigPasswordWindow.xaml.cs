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

namespace Sentrix.UIPages
{
    /// <summary>
    /// Interaction logic for ConfigPasswordWindow.xaml
    /// </summary>
    public partial class ConfigPasswordWindow : Window
    {
        public bool IsValid { get; private set; }
        public ConfigPasswordWindow()
        {
            InitializeComponent();
        }

        private void Unlock_Click(object sender, RoutedEventArgs e)
        {
            if (PwdBox.Password == "sentrix12")
            {
                IsValid = true;
                Close();
            }
            else
            {
                System.Windows.MessageBox.Show("Invalid password. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }// Replace
        }
    }
}
