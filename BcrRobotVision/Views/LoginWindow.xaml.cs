using System.Windows;
using System.Windows.Input;

namespace BcrRobotVision.Views
{
    public partial class LoginWindow : Window
    {
        private const string DefaultUserName = "admin";
        private const string DefaultPassword = "admin";

        public LoginWindow()
        {
            InitializeComponent();

            txtUserName.Text = "admin";
            txtPassword.Password = "admin";
        }

        private void BtnLogin_Click(object sender, RoutedEventArgs e)
        {
            string userName = txtUserName.Text.Trim();
            string password = txtPassword.Password.Trim();

            if (userName == DefaultUserName && password == DefaultPassword)
            {
                var mainWindow = new MainWindow();
                Application.Current.MainWindow = mainWindow;
                mainWindow.Show();
                Close();
            }
            else
            {
                MessageBox.Show("账号或密码错误", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void BtnExit_Click(object sender, RoutedEventArgs e)
        {
            Application.Current.Shutdown();
        }

        private void BtnMin_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            DragMove();
        }
    }
}