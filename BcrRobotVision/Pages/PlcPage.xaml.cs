using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using BcrRobotVision.Services;

namespace BcrRobotVision.Pages
{
    public partial class PlcPage : UserControl
    {
        private readonly PlcService _plcService = new PlcService();

        public PlcPage()
        {
            InitializeComponent();
        }

        private void BtnConnectPlc_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string ip = txtPlcIp.Text.Trim();
                int port = int.Parse(txtPlcPort.Text.Trim());

                bool ok = _plcService.Connect(ip, port);

                txtPlcState.Text = ok ? "PLC：已连接" : "PLC：未连接";
                txtPlcState.Foreground = ok ? Brushes.LimeGreen : Brushes.Red;

                AppendLog(ok ? $"PLC连接成功：{ip}:{port}" : "PLC连接失败");
            }
            catch (Exception ex)
            {
                txtPlcState.Text = "PLC：连接失败";
                txtPlcState.Foreground = Brushes.Red;
                AppendLog($"PLC连接失败：{ex.Message}");
                MessageBox.Show($"PLC连接失败：{ex.Message}");
            }
        }

        private void BtnDisconnectPlc_Click(object sender, RoutedEventArgs e)
        {
            _plcService.Disconnect();

            txtPlcState.Text = "PLC：未连接";
            txtPlcState.Foreground = Brushes.Red;

            AppendLog("PLC已断开");
        }

        private void BtnWriteCam1Ok_Click(object sender, RoutedEventArgs e)
        {
            WriteResult(cameraNo: 1, resultCode: 1);
        }

        private void BtnWriteCam1Ng_Click(object sender, RoutedEventArgs e)
        {
            WriteResult(cameraNo: 1, resultCode: 2);
        }

        private void BtnWriteCam2Ok_Click(object sender, RoutedEventArgs e)
        {
            WriteResult(cameraNo: 2, resultCode: 1);
        }

        private void BtnWriteCam2Ng_Click(object sender, RoutedEventArgs e)
        {
            WriteResult(cameraNo: 2, resultCode: 2);
        }



        private void WriteResult(int cameraNo, int resultCode)
        {
            try
            {
                if (!_plcService.IsConnected)
                {
                    MessageBox.Show("PLC未连接");
                    return;
                }

                ushort address = cameraNo == 1 ? (ushort)100 : (ushort)101;

                _plcService.WriteSingleRegister(address, (short)resultCode);

                double measureValue = GenerateDemoMeasureValue(resultCode);

                InspectionDataStore.AddRecord(cameraNo, resultCode, measureValue);

                AppendLog($"拍照{cameraNo}结果写入PLC：地址={address}，结果={(resultCode == 1 ? "OK=1" : "NG=2")}，检测值={measureValue:F3}");
            }
            catch (Exception ex)
            {
                AppendLog($"写入PLC失败：{ex.Message}");
                MessageBox.Show($"写入PLC失败：{ex.Message}");
            }
        }

        private double GenerateDemoMeasureValue(int resultCode)
        {
            Random random = new Random();

            if (resultCode == 1)
            {
                return 10.0 + (random.NextDouble() - 0.5) * 0.2;
            }

            return 10.35 + random.NextDouble() * 0.2;
        }

        private void AppendLog(string message)
        {
            txtPlcLog.AppendText($"{DateTime.Now:HH:mm:ss} {message}{Environment.NewLine}");
            txtPlcLog.ScrollToEnd();
        }
    }
}