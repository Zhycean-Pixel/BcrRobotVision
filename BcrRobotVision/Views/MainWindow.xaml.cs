using BcrRobotVision.Pages;
using System.IO.Packaging;
using System.Windows;
using System.Windows.Controls;
using BcrRobotVision.Models;




namespace BcrRobotVision.Views
{
    public partial class MainWindow : Window
    {
        private readonly CameraPage _cameraPage;
        private readonly PlcPage _plcPage;
        private readonly ReportPage _reportPage;
        private readonly SpcReportPage _spcReportPage;
        private bool _isModeChanging = false;
        private int _lastModeIndex = 0;
        public MainWindow()
        {
            InitializeComponent();

            _cameraPage = new CameraPage();
            _plcPage = new PlcPage();
            _reportPage = new ReportPage();
            _spcReportPage = new SpcReportPage();

            MainContent.Content = _cameraPage;
        }

        private void BtnPLC_Click(object sender, RoutedEventArgs e)
        {
            MainContent.Content = _plcPage;
        }

        private void BtnCameraPage_Click(object sender, RoutedEventArgs e)
        {
            MainContent.Content = _cameraPage;
        }

        private void BtnPlcPage_Click(object sender, RoutedEventArgs e)
        {
            MainContent.Content = _plcPage;
        }

        private void BtnReportPage_Click(object sender, RoutedEventArgs e)
        {
            MainContent.Content = _reportPage;
        }


        private void CmbRunMode_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (_isModeChanging)
                return;

            if (cmbRunMode == null)
                return;

            int newIndex = cmbRunMode.SelectedIndex;

            if (newIndex == _lastModeIndex)
                return;

            string newModeName = newIndex == 0 ? "全部正面" : "全部反面";

            MessageBoxResult result = MessageBox.Show(
                $"确定要切换到【{newModeName}】模式吗？\n\n切换模式可能会影响PLC写入结果和报表统计。",
                "确认切换模式",
                MessageBoxButton.YesNo,
                MessageBoxImage.Question);

            if (result != MessageBoxResult.Yes)
            {
                _isModeChanging = true;
                cmbRunMode.SelectedIndex = _lastModeIndex;
                _isModeChanging = false;
                return;
            }

            _lastModeIndex = newIndex;

            AppSession.CurrentMode = newIndex == 0
                ? RunMode.全部正面
                : RunMode.全部反面;
        }

        private void BtnSpcPage_Click(object sender, RoutedEventArgs e)
        {
            MainContent.Content = _spcReportPage;
        }   
        protected override void OnClosed(System.EventArgs e)
        {
            base.OnClosed(e);
            Application.Current.Shutdown();
        }
    }
}