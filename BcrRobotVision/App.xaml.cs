using System;
using System.Windows;
using BcrRobotVision.Views;

namespace BcrRobotVision
{
    public partial class App : Application
    {
        protected override void OnStartup(StartupEventArgs e)
        {
            try
            {
                base.OnStartup(e);

                var login = new LoginWindow();
                Current.MainWindow = login;
                login.Show();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "程序启动异常");
                Shutdown();
            }
        }

        protected override void OnExit(ExitEventArgs e)
        {
            base.OnExit(e);
            Environment.Exit(0);
        }
    }
}
