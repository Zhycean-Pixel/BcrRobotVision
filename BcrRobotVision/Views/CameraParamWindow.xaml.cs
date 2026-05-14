using System;
using System.Windows;
using BcrRobotVision.Camera;
using static Camera.CameraWrapper;

namespace BcrRobotVision.Views
{
    public partial class CameraParamWindow : Window
    {
        private readonly CameraWrapImpl _cameraWrap;

        public CameraParamWindow(CameraWrapImpl cameraWrap)
        {
            InitializeComponent();
            _cameraWrap = cameraWrap;

            InitComboBoxes();
            LoadCameraParams();
        }

        private void InitComboBoxes()
        {
            cmbCaptureMode.Items.Clear();
            cmbCaptureMode.Items.Add("内部触发");
            cmbCaptureMode.Items.Add("编码器");
            cmbCaptureMode.Items.Add("外部触发");

            cmbTransMode.Items.Clear();
            if (_cameraWrap.IsSupportParamId())
            {
                cmbTransMode.Items.Add("软件批处理");
                cmbTransMode.Items.Add("硬件批处理");
                cmbTransMode.Items.Add("连续软件触发");
                cmbTransMode.Items.Add("连续硬件触发");
            }
            else
            {
                cmbTransMode.Items.Add("连续");
                cmbTransMode.Items.Add("软件批处理");
                cmbTransMode.Items.Add("硬件批处理");
            }

            cmbDataType.Items.Clear();
            cmbDataType.Items.Add("高度");
            cmbDataType.Items.Add("高度+灰度");
        }

        private void LoadCameraParams()
        {
            try
            {
                byte capMode = _cameraWrap.GetCaptureMode();
                byte transMode = _cameraWrap.GetTransMode();
                byte dataType = _cameraWrap.GetDataType();

                uint grabLineNum = _cameraWrap.GetBatchGrabNumber();
                int callbackLineNum = _cameraWrap.GetPointCloudCallBackNum();
                uint expo = _cameraWrap.GetExpo();
                ushort frame = _cameraWrap.GetFrame();
                uint maxFrame = _cameraWrap.GetMaxFrame();

                if (capMode == (byte)CAPMODE.CAPMODE_AUTO)
                    cmbCaptureMode.SelectedIndex = 0;
                else if (capMode == (byte)CAPMODE.CAPMODE_ENC)
                    cmbCaptureMode.SelectedIndex = 1;
                else if (capMode == (byte)CAPMODE.CAPMODE_PULSE)
                    cmbCaptureMode.SelectedIndex = 2;

                if (_cameraWrap.IsSupportParamId())
                {
                    if (transMode == (byte)TRANSMODE.TRANSMODE_BATCH_SW)
                        cmbTransMode.SelectedIndex = 0;
                    else if (transMode == (byte)TRANSMODE.TRANSMODE_BATCH_HW)
                        cmbTransMode.SelectedIndex = 1;
                    else if (transMode == (byte)TRANSMODE.TRANSMODE_CONTINOUS_SW)
                        cmbTransMode.SelectedIndex = 2;
                    else if (transMode == (byte)TRANSMODE.TRANSMODE_CONTINOUS_HW)
                        cmbTransMode.SelectedIndex = 3;
                }
                else
                {
                    if (transMode == (byte)TRANSMODE.TRANSMODE_CONTINOUS)
                        cmbTransMode.SelectedIndex = 0;
                    else if (transMode == (byte)TRANSMODE.TRANSMODE_BATCH_SW)
                        cmbTransMode.SelectedIndex = 1;
                    else if (transMode == (byte)TRANSMODE.TRANSMODE_BATCH_HW)
                        cmbTransMode.SelectedIndex = 2;
                }

                if (dataType == (byte)DATATYPE.DATATYPE_PROFILE)
                    cmbDataType.SelectedIndex = 0;
                else if (dataType == (byte)DATATYPE.DATATYPE_PROFILE_GRAY)
                    cmbDataType.SelectedIndex = 1;

                txtGrabLineNum.Text = grabLineNum.ToString();
                txtCallbackLineNum.Text = callbackLineNum.ToString();
                txtExpo.Text = expo.ToString();
                txtFrame.Text = frame.ToString();
                txtMaxFrame.Text = maxFrame.ToString();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"读取相机参数失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnLoad_Click(object sender, RoutedEventArgs e)
        {
            LoadCameraParams();
        }

        private void BtnSave_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                byte capMode = GetCaptureModeFromUi();
                byte transMode = GetTransModeFromUi();
                byte dataType = GetDataTypeFromUi();

                uint grabLineNum = uint.Parse(txtGrabLineNum.Text.Trim());
                int callbackLineNum = int.Parse(txtCallbackLineNum.Text.Trim());
                uint expo = uint.Parse(txtExpo.Text.Trim());
                ushort frame = ushort.Parse(txtFrame.Text.Trim());

                bool ok = true;

                if (!_cameraWrap.SetCaptureMode(capMode))
                    ok = false;

                if (!_cameraWrap.SetCamParams(dataType, transMode, grabLineNum))
                    ok = false;

                if (!_cameraWrap.SetFrame(frame))
                    ok = false;

                if (!_cameraWrap.SetExpo(expo))
                    ok = false;

                if (!_cameraWrap.SetPointCloudCallBackNum(callbackLineNum))
                    ok = false;

                if (!ok)
                {
                    MessageBox.Show("保存参数失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    return;
                }

                LoadCameraParams();
                MessageBox.Show("保存参数成功", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存参数异常：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private byte GetCaptureModeFromUi()
        {
            return cmbCaptureMode.SelectedIndex switch
            {
                0 => (byte)CAPMODE.CAPMODE_AUTO,
                1 => (byte)CAPMODE.CAPMODE_ENC,
                2 => (byte)CAPMODE.CAPMODE_PULSE,
                _ => (byte)CAPMODE.CAPMODE_AUTO
            };
        }

        private byte GetTransModeFromUi()
        {
            if (_cameraWrap.IsSupportParamId())
            {
                return cmbTransMode.SelectedIndex switch
                {
                    0 => (byte)TRANSMODE.TRANSMODE_BATCH_SW,
                    1 => (byte)TRANSMODE.TRANSMODE_BATCH_HW,
                    2 => (byte)TRANSMODE.TRANSMODE_CONTINOUS_SW,
                    3 => (byte)TRANSMODE.TRANSMODE_CONTINOUS_HW,
                    _ => (byte)TRANSMODE.TRANSMODE_BATCH_SW
                };
            }

            return cmbTransMode.SelectedIndex switch
            {
                0 => (byte)TRANSMODE.TRANSMODE_CONTINOUS,
                1 => (byte)TRANSMODE.TRANSMODE_BATCH_SW,
                2 => (byte)TRANSMODE.TRANSMODE_BATCH_HW,
                _ => (byte)TRANSMODE.TRANSMODE_CONTINOUS
            };
        }

        private byte GetDataTypeFromUi()
        {
            return cmbDataType.SelectedIndex switch
            {
                0 => (byte)DATATYPE.DATATYPE_PROFILE,
                1 => (byte)DATATYPE.DATATYPE_PROFILE_GRAY,
                _ => (byte)DATATYPE.DATATYPE_PROFILE
            };
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}