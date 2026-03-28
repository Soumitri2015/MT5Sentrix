using Microsoft.Extensions.DependencyInjection;
using Sentrix.Models;
using Sentrix.Repositories;
using System;
using System.Collections.Generic;
using System.Diagnostics;
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
    /// Interaction logic for LoginSignup.xaml
    /// </summary>
    public partial class LoginSignup : Window
    {
        private UserRepository _userRepository;


        public LoginSignup(UserRepository userRepository)
        {
            InitializeComponent();
            _userRepository = userRepository;
        }

        private void ShowSignUp_Click(object sender, RoutedEventArgs e)
        {
            LoginForm.Visibility = Visibility.Collapsed;
            SignUpForm.Visibility = Visibility.Visible;
        }

        private void ShowLogin_Click(object sender, RoutedEventArgs e)
        {
            SignUpForm.Visibility = Visibility.Collapsed;
            LoginForm.Visibility = Visibility.Visible;
        }

        private void LoginButton_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(LoginEmail.Text))
                {
                    MessageBox.Show("Please enter your email.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (string.IsNullOrEmpty(LoginPassword.Password))
                {
                    MessageBox.Show("Please enter your password.", "Validation Error",
                        MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var userId = _userRepository.GetUser(LoginEmail.Text, LoginPassword.Password);
                if (userId == -1)
                {
                    MessageBox.Show("Invalid email or password.", "Authentication Failed",
                        MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }


                MessageBox.Show($"Login attempted for: {LoginEmail.Text}", "Login",
                    MessageBoxButton.OK, MessageBoxImage.Information);

                UserSession.SetUser(userId);
                UserSession.SetUserRole(_userRepository.GetUserRoleById(userId));
                var scope = ((App)System.Windows.Application.Current).ServiceProvider.GetRequiredService<MainWindow>();
                System.Windows.Application.Current.MainWindow = scope;
                scope.Show();
                this.Close();

            }
            catch (Exception)
            {

                throw;
            }
            
        }

        private void SignUpButton_Click(object sender, RoutedEventArgs e)
        {
           


            // Validate inputs
            if (string.IsNullOrWhiteSpace(SignUpName.Text))
            {
                MessageBox.Show("Please enter your full name.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);

                return;
            }

            if (string.IsNullOrWhiteSpace(SignUpEmail.Text))
            {
                MessageBox.Show("Please enter your email.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);

                return;
            }

            if (string.IsNullOrEmpty(SignUpPassword.Password))
            {
                MessageBox.Show("Please enter a password.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);

                return;
            }

            if (SignUpPassword.Password != SignUpConfirmPassword.Password)
            {
                MessageBox.Show("Passwords do not match.", "Validation Error",
                    MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            try
            {
                _userRepository.AddUser(new EntityModel.Users
                {
                    UserName = SignUpName.Text,
                    Email = SignUpEmail.Text,
                    Password = SignUpPassword.Password,
                    IsActive = true
                });
            }
            catch (Exception ex)
            {

                Debug.WriteLine($"Error adding user: {ex.Message}");
            }


            MessageBox.Show($"Account created for: {SignUpName.Text}", "Success",
                MessageBoxButton.OK, MessageBoxImage.Information);


            ShowLogin_Click(sender, e);

            // Clear signup form
            SignUpName.Clear();
            SignUpEmail.Clear();
            SignUpPassword.Clear();
            SignUpConfirmPassword.Clear();
        }
    }
}
