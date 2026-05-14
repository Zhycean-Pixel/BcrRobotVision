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

            // 兜底退出：部分相机SDK/非托管库可能留下后台线程，
            // 即使WPF窗口关闭也会让BcrRobotVision.exe留在任务管理器中。
            // 前面MainWindow.OnClosing已经做了正常资源释放，这里只负责确保进程最终结束。
            Environment.Exit(0);
        }
    }
}
