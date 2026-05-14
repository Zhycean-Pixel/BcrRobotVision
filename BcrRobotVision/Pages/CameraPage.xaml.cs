using BcrRobotVision.Camera;
using BcrRobotVision.Models;
using BcrRobotVision.Services;
using BcrRobotVision.Views;
using HalconDotNet;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Media.Media3D;
using System.Windows.Threading;
using static Camera.CameraWrapper;
using Microsoft.Win32;
using System.IO;
using System.Threading.Tasks;
using System.Threading;





namespace BcrRobotVision.Pages
{
    public partial class CameraPage : UserControl
    {
        private CameraWrapImpl? _cameraWrap;
        public CameraWrapImpl? CameraWrap => _cameraWrap;

        private readonly object _lockForImageRef = new object();

        private IntPtr _pGrayData = IntPtr.Zero;
        private IntPtr _pHeightData = IntPtr.Zero;
        private SG_DEPTHDATA_PARAM _depthParam;
        private bool _hasFreshImage = false;

        private readonly DispatcherTimer _timerUpdatePointCloud;

        private readonly ConfigService _configService = new ConfigService();
        private readonly LogService _logService = new LogService();
        private AppConfig _appConfig = new AppConfig();

        private float[] _latestZValues = Array.Empty<float>();
        private int _latestWidth;
        private int _latestHeight;
        private float _latestXSpace;
        private float _latestYSpace;

        private PerspectiveCamera? _camera3D;
        private AxisAngleRotation3D? _rotateX;
        private AxisAngleRotation3D? _rotateY;
        private bool _isDragging3D;
        private Point _lastMousePoint;
        private double _cameraDistance = 600;

        private readonly PlcService _autoPlcService = new PlcService();

        // PLC自动监听线程的取消源。
        // 点击“启动自动监听”后会创建，点击停止监听、断开PLC或关闭软件时必须取消并释放，
        // 否则后台Task可能继续读PLC，导致程序关闭后进程残留。
        private CancellationTokenSource? _plcListenCts;

        // 关闭软件时置为true。此时不再向界面控件追加日志，避免窗口销毁后后台回调访问UI引发异常。
        private bool _isCleaningUp = false;

        // 相机宽度目前是设备固定输出，保留这个值主要用于日志提醒。
        private const int ExpectedImageWidth = 704;

        // 相机回调正常会给出 _iWantCaptureProfileLineNum，例如 1600。
        // 如果设备没有返回目标线数，才使用这个兜底值。
        private const int FallbackExpectedImageHeight = 320;

        // 保存上一轮PLC拍照信号状态，用来避免PLC信号保持为1时每50ms重复触发拍照。
        private bool _lastPhoto1Signal = false;

        // 自动拍照互斥锁：防止PLC信号抖动或连续触发时，同时跑多个拍照/保存/HALCON流程。
        private readonly SemaphoreSlim _autoPhotoLock = new SemaphoreSlim(1, 1);

        // 自动拍照时等待OnDepth回调返回完整3D图。
        // PLC触发后创建，OnDepth收到完整图后TrySetResult，流程继续保存/检测。
        private TaskCompletionSource<CameraFrameSnapshot>? _grabWaiter;
        private DateTime _activeMaterialStartTime = DateTime.MinValue;

        // PLC拍照信号：当前版本只监听一个开始拍照信号。
        // 一个工位两个产品改为一次拍照，因此不再监听第二路拍照信号。
        private const ushort Material1StartAddress = 0x80; // 01x80 一号开始

        // PLC结果地址：虽然只拍一次图，但现场仍然有两个产品结果位。
        // 算法未使能时两个地址都写OK=1；算法使能时当前也先把同一个结果写到两个地址。
        private const ushort Photo1ResultAddress = 100;  // 一号结果
        private const ushort Photo2ResultAddress = 101;  // 二号结果

        private int _activeMaterialNo = 0;
        private readonly string _imageRootFolder = @"D:\BcrRobotVisionData\InspectionImages";
        private readonly string _historyImageFolderName = "SourceImages";

        // 给HALCON算法使用的“当前图”目录：每次只保留CurrentImage.tiff。
        // 这样算法入口稳定，不会因为目录里有多张图片而拿错。
        private readonly string _plcCurrentImageFolder = @"D:\BcrRobotVisionData\HalconCurrentImage";
        private readonly string _plcCurrentImagePath = @"D:\BcrRobotVisionData\HalconCurrentImage\CurrentImage.tiff";
        private string _lastSavedCurrentImagePath = "";

        // 确保HALCON当前图目录和历史素材目录可用。这里先检查盘符是否存在，避免现场机器没有D盘时报异常。
        private bool EnsureCurrentImageFolder()
        {
            try
            {
                string? drive = Path.GetPathRoot(_imageRootFolder);

                if (string.IsNullOrWhiteSpace(drive) || !Directory.Exists(drive))
                {
                    AppendLog($"图片保存失败：磁盘不存在 {drive}");
                    MessageBox.Show($"图片保存失败：磁盘不存在 {drive}");
                    return false;
                }

                Directory.CreateDirectory(_plcCurrentImageFolder);
                Directory.CreateDirectory(GetTodayHistoryImageFolder());

                txtSaveFolder.Text =
                    $"HALCON当前图目录：{_plcCurrentImageFolder}\n历史素材目录：{GetTodayHistoryImageFolder()}";

                AppendLog($"HALCON当前图目录已确认：{_plcCurrentImageFolder}");
                AppendLog($"历史素材目录已确认：{GetTodayHistoryImageFolder()}");

                return true;
            }
            catch (Exception ex)
            {
                AppendLog($"创建图片目录失败：{ex.Message}");
                MessageBox.Show($"创建图片目录失败：{ex.Message}");
                return false;
            }
        }


        public CameraPage()
        {
            InitializeComponent();
            txtSaveFolder.Text =
                $"HALCON当前图目录：{_plcCurrentImageFolder}\n历史素材目录：{GetTodayHistoryImageFolder()}";
            EnsureCurrentImageFolder();


            Init3DView();

            _appConfig = _configService.Load();

            txtLocalIp.Text = _appConfig.LocalIp;
            txtCameraIp.Text = _appConfig.CameraIp;

            txtConnectState.Text = "未连接";
            txtCameraInfo.Text = "等待搜索相机";
            txtGrabInfo.Text = "暂无数据";

            _timerUpdatePointCloud = new DispatcherTimer();
            _timerUpdatePointCloud.Interval = TimeSpan.FromMilliseconds(200);
            _timerUpdatePointCloud.Tick += TimerUpdatePointCloud_Tick;

            AppendLog("相机页面已加载");

            Unloaded += CameraPage_Unloaded;
        }

        private class CameraFrameSnapshot
        {
            public float[] ZValues { get; set; } = Array.Empty<float>();
            public int Width { get; set; }
            public int Height { get; set; }
            public float XSpace { get; set; }
            public float YSpace { get; set; }
            public SG_DEPTHDATA_PARAM Param { get; set; }
        }

        private class HalconInspectionResult
        {
            public double MeanZLeft { get; set; }
            public double MeanZRight { get; set; }
            public double TiltDiff { get; set; }
            public double DevLRegion { get; set; }
            public double DevRRegion { get; set; }
            public double MaxDeviation { get; set; }
            public string ResultText { get; set; } = "";
            public int ResultCode { get; set; }
        }


        private bool InitCameraSdk()
        {
            try
            {
                if (_cameraWrap != null)
                    return true;

                AppendLog("开始初始化相机SDK...");

                CameraWrapImpl.LibInit();

                _cameraWrap = new CameraWrapImpl();
                _cameraWrap.SG_CAP_DEPTH_CB(OnDepth);
                _cameraWrap.SG_CAP_IMGROFILE_CB(OnImageProfile);
                _cameraWrap.SG_CAP_IMG_CB(OnImage);

                AppendLog("相机SDK初始化完成");
                return true;
            }
            catch (Exception ex)
            {
                AppendLog($"相机SDK初始化失败：{ex.Message}");
                MessageBox.Show($"相机SDK初始化失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return false;
            }
        }


        /*
         * 保存PLC触发后的完整图像。
         *
         * 注意：
         * 1. 历史素材目录：按时间戳保存多张图，用于后续改HALCON算法。
         * 2. HALCON当前图目录：每次覆盖CurrentImage.tiff，保证算法固定读取唯一当前图。
         * 3. 返回值是HALCON当前图路径，也就是后续算法实际读取的路径。
         */
        private string SaveCurrentImageForHalcon(HObject halconImage, BitmapSource? depthBitmap)
        {
            try
            {
                if (!EnsureCurrentImageFolder())
                    return "";

                string historyImagePath = Path.Combine(
                    GetTodayHistoryImageFolder(),
                    $"CurrentImage_{DateTime.Now:yyyyMMdd_HHmmss_fff}.tiff");

                bool historySaved = TrySaveHalconOrBitmapImage(
                    halconImage,
                    depthBitmap,
                    historyImagePath,
                    "历史素材图");

                bool currentSaved = TrySaveHalconOrBitmapImage(
                    halconImage,
                    depthBitmap,
                    _plcCurrentImagePath,
                    "HALCON当前图");

                if (!historySaved)
                    AppendLog("历史素材图保存失败，但不影响HALCON当前图流程");

                if (currentSaved)
                {
                    _lastSavedCurrentImagePath = _plcCurrentImagePath;
                    return _plcCurrentImagePath;
                }

                AppendLog("保存HALCON当前图失败：halconImage和depthBitmap都没有成功写入");
                return "";
            }
            catch (Exception ex)
            {
                AppendLog($"保存当前图片异常：{ex.Message}");
                MessageBox.Show($"保存当前图片异常：{ex.Message}");
                return "";
            }
        }

        private bool TrySaveHalconOrBitmapImage(
            HObject halconImage,
            BitmapSource? depthBitmap,
            string imagePath,
            string imageName)
        {
            try
            {
                string? folder = Path.GetDirectoryName(imagePath);
                if (!string.IsNullOrWhiteSpace(folder))
                    Directory.CreateDirectory(folder);

                try
                {
                    HOperatorSet.WriteImage(halconImage, "tiff", 0, imagePath);

                    if (File.Exists(imagePath))
                    {
                        AppendLog($"{imageName}已保存：{imagePath}");
                        return true;
                    }

                    AppendLog($"{imageName} HALCON写图后未发现文件，准备使用界面图兜底保存");
                }
                catch (Exception ex)
                {
                    AppendLog($"{imageName} HALCON原始图保存失败：{ex.Message}");
                }

                if (depthBitmap != null)
                {
                    SaveBitmapSourceAsTiff(depthBitmap, imagePath);

                    if (File.Exists(imagePath))
                    {
                        AppendLog($"{imageName}高度图已保存：{imagePath}");
                        return true;
                    }
                }

                return false;
            }
            catch (Exception ex)
            {
                AppendLog($"{imageName}保存异常：{ex.Message}");
                return false;
            }
        }


        private void SaveConfig()
        {
            _appConfig.LocalIp = txtLocalIp.Text.Trim();
            _appConfig.CameraIp = txtCameraIp.Text.Trim();
            _configService.Save(_appConfig);
        }

        private void BtnDetect_Click(object sender, RoutedEventArgs e)
        {
            if (!InitCameraSdk())
                return;

            try
            {
                IntPtr ptr = CameraWrapImpl.DetectCamera();
                string raw = Marshal.PtrToStringAnsi(ptr) ?? string.Empty;

                AppendLog($"搜索相机结果: {raw}");

                Common.IpInfo[] devices = Common.analysisIpInfos(raw);
                if (devices.Length > 0)
                {
                    txtLocalIp.Text = devices[0].localIp;
                    txtCameraIp.Text = devices[0].cameraIp;
                    txtCameraInfo.Text = $"已搜索到相机，本机IP={devices[0].localIp}，相机IP={devices[0].cameraIp}";
                    SaveConfig();
                }
                else
                {
                    txtCameraInfo.Text = "未搜索到相机";
                    MessageBox.Show("未搜索到相机", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
            }
            catch (Exception ex)
            {
                AppendLog($"搜索相机异常: {ex.Message}");
                MessageBox.Show($"搜索相机异常：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (!InitCameraSdk())
                return;

            try
            {
                if (_cameraWrap!.GetCameraIsConnect())
                {
                    AppendLog("相机已连接，无需重复连接");
                    return;
                }

                string localIp = txtLocalIp.Text.Trim();
                string camIp = txtCameraIp.Text.Trim();

                if (string.IsNullOrWhiteSpace(localIp) || string.IsNullOrWhiteSpace(camIp))
                {
                    MessageBox.Show("请先填写本机IP和相机IP", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!_cameraWrap.Connect2Camera(localIp, camIp))
                {
                    txtConnectState.Text = "未连接";
                    txtConnectState.Foreground = Brushes.Red;
                    MessageBox.Show("连接相机失败", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                    AppendLog("连接相机失败");
                    return;
                }

                UpdateCameraInfo();
                SaveConfig();
                AppendLog("连接相机成功");
                MessageBox.Show("连接相机成功", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                AppendLog($"连接相机异常: {ex.Message}");
                MessageBox.Show($"连接相机异常：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void BtnDisConnect_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_cameraWrap != null && _cameraWrap.GetCameraIsConnect())
                {
                    _cameraWrap.CloseCamera();
                }

                txtConnectState.Text = "未连接";
                txtConnectState.Foreground = Brushes.Red;
                txtCameraInfo.Text = "相机已断开";
                AppendLog("相机已断开连接");
            }
            catch (Exception ex)
            {
                AppendLog($"断开相机异常: {ex.Message}");
            }
        }

        private void BtnAutoConnectPlc_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string ip = txtAutoPlcIp.Text.Trim();
                int port = int.Parse(txtAutoPlcPort.Text.Trim());

                bool ok = _autoPlcService.Connect(ip, port);

                txtAutoPlcState.Text = ok ? "PLC自动：已连接" : "PLC自动：未连接";
                txtAutoPlcState.Foreground = ok ? Brushes.LimeGreen : Brushes.Red;

                AppendLog(ok ? $"PLC自动连接成功：{ip}:{port}" : "PLC自动连接失败");
            }
            catch (Exception ex)
            {
                txtAutoPlcState.Text = "PLC自动：连接失败";
                txtAutoPlcState.Foreground = Brushes.Red;
                AppendLog($"PLC自动连接失败：{ex.Message}");
                MessageBox.Show($"PLC自动连接失败：{ex.Message}");
            }
        }

        private void BtnAutoDisconnectPlc_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _plcListenCts?.Cancel();
                _plcListenCts?.Dispose();
                _plcListenCts = null;

                _autoPlcService.Disconnect();

                txtAutoPlcState.Text = "PLC自动：未连接";
                txtAutoPlcState.Foreground = Brushes.Red;

                AppendLog("PLC自动已断开");
            }
            catch (Exception ex)
            {
                AppendLog($"PLC自动断开异常：{ex.Message}");
            }
        }

        private void BtnStartPlcAuto_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (!EnsureCurrentImageFolder())
                    return;

                if (!_autoPlcService.IsConnected)
                {
                    MessageBox.Show("请先连接PLC");
                    return;
                }

                if (_plcListenCts != null)
                {
                    MessageBox.Show("PLC自动监听已经在运行");
                    return;
                }

                _lastPhoto1Signal = false;

                txtSaveFolder.Text =
                    $"HALCON当前图目录：{_plcCurrentImageFolder}\n历史素材目录：{GetTodayHistoryImageFolder()}";

                _plcListenCts = new CancellationTokenSource();

                _ = Task.Run(() => PlcFastListenLoopAsync(_plcListenCts.Token));

                txtAutoPlcState.Text = "PLC自动：高速监听中";
                txtAutoPlcState.Foreground = Brushes.LimeGreen;

                AppendLog("PLC自动监听已启动，50ms读取一次拍照信号 01x80");
            }
            catch (Exception ex)
            {
                AppendLog($"启动PLC自动监听失败：{ex.Message}");
                MessageBox.Show($"启动PLC自动监听失败：{ex.Message}");
            }
        }

        private async Task PlcFastListenLoopAsync(CancellationToken token)
        {
            DateTime lastStateUpdateTime = DateTime.MinValue;

            while (!token.IsCancellationRequested)
            {
                try
                {
                    // 单次拍照模式：只监听01x80。
                    // PLC线圈保持为1时会被每50ms读到一次，因此下面使用_lastPhoto1Signal
                    // 只在“本次为1、上一轮为0”的瞬间启动一次自动拍照流程。
                    bool[] signals = _autoPlcService.ReadCoils(Material1StartAddress, 1);

                    bool photo1Signal = signals.Length > 0 && signals[0];

                    if (photo1Signal != _lastPhoto1Signal)
                    {
                        _ = Dispatcher.BeginInvoke(new Action(() =>
                        {
                            AppendLog($"PLC单次拍照信号变化：01x80={photo1Signal}");
                        }));
                    }

                    if (photo1Signal && !_lastPhoto1Signal)
                    {
                        // 一个工位两个产品现在只触发一次拍照，因此固定传1。
                        // 后续写PLC结果时会同时写100和101两个地址。
                        StartAutoPhotoFromPlc(1);
                    }

                    // 每轮循环结束后记录当前状态，供下一轮判断是否需要触发。
                    _lastPhoto1Signal = photo1Signal;

                    if ((DateTime.Now - lastStateUpdateTime).TotalSeconds >= 1)
                    {
                        lastStateUpdateTime = DateTime.Now;

                        _ = Dispatcher.BeginInvoke(new Action(() =>
                        {
                            txtAutoPlcState.Text =
                                $"PLC自动：监听中  01x80={photo1Signal}";

                            txtAutoPlcState.Foreground = Brushes.LimeGreen;
                        }));
                    }
                }
                catch (Exception ex)
                {
                    _ = Dispatcher.BeginInvoke(new Action(() =>
                    {
                        AppendLog($"PLC自动读取信号异常：{ex.Message}");
                        txtAutoPlcState.Text = "PLC自动：读取异常";
                        txtAutoPlcState.Foreground = Brushes.Red;
                    }));
                }

                try
                {
                    await Task.Delay(50, token);
                }
                catch
                {
                    break;
                }
            }
        }

        private string GetTodayHistoryImageFolder()
        {
            return Path.Combine(
                _imageRootFolder,
                DateTime.Now.ToString("yyyyMMdd"),
                _historyImageFolderName);
        }

        private void StartMaterialCaptureFromPlc(int materialNo)
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                AppendLog($"捕捉到 PLC 物料{materialNo}开始拍照信号");

                if (_activeMaterialNo != 0)
                {
                    if ((DateTime.Now - _activeMaterialStartTime).TotalSeconds > 8)
                    {
                        AppendLog($"物料{_activeMaterialNo}超时未结束，自动复位，允许新的物料{materialNo}开始");
                        _activeMaterialNo = 0;
                        _grabWaiter = null;
                    }
                    else
                    {
                        AppendLog($"当前物料{_activeMaterialNo}还未结束，忽略物料{materialNo}开始信号");
                        return;
                    }
                }

                if (!EnsureCameraReadyForAuto())
                {
                    AppendLog($"物料{materialNo}开始失败：相机未准备好");
                    return;
                }

                if (_cameraWrap == null)
                {
                    AppendLog($"物料{materialNo}开始失败：相机对象为空");
                    return;
                }

                _activeMaterialNo = materialNo;
                _activeMaterialStartTime = DateTime.Now;

                _grabWaiter = new TaskCompletionSource<CameraFrameSnapshot>(
                    TaskCreationOptions.RunContinuationsAsynchronously);

                bool triggerOk = _cameraWrap.SendGrabSignalToCamera(false);

                if (!triggerOk)
                {
                    AppendLog($"物料{materialNo}发送抓图触发失败");
                    _activeMaterialNo = 0;
                    _grabWaiter = null;
                    return;
                }

                AppendLog($"物料{materialNo}已发送开始抓图信号，等待PLC停止信号01x82");
            }));
        }

        private void StopMaterialCaptureFromPlc(int materialNo)
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                AppendLog($"捕捉到 PLC 物料{materialNo}停止拍照信号");
                _ = StopMaterialCaptureAsync(materialNo);
            }));
        }

        private async Task StopMaterialCaptureAsync(int materialNo)
        {
            await _autoPhotoLock.WaitAsync();

            try
            {
                if (_activeMaterialNo != materialNo)
                {
                    AppendLog($"物料{materialNo}停止信号无效：当前活动物料={_activeMaterialNo}");
                    return;
                }

                if (_grabWaiter == null)
                {
                    AppendLog($"物料{materialNo}停止失败：没有等待中的图像任务");
                    _activeMaterialNo = 0;
                    return;
                }

                if (_cameraWrap == null)
                {
                    AppendLog($"物料{materialNo}停止失败：相机对象为空");
                    _grabWaiter = null;
                    _activeMaterialNo = 0;
                    return;
                }

                bool stopOk = _cameraWrap.SendGrabSignalToCamera(true);
                if (!stopOk)
                {
                    AppendLog($"物料{materialNo}发送停止抓图信号失败");
                    _grabWaiter = null;
                    _activeMaterialNo = 0;
                    return;
                }

                AppendLog($"物料{materialNo}停止信号已收到，已发送结束抓图信号，等待完整图像回调");

                Task completedTask = await Task.WhenAny(_grabWaiter.Task, Task.Delay(5000));

                if (completedTask != _grabWaiter.Task)
                {
                    AppendLog($"物料{materialNo}等待完整3D图超时");
                    _grabWaiter = null;
                    _activeMaterialNo = 0;
                    return;
                }

                CameraFrameSnapshot snapshot = await _grabWaiter.Task;

                _grabWaiter = null;
                _activeMaterialNo = 0;

                await ProcessMaterialSnapshotAsync(materialNo, snapshot);
            }
            catch (Exception ex)
            {
                AppendLog($"物料{materialNo}停止处理异常：{ex.Message}");
                MessageBox.Show($"物料{materialNo}停止处理异常：{ex.Message}");
                _grabWaiter = null;
                _activeMaterialNo = 0;
            }
            finally
            {
                _autoPhotoLock.Release();
            }
        }

        private async Task ProcessMaterialSnapshotAsync(int materialNo, CameraFrameSnapshot snapshot)
        {
            try
            {
                AppendLog($"物料{materialNo}取图成功：{snapshot.Width}×{snapshot.Height}");

                if (snapshot.Height <= 0)
                {
                    AppendLog($"物料{materialNo}图像高度异常：当前={snapshot.Width}×{snapshot.Height}");
                    return;
                }

                if (snapshot.Width != ExpectedImageWidth)
                {
                    AppendLog($"提示：当前图像宽度={snapshot.Width}，目标宽度={ExpectedImageWidth}，当前先按实际宽度处理");
                }

                BitmapSource? depthBitmap =
                    CreateDepthBitmapFromArray(snapshot.ZValues, snapshot.Width, snapshot.Height);

                if (depthBitmap != null)
                {
                    imgDepth.Source = depthBitmap;
                    AppendLog($"物料{materialNo}完整3D图已显示到Camera界面");
                }

                HObject halconImage = CreateHalconImageFromSnapshot(snapshot);

                string currentImagePath = SaveCurrentImageForHalcon(halconImage, depthBitmap);

                halconImage.Dispose();

                if (string.IsNullOrWhiteSpace(currentImagePath))
                {
                    AppendLog("当前图片保存失败，终止HALCON检测");
                    return;
                }

                if (chkEnableHalcon.IsChecked != true)
                {
                    ShowHalconDisabledResult();
                    WriteOkToBothResultAddresses();
                    AppendLog($"视觉算法未使能：已保存完整图片={currentImagePath}，PLC地址100/101已写入OK=1");
                    return;
                }

                HalconInspectionResult halconResult = await Task.Run(() =>
                {
                    return ExecuteHalconAlgorithmFromImagePath(currentImagePath);
                });

                ShowHalconResult(halconResult);

                WriteResultToBothAddresses((short)halconResult.ResultCode);

                InspectionDataStore.AddRecord(
                    materialNo,
                    halconResult.ResultCode,
                    halconResult.MaxDeviation,
                    halconResult.MeanZLeft,
                    halconResult.MeanZRight,
                    halconResult.TiltDiff,
                    halconResult.DevLRegion,
                    halconResult.DevRRegion,
                    halconResult.MaxDeviation,
                    halconResult.ResultText,
                    currentImagePath);

                AppendLog($"物料{materialNo}处理完成：{halconResult.ResultText}，PLC地址100/101写入={halconResult.ResultCode}");
            }
            catch (Exception ex)
            {
                AppendLog($"物料{materialNo}完整图处理异常：{ex.Message}");
                MessageBox.Show($"物料{materialNo}完整图处理异常：{ex.Message}");
            }
        }

        private void StartAutoPhotoFromPlc(int cameraNo)
        {
            _ = Dispatcher.BeginInvoke(new Action(() =>
            {
                AppendLog($"捕捉到 PLC 拍照{cameraNo} 启动信号");

                _ = ProcessPlcPhotoSignalAsync(cameraNo);
            }));
        }



        private HalconInspectionResult ExecuteHalconAlgorithm(HObject image)
        {
            HalconEngine_Test.ResourcePath =
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res_HalconEngine_Test");

            HalconEngine_Test.Bearing_inspection(
                image,
                out HTuple meanZLeft,
                out HTuple meanZRight,
                out HTuple tiltDiff,
                out HTuple devLRegion,
                out HTuple devRRegion,
                out HTuple maxDeviation,
                out HTuple result);

            string resultText = result.ToString();

            int resultCode = resultText.Contains("OK", StringComparison.OrdinalIgnoreCase)
                ? 1
                : 2;

            return new HalconInspectionResult
            {
                MeanZLeft = ToDouble(meanZLeft),
                MeanZRight = ToDouble(meanZRight),
                TiltDiff = ToDouble(tiltDiff),
                DevLRegion = ToDouble(devLRegion),
                DevRRegion = ToDouble(devRRegion),
                MaxDeviation = ToDouble(maxDeviation),
                ResultText = resultText,
                ResultCode = resultCode
            };
        }


        private HalconInspectionResult ExecuteHalconAlgorithmFromCurrentImage()
        {
            string imagePath = !string.IsNullOrWhiteSpace(_lastSavedCurrentImagePath)
                ? _lastSavedCurrentImagePath
                : _plcCurrentImagePath;

            return ExecuteHalconAlgorithmFromImagePath(imagePath);
        }

        private HalconInspectionResult ExecuteHalconAlgorithmFromImagePath(string imagePath)
        {
            if (!File.Exists(imagePath))
                throw new FileNotFoundException("当前HALCON图片不存在", imagePath);

            HOperatorSet.ReadImage(out HObject image, imagePath);

            try
            {
                return ExecuteHalconAlgorithm(image);
            }
            finally
            {
                image.Dispose();
            }
        }


        private BitmapSource? CreateDepthBitmapFromArray(float[] zValues, int width, int height)
        {
            if (zValues.Length == 0 || width <= 0 || height <= 0)
                return null;

            int count = width * height;

            List<float> validZ = zValues.Where(z => z > 0.00001f).ToList();

            float zMin = 0;
            float zMax = 0;

            if (validZ.Count > 0)
            {
                zMin = validZ.Min();
                zMax = validZ.Max();
            }

            byte[] pixels = new byte[count];

            for (int i = 0; i < count && i < zValues.Length; i++)
            {
                pixels[i] = Rescale(zValues[i], zMax, zMin);
            }

            BitmapSource bitmap = BitmapSource.Create(
                width,
                height,
                96,
                96,
                PixelFormats.Gray8,
                BitmapPalettes.Gray256,
                pixels,
                width);

            bitmap.Freeze();

            return bitmap;
        }


        /*自动创建文件夹*/
        private string SaveInspectionImage(
    int cameraNo,
    HalconInspectionResult result,
    BitmapSource? depthBitmap,
    HObject halconImage)
        {
            try
            {
                string resultFolderName = result.ResultCode == 1 ? "OK" : "NG";

                string root = Path.Combine(
                    _imageRootFolder,
                    DateTime.Now.ToString("yyyyMMdd"),
                    resultFolderName);

                Directory.CreateDirectory(root);

                string fileBaseName =
                    $"{DateTime.Now:HHmmssfff}_拍照{cameraNo}_{resultFolderName}";

                string pngPath = Path.Combine(root, fileBaseName + ".png");
                string tiffPath = Path.Combine(root, fileBaseName + ".tiff");

                if (depthBitmap != null)
                {
                    SaveBitmapSourceAsPng(depthBitmap, pngPath);
                    AppendLog($"PNG图片保存成功：{pngPath}");
                }
                else
                {
                    AppendLog("PNG图片保存失败：depthBitmap为空");
                }

                try
                {
                    HOperatorSet.WriteImage(halconImage, "tiff", 0, tiffPath);
                    AppendLog($"HALCON图像保存成功：{tiffPath}");
                }
                catch (Exception ex)
                {
                    AppendLog($"HALCON tiff保存失败：{ex.Message}");
                }

                return pngPath;
            }
            catch (Exception ex)
            {
                AppendLog($"创建文件夹或保存图片失败：{ex.Message}");
                MessageBox.Show($"创建文件夹或保存图片失败：{ex.Message}");
                return "";
            }
        }

        private void SaveBitmapSourceAsPng(BitmapSource bitmap, string path)
        {
            string? folder = Path.GetDirectoryName(path);

            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }

            using FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write);

            PngBitmapEncoder encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
        }

        private void SaveBitmapSourceAsTiff(BitmapSource bitmap, string path)
        {
            string? folder = Path.GetDirectoryName(path);

            if (!string.IsNullOrWhiteSpace(folder))
            {
                Directory.CreateDirectory(folder);
            }

            using FileStream stream = new FileStream(path, FileMode.Create, FileAccess.Write);

            TiffBitmapEncoder encoder = new TiffBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            encoder.Save(stream);
        }

        private unsafe HObject CreateHalconImageFromSnapshot(CameraFrameSnapshot snapshot)
        {
            fixed (float* p = snapshot.ZValues)
            {
                HOperatorSet.GenImage1(
                    out HObject tempImage,
                    "real",
                    snapshot.Width,
                    snapshot.Height,
                    new IntPtr(p));

                HOperatorSet.CopyImage(tempImage, out HObject copiedImage);

                tempImage.Dispose();

                return copiedImage;
            }
        }

        
        private double ToDouble(HTuple tuple)
        {
            try
            {
                if (tuple == null || tuple.Length <= 0)
                    return 0;

                return tuple.D;
            }
            catch
            {
                double.TryParse(tuple.ToString(), out double value);
                return value;
            }
        }

        private async Task<CameraFrameSnapshot?> TriggerCameraAndWaitFrameAsync()
        {
            if (_cameraWrap == null)
            {
                AppendLog("自动触发失败：_cameraWrap为空");
                return null;
            }

            _grabWaiter = new TaskCompletionSource<CameraFrameSnapshot>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            AppendLog("准备发送相机软件触发信号");

            // 这里仍然使用相机SDK的一次软件触发接口。
            // 触发后并不立刻保存，而是等待OnDepth收到“目标线数”的完整高度图。
            bool triggerOk = _cameraWrap.SendGrabSignalToCamera();

            if (!triggerOk)
            {
                AppendLog("自动触发抓图失败：SendGrabSignalToCamera返回false");
                _grabWaiter = null;
                return null;
            }

            AppendLog("相机软件触发信号已发送，等待OnDepth回调");

            Task completedTask = await Task.WhenAny(_grabWaiter.Task, Task.Delay(5000));

            if (completedTask != _grabWaiter.Task)
            {
                AppendLog("等待OnDepth图像回调超时");

                _grabWaiter = null;
                return null;
            }

            CameraFrameSnapshot snapshot = await _grabWaiter.Task;

            _grabWaiter = null;

            return snapshot;
        }


        private bool EnsureCameraReadyForAuto()
        {
            if (!InitCameraSdk())
                return false;

            if (_cameraWrap == null)
                return false;

            if (!_cameraWrap.GetCameraIsConnect())
            {
                MessageBox.Show("相机未连接，请先连接相机");
                return false;
            }

            if (!_cameraWrap.GetCameraIsGrab())
            {
                // 切换到采集模式并启动采集。
                // 不在这里设置抓取线数，避免覆盖相机自带软件里配置的1600/1700等线数。
                if (!_cameraWrap.SetUserOperatorMode((byte)DATAMODE.DATAMODE_GRAB))
                {
                    MessageBox.Show("切换数据采集模式失败");
                    return false;
                }

                if (!_cameraWrap.StartCapture())
                {
                    MessageBox.Show("开始采集失败");
                    return false;
                }

                _timerUpdatePointCloud.Start();
                AppendLog("自动流程已启动相机采集");
                Thread.Sleep(300);
            }
            return true;
        }



        /*
         * PLC自动拍照主流程。
         *
         * 当前版本的现场目的：
         * 1. PLC给01x80信号后，只拍一次完整3D图。
         * 2. 保存完整素材图，用于后续修改HALCON算法。
         * 3. 如果“启用视觉算法”未勾选，不调用旧HALCON算法，避免旧算法因为新图尺寸/流程不匹配而报错。
         * 4. 算法未使能时，仍然向PLC 100/101写OK=1，让PLC流程可以继续跑。
         */
        private async Task ProcessPlcPhotoSignalAsync(int cameraNo)
        {
            await _autoPhotoLock.WaitAsync();

            try
            {
                AppendLog($"收到PLC拍照{cameraNo}启动信号，开始自动拍照");

                if (!EnsureCameraReadyForAuto())
                {
                    AppendLog("自动拍照失败：相机未准备好");
                    return;
                }

                CameraFrameSnapshot? snapshot = await TriggerCameraAndWaitFrameAsync();

                if (snapshot == null)
                {
                    AppendLog($"拍照{cameraNo}失败：等待相机图像超时");
                    return;
                }

                AppendLog($"拍照{cameraNo}取图成功：{snapshot.Width} × {snapshot.Height}");

                // 1. 生成界面显示用高度图
                BitmapSource? depthBitmap =
                    CreateDepthBitmapFromArray(snapshot.ZValues, snapshot.Width, snapshot.Height);

                if (depthBitmap != null)
                {
                    imgDepth.Source = depthBitmap;
                    AppendLog("当前图片已显示到Camera界面");
                }
                else
                {
                    AppendLog("高度图生成失败：depthBitmap为空");
                }

                // 2. 生成HALCON real图。这里保存的是原始高度数据，不是8位显示图。
                HObject halconImage = CreateHalconImageFromSnapshot(snapshot);

                // 3. 保存到素材目录。SaveCurrentImageForHalcon内部会加时间戳，不覆盖旧图片。
                string currentImagePath = SaveCurrentImageForHalcon(halconImage, depthBitmap);

                halconImage.Dispose();

                if (string.IsNullOrWhiteSpace(currentImagePath))
                {
                    AppendLog("当前图片保存失败，终止HALCON检测");
                    return;
                }

                if (chkEnableHalcon.IsChecked != true)
                {
                    // 旧HALCON算法还没改完时走这里：只采图保存，不执行算法。
                    // 为了不阻塞PLC节拍，两个产品结果地址都先写OK。
                    ShowHalconDisabledResult();
                    WriteOkToBothResultAddresses();
                    AppendLog($"视觉算法未使能：已保存完整图片={currentImagePath}，PLC地址100/101已写入OK=1");
                    return;
                }

                // 4. HALCON从本次保存的图片读取并执行算法。
                // 注意：输入给HALCON的是当前高度图实际尺寸，例如704×1600，而不是旧的704×320。
                HalconInspectionResult halconResult = await Task.Run(() =>
                {
                    return ExecuteHalconAlgorithmFromImagePath(currentImagePath);
                });

                // 5. 显示7个中文检测数据
                ShowHalconResult(halconResult);

                // 6. OK/NG写回PLC。
                // 目前新算法还没有拆出两个产品的独立结果，因此先把同一个结果写到100和101。
                ushort resultAddress = cameraNo == 1
                    ? Photo1ResultAddress
                    : Photo2ResultAddress;

                WriteResultToBothAddresses((short)halconResult.ResultCode);

                AppendLog(
                    $"PLC结果写入完成：拍照{cameraNo}，地址100/101，结果={(halconResult.ResultCode == 1 ? "OK=1" : "NG=2")}");

                // 7. 写入报表和CPK数据
                InspectionDataStore.AddRecord(
                    cameraNo,
                    halconResult.ResultCode,
                    halconResult.MaxDeviation,
                    halconResult.MeanZLeft,
                    halconResult.MeanZRight,
                    halconResult.TiltDiff,
                    halconResult.DevLRegion,
                    halconResult.DevRRegion,
                    halconResult.MaxDeviation,
                    halconResult.ResultText,
                    currentImagePath);

                AppendLog(
                    $"拍照{cameraNo}处理完成：{halconResult.ResultText}，当前图片={currentImagePath}");
            }
            catch (Exception ex)
            {
                AppendLog($"自动拍照{cameraNo}流程异常：{ex.Message}");
                MessageBox.Show($"自动拍照{cameraNo}流程异常：{ex.Message}");
            }
            finally
            {
                _autoPhotoLock.Release();
            }
        }

        private void WriteOkToBothResultAddresses()
        {
            WriteResultToBothAddresses(1);
        }

        private void WriteResultToBothAddresses(short resultCode)
        {
            _autoPlcService.WriteSingleRegister(Photo1ResultAddress, resultCode);
            _autoPlcService.WriteSingleRegister(Photo2ResultAddress, resultCode);
        }

        private void ShowHalconDisabledResult()
        {
            txtMeanZLeft.Text = "--";
            txtMeanZRight.Text = "--";
            txtTiltDiff.Text = "--";
            txtDevLRegion.Text = "--";
            txtDevRRegion.Text = "--";
            txtMaxDeviation.Text = "--";
            txtHalconResult.Text = "视觉算法未使能";
            txtHalconResult.Foreground = Brushes.Orange;
        }


        private void BtnStopPlcAuto_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _plcListenCts?.Cancel();
                _plcListenCts?.Dispose();
                _plcListenCts = null;

                txtAutoPlcState.Text = _autoPlcService.IsConnected
                    ? "PLC自动：已连接，未监听"
                    : "PLC自动：未连接";

                txtAutoPlcState.Foreground = _autoPlcService.IsConnected
                    ? Brushes.DeepSkyBlue
                    : Brushes.Red;

                AppendLog("PLC自动监听已停止");
            }
            catch (Exception ex)
            {
                AppendLog($"停止PLC自动监听异常：{ex.Message}");
            }
        }
        private void BtnStartCapture_Click(object sender, RoutedEventArgs e)
        {
            if (!InitCameraSdk())
                return;

            try
            {
                if (!_cameraWrap!.GetCameraIsConnect())
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!_cameraWrap.SetUserOperatorMode((byte)DATAMODE.DATAMODE_GRAB))
                {
                    MessageBox.Show("切换数据采集模式失败");
                    return ;
                }

                if (!_cameraWrap.StartCapture())
                {
                    MessageBox.Show("开始采集失败", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    AppendLog("开始采集失败");
                    return;
                }

                _timerUpdatePointCloud.Start();
                txtGrabInfo.Text = "已开始采集，等待触发抓图";
                UpdateCameraInfo();
                AppendLog("开始采集成功");
            }
            catch (Exception ex)
            {
                AppendLog($"开始采集异常: {ex.Message}");
            }
        }

        private void BtnTrigger_Click(object sender, RoutedEventArgs e)
        {
            if (!InitCameraSdk())
                return;

            try
            {
                if (!_cameraWrap!.GetCameraIsConnect())
                {
                    MessageBox.Show("相机未连接", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                if (!_cameraWrap.GetCameraIsGrab())
                {
                    MessageBox.Show("请先开始采集", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                imgDepth.Source = null;
                Init3DView();

                if (!_cameraWrap.SendGrabSignalToCamera())
                {
                    MessageBox.Show("触发抓图失败", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    AppendLog("触发抓图失败");
                    return;
                }

                AppendLog("已发送一次抓图触发");
            }
            catch (Exception ex)
            {
                AppendLog($"触发抓图异常: {ex.Message}");
            }
        }

        private void BtnStopCapture_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                if (_cameraWrap == null)
                    return;

                if (!_cameraWrap.StopCapture())
                {
                    MessageBox.Show("停止采集失败", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    AppendLog("停止采集失败");
                    return;
                }

                _timerUpdatePointCloud.Stop();
                txtGrabInfo.Text = "已停止采集";
                AppendLog("停止采集成功");
            }
            catch (Exception ex)
            {
                AppendLog($"停止采集异常: {ex.Message}");
            }
        }

        private void BtnCameraParam_Click(object sender, RoutedEventArgs e)
        {
            if (!InitCameraSdk())
                return;

            try
            {
                if (!_cameraWrap!.GetCameraIsConnect())
                {
                    MessageBox.Show("请先连接相机", "提示", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                _cameraWrap.StopCapture();

                var win = new CameraParamWindow(_cameraWrap);
                win.Owner = Window.GetWindow(this);
                win.ShowDialog();

                UpdateCameraInfo();
                AppendLog("已打开相机参数窗口");
            }
            catch (Exception ex)
            {
                AppendLog($"打开相机参数窗口异常: {ex.Message}");
            }
        }

        /*halcon测试*/
        private void BtnTestHalcon_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dialog = new OpenFileDialog
                {
                    Title = "选择HALCON测试图片",
                    Filter = "图像文件|*.png;*.jpg;*.jpeg;*.bmp;*.tif;*.tiff|所有文件|*.*"
                };

                if (dialog.ShowDialog() != true)
                    return;

                HOperatorSet.ReadImage(out HObject image, dialog.FileName);

                RunHalconAlgorithm(image);

                image.Dispose();
            }
            catch (Exception ex)
            {
                AppendLog($"HALCON测试失败：{ex.Message}");
                MessageBox.Show($"HALCON测试失败：{ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        

        private void RunHalconAlgorithm(HObject image)
        {
            try
            {
                // 固定 HALCON 引擎资源路径
                HalconEngine_Test.ResourcePath =
                    System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res_HalconEngine_Test");

                HalconEngine_Test.Bearing_inspection(
                    image,
                    out HTuple meanZLeft,
                    out HTuple meanZRight,
                    out HTuple tiltDiff,
                    out HTuple devLRegion,
                    out HTuple devRRegion,
                    out HTuple maxDeviation,
                    out HTuple result);

                ShowHalconResult(
                    meanZLeft,
                    meanZRight,
                    tiltDiff,
                    devLRegion,
                    devRRegion,
                    maxDeviation,
                    result);
            }
            catch (Exception ex)
            {
                AppendLog($"HALCON算法执行异常：{ex.Message}");
                MessageBox.Show($"HALCON算法执行异常：{ex.Message}", "HALCON错误", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /*显示图片的七个变量*/
        private void ShowHalconResult(
    HTuple meanZLeft,
    HTuple meanZRight,
    HTuple tiltDiff,
    HTuple devLRegion,
    HTuple devRRegion,
    HTuple maxDeviation,
    HTuple result)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                txtMeanZLeft.Text = FormatHalconValue(meanZLeft);
                txtMeanZRight.Text = FormatHalconValue(meanZRight);
                txtTiltDiff.Text = FormatHalconValue(tiltDiff);
                txtDevLRegion.Text = FormatHalconValue(devLRegion);
                txtDevRRegion.Text = FormatHalconValue(devRRegion);
                txtMaxDeviation.Text = FormatHalconValue(maxDeviation);

                string resultText = result.ToString();
                txtHalconResult.Text = resultText;

                if (resultText.Contains("OK", StringComparison.OrdinalIgnoreCase))
                {
                    txtHalconResult.Foreground = Brushes.LimeGreen;
                }
                else
                {
                    txtHalconResult.Foreground = Brushes.Red;
                }

                AppendLog($"HALCON检测完成：{resultText}");
            }));
        }

        private void ShowHalconResult(HalconInspectionResult result)
        {
            Dispatcher.BeginInvoke(new Action(() =>
            {
                txtMeanZLeft.Text = result.MeanZLeft.ToString("F6");
                txtMeanZRight.Text = result.MeanZRight.ToString("F6");
                txtTiltDiff.Text = result.TiltDiff.ToString("F6");
                txtDevLRegion.Text = result.DevLRegion.ToString("F6");
                txtDevRRegion.Text = result.DevRRegion.ToString("F6");
                txtMaxDeviation.Text = result.MaxDeviation.ToString("F6");

                txtHalconResult.Text = result.ResultText;

                txtHalconResult.Foreground = result.ResultCode == 1
                    ? Brushes.LimeGreen
                    : Brushes.Red;

                AppendLog($"HALCON检测结果已刷新：{result.ResultText}");
            }));
        }

        //接受数据格式化方法
        private string FormatHalconValue(HTuple value)
        {
            if (value == null || value.Length == 0)
                return "--";

            try
            {
                if (value.Length == 1)
                {
                    double d = value.D;
                    return d.ToString("F6");
                }

                return value.ToString();
            }
            catch
            {
                return value.ToString();
            }
        }

        private void UpdateCameraInfo()
        {
            if (_cameraWrap == null)
                return;

            string capMsg = "";
            string tranMsg = "";
            string dataTypeMsg = "";

            if (_cameraWrap.GetCaptureMode() == Convert.ToByte(CAPMODE.CAPMODE_ENC))
                capMsg = "编码器";
            else if (_cameraWrap.GetCaptureMode() == Convert.ToByte(CAPMODE.CAPMODE_PULSE))
                capMsg = "外部触发";
            else if (_cameraWrap.GetCaptureMode() == Convert.ToByte(CAPMODE.CAPMODE_AUTO))
                capMsg = "内部触发";

            if (_cameraWrap.GetTransMode() == Convert.ToByte(TRANSMODE.TRANSMODE_BATCH_SW))
                tranMsg = "软件批处理";
            else if (_cameraWrap.GetTransMode() == Convert.ToByte(TRANSMODE.TRANSMODE_BATCH_HW))
                tranMsg = "硬件批处理";
            else if (_cameraWrap.GetTransMode() == Convert.ToByte(TRANSMODE.TRANSMODE_CONTINOUS_SW))
                tranMsg = "连续软件触发";
            else if (_cameraWrap.GetTransMode() == Convert.ToByte(TRANSMODE.TRANSMODE_CONTINOUS_HW))
                tranMsg = "连续硬件触发";
            else if (_cameraWrap.GetTransMode() == Convert.ToByte(TRANSMODE.TRANSMODE_CONTINOUS))
                tranMsg = "连续";

            if (_cameraWrap.GetDataType() == Convert.ToByte(DATATYPE.DATATYPE_PROFILE))
                dataTypeMsg = "高度";
            else if (_cameraWrap.GetDataType() == Convert.ToByte(DATATYPE.DATATYPE_PROFILE_GRAY))
                dataTypeMsg = "高度+灰度";

            txtCameraInfo.Text =
                $"触发模式：{capMsg}\n" +
                $"抓取模式：{tranMsg}\n" +
                $"数据输出：{dataTypeMsg}\n" +
                $"抓取线数：{_cameraWrap.GetBatchGrabNumber()}\n" +
                $"回调线数：{_cameraWrap.GetPointCloudCallBackNum()}\n" +
                $"曝光：{_cameraWrap.GetExpo()}\n" +
                $"帧率：{_cameraWrap.GetFrame()}";

            txtConnectState.Text = _cameraWrap.GetCameraIsConnect() ? "已连接" : "未连接";
            txtConnectState.Foreground = _cameraWrap.GetCameraIsConnect() ? Brushes.Green : Brushes.Red;
        }

        private unsafe void OnImage(IntPtr pGrayBuf, SG_IMGDATA_PARAM param, IntPtr pOwner)
        {
            if (pGrayBuf == IntPtr.Zero || _cameraWrap == null)
                return;

            _cameraWrap.releaseImageBuf(pGrayBuf);
        }

        private unsafe void OnImageProfile(IntPtr pGrayBuf, IntPtr pProfile, SG_IMGDATA_PARAM param, IntPtr pOwner)
        {
            if (pGrayBuf == IntPtr.Zero || _cameraWrap == null)
                return;

            _cameraWrap.releaseImageBuf(pGrayBuf);
        }

        private unsafe void OnDepth(IntPtr pBuf, IntPtr pGrayBuf, SG_DEPTHDATA_PARAM param, IntPtr pOwner)
        {
            if (pBuf == IntPtr.Zero)
                return;

            int pointCount = 0;

            lock (_lockForImageRef)
            {
                pointCount = param._iCapturedProfileLineNum * param._iPointNumPerLine;
                if (pointCount <= 0)
                    return;

                if (_depthParam._iBufSize != param._iBufSize || _pHeightData == IntPtr.Zero)
                {
                    _depthParam = param;

                    if (_pHeightData != IntPtr.Zero)
                        Marshal.FreeHGlobal(_pHeightData);

                    _pHeightData = Marshal.AllocHGlobal(pointCount * sizeof(float));
                }
                else
                {
                    _depthParam = param;
                }

                float* sourceFloat = (float*)pBuf.ToPointer();
                float* heightData = (float*)_pHeightData.ToPointer();

                int bufLenBatch = pointCount * 3;
                int index = 0;

                for (int j = 2; j < bufLenBatch; j += 3)
                {
                    heightData[index++] = sourceFloat[j];
                }

                _latestWidth = param._iPointNumPerLine;
                _latestHeight = param._iCapturedProfileLineNum;
                _latestXSpace = param._fXSpace;
                _latestYSpace = param._fYSpace;

                _latestZValues = new float[pointCount];
                Marshal.Copy(_pHeightData, _latestZValues, 0, pointCount);

                _hasFreshImage = true;
                if (_grabWaiter != null)
                {
                    // 自动拍照流程正在等待完整图。
                    // 目标高度优先取相机回调里的 _iWantCaptureProfileLineNum，
                    // 因此相机软件里把抓取线数从1600改到1700后，上位机会自动跟随。
                    int targetHeight = GetTargetCaptureHeight(param);

                    if (_latestHeight >= targetHeight)
                    {
                        int actualWidth = _latestWidth;
                        int actualHeight = _latestHeight;

                        // 保存实际收到的完整高度，不再裁剪成旧版320高度。
                        int targetCount = actualWidth * actualHeight;
                        float[] fixedZValues = new float[targetCount];

                        Array.Copy(
                            _latestZValues,
                            fixedZValues,
                            Math.Min(fixedZValues.Length, _latestZValues.Length));

                        _grabWaiter.TrySetResult(new CameraFrameSnapshot
                        {
                            ZValues = fixedZValues,
                            Width = actualWidth,
                            Height = actualHeight,
                            XSpace = _latestXSpace,
                            YSpace = _latestYSpace,
                            Param = param
                        });

                        AppendLog($"收到完整物料3D图：{actualWidth}×{actualHeight}");
                    }
                    else
                    {
                        AppendLog($"收到非完整图，暂不处理：{_latestWidth}×{_latestHeight}，目标高度={targetHeight}");
                    }
                }


                /*触发拍照日志*/
                if (_grabWaiter != null)
                {
                    AppendLog($"OnDepth收到自动拍照图像：{_latestWidth} × {_latestHeight}");
                }

            }

            int showPointCount = pointCount;

            Dispatcher.BeginInvoke(new Action(() =>
            {
                txtGrabInfo.Text =
                    $"抓取线数：{param._iCapturedProfileLineNum}\n" +
                    $"每线点数：{param._iPointNumPerLine}\n" +
                    $"目标线数：{param._iWantCaptureProfileLineNum}\n" +
                    $"缓冲区大小：{param._iBufSize}\n" +
                    $"丢包数：{param._iLostPacketCount}\n" +
                    $"X间距：{param._fXSpace:F6}\n" +
                    $"Y间距：{param._fYSpace:F6}\n" +
                    $"编码器计数：{param._uiEncoderCount}";

                txtDepthImageInfo.Text =
                    $"高度图：{param._iPointNumPerLine} × {param._iCapturedProfileLineNum}，丢包：{param._iLostPacketCount}";

                txtPointCloudInfo.Text =
                    $"三维点云：点数 {showPointCount}，X间距 {param._fXSpace:F6}，Y间距 {param._fYSpace:F6}";
            }));
        }

        private int GetTargetCaptureHeight(SG_DEPTHDATA_PARAM param)
        {
            // 相机返回的目标线数可信时使用它；否则用320兜底，避免目标值为0时永远等不到完整图。
            return param._iWantCaptureProfileLineNum > 0
                ? param._iWantCaptureProfileLineNum
                : FallbackExpectedImageHeight;
        }

        private void TimerUpdatePointCloud_Tick(object? sender, EventArgs e)
        {
            lock (_lockForImageRef)
            {
                if (!_hasFreshImage)
                    return;

                if (chkShowImage.IsChecked != true)
                {
                    _hasFreshImage = false;
                    return;
                }

                if (_pHeightData != IntPtr.Zero)
                {
                    imgDepth.Source = CreateDepthBitmap(_pHeightData, _depthParam);
                }

                Update3DPointCloud();

                _hasFreshImage = false;
            }
        }

        private unsafe BitmapSource? CreateDepthBitmap(IntPtr pDepthBuf, SG_DEPTHDATA_PARAM param)
        {
            if (pDepthBuf == IntPtr.Zero || param._iPointNumPerLine <= 0 || param._iCapturedProfileLineNum <= 0)
                return null;

            int width = param._iPointNumPerLine;
            int height = param._iCapturedProfileLineNum;
            int count = width * height;

            float[] zValues = new float[count];
            Marshal.Copy(pDepthBuf, zValues, 0, count);

            List<float> validZ = zValues.Where(z => z > 0.00001f).ToList();

            float zMin = 0;
            float zMax = 0;
            if (validZ.Count > 0)
            {
                zMin = validZ.Min();
                zMax = validZ.Max();
            }

            byte[] pixels = new byte[count];
            for (int i = 0; i < count; i++)
            {
                pixels[i] = Rescale(zValues[i], zMax, zMin);
            }

            return BitmapSource.Create(
                width,
                height,
                96,
                96,
                PixelFormats.Gray8,
                BitmapPalettes.Gray256,
                pixels,
                width);
        }

        private byte Rescale(float value, float max, float min)
        {
            if (value < min)
                return 0;
            if (value > max)
                return 255;
            if (Math.Abs(max - min) < 0.00001f)
                return 0;

            return (byte)(((value - min) / (max - min)) * 255);
        }

        private void Init3DView()
        {
            viewport3D.Children.Clear();

            _camera3D = new PerspectiveCamera
            {
                Position = new Point3D(0, -_cameraDistance, _cameraDistance),
                LookDirection = new Vector3D(0, _cameraDistance, -_cameraDistance),
                UpDirection = new Vector3D(0, 0, 1),
                FieldOfView = 45
            };

            viewport3D.Camera = _camera3D;

            viewport3D.Children.Add(new ModelVisual3D
            {
                Content = new DirectionalLight
                {
                    Color = Colors.White,
                    Direction = new Vector3D(-1, -1, -2)
                }
            });
        }

        private void Update3DPointCloud()
        {
            if (_latestZValues.Length == 0 || _latestWidth <= 1 || _latestHeight <= 1)
                return;

            viewport3D.Children.Clear();

            // 重新加灯光
            viewport3D.Children.Add(new ModelVisual3D
            {
                Content = new AmbientLight(Colors.White)
            });

            int maxCols = 220;
            int maxRows = 160;

            int stepX = Math.Max(1, _latestWidth / maxCols);
            int stepY = Math.Max(1, _latestHeight / maxRows);

            int cols = _latestWidth / stepX;
            int rows = _latestHeight / stepY;

            if (cols <= 1 || rows <= 1)
                return;

            float zMin = float.MaxValue;
            float zMax = float.MinValue;

            foreach (float z in _latestZValues)
            {
                if (!float.IsNaN(z) && !float.IsInfinity(z) && z > 0.00001f)
                {
                    zMin = Math.Min(zMin, z);
                    zMax = Math.Max(zMax, z);
                }
            }

            if (zMin == float.MaxValue || zMax <= zMin)
                return;

            double realWidth = _latestWidth * _latestXSpace;
            double realHeight = _latestHeight * _latestYSpace;
            double xyMax = Math.Max(realWidth, realHeight);

            double zRange = zMax - zMin;

            // 关键：Z方向显示放大，否则3D图会非常扁，看起来像黑屏
            double zDisplayScale = xyMax * 0.35 / zRange;
            if (double.IsNaN(zDisplayScale) || double.IsInfinity(zDisplayScale) || zDisplayScale <= 0)
                zDisplayScale = 1;

            var mesh = new MeshGeometry3D();

            for (int r = 0; r < rows; r++)
            {
                for (int c = 0; c < cols; c++)
                {
                    int srcX = c * stepX;
                    int srcY = r * stepY;
                    int index = srcY * _latestWidth + srcX;

                    if (index < 0 || index >= _latestZValues.Length)
                        continue;

                    float zRaw = _latestZValues[index];

                    if (float.IsNaN(zRaw) || float.IsInfinity(zRaw) || zRaw <= 0.00001f)
                        zRaw = zMin;

                    double x = (srcX - _latestWidth / 2.0) * _latestXSpace;
                    double y = (srcY - _latestHeight / 2.0) * _latestYSpace;

                    // 关键：Z减去最小值，再按比例放大
                    double z = (zRaw - zMin) * zDisplayScale;

                    mesh.Positions.Add(new Point3D(x, y, z));
                }
            }

            for (int r = 0; r < rows - 1; r++)
            {
                for (int c = 0; c < cols - 1; c++)
                {
                    int p0 = r * cols + c;
                    int p1 = r * cols + c + 1;
                    int p2 = (r + 1) * cols + c;
                    int p3 = (r + 1) * cols + c + 1;

                    mesh.TriangleIndices.Add(p0);
                    mesh.TriangleIndices.Add(p2);
                    mesh.TriangleIndices.Add(p1);

                    mesh.TriangleIndices.Add(p1);
                    mesh.TriangleIndices.Add(p2);
                    mesh.TriangleIndices.Add(p3);
                }
            }

            var materialGroup = new MaterialGroup();

            // 自发光材质，避免黑屏
            materialGroup.Children.Add(new DiffuseMaterial(new SolidColorBrush(Color.FromRgb(0, 180, 255))));
            materialGroup.Children.Add(new EmissiveMaterial(new SolidColorBrush(Color.FromRgb(0, 120, 220))));

            var geometry = new GeometryModel3D
            {
                Geometry = mesh,
                Material = materialGroup,
                BackMaterial = materialGroup
            };

            _rotateX = new AxisAngleRotation3D(new Vector3D(1, 0, 0), 60);
            _rotateY = new AxisAngleRotation3D(new Vector3D(0, 0, 1), 0);

            var transformGroup = new Transform3DGroup();
            transformGroup.Children.Add(new RotateTransform3D(_rotateX));
            transformGroup.Children.Add(new RotateTransform3D(_rotateY));

            geometry.Transform = transformGroup;

            viewport3D.Children.Add(new ModelVisual3D
            {
                Content = geometry
            });

            // 关键：根据点云大小自动调整相机距离
            double displayZHeight = zRange * zDisplayScale;
            double modelSize = Math.Max(xyMax, displayZHeight);

            _cameraDistance = Math.Max(20, modelSize * 2.8);

            _camera3D = new PerspectiveCamera
            {
                Position = new Point3D(0, -_cameraDistance, _cameraDistance * 0.75),
                LookDirection = new Vector3D(0, _cameraDistance, -_cameraDistance * 0.75),
                UpDirection = new Vector3D(0, 0, 1),
                FieldOfView = 45
            };

            viewport3D.Camera = _camera3D;
        }

        private void Viewport3D_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            _isDragging3D = true;
            _lastMousePoint = e.GetPosition(viewport3D);
            viewport3D.CaptureMouse();
        }

        private void Viewport3D_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            _isDragging3D = false;
            viewport3D.ReleaseMouseCapture();
        }

        private void Viewport3D_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isDragging3D || _rotateX == null || _rotateY == null)
                return;

            Point current = e.GetPosition(viewport3D);
            Vector delta = current - _lastMousePoint;

            _rotateY.Angle += delta.X * 0.5;
            _rotateX.Angle += delta.Y * 0.5;

            _lastMousePoint = current;
        }

        private void Viewport3D_MouseWheel(object sender, MouseWheelEventArgs e)
        {
            if (_camera3D == null)
                return;

            double factor = e.Delta > 0 ? 0.85 : 1.15;

            _cameraDistance *= factor;
            _cameraDistance = Math.Max(5, Math.Min(5000, _cameraDistance));

            _camera3D.Position = new Point3D(0, -_cameraDistance, _cameraDistance * 0.75);
            _camera3D.LookDirection = new Vector3D(0, _cameraDistance, -_cameraDistance * 0.75);
        }

        private void AppendLog(string message)
        {
            if (_isCleaningUp)
            {
                try
                {
                    _logService.Write(message);
                }
                catch
                {
                }
                return;
            }

            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke(new Action(() => AppendLog(message)));
                return;
            }

            string line = $"{DateTime.Now:HH:mm:ss} {message}";
            txtLog.AppendText(line + Environment.NewLine);
            txtLog.ScrollToEnd();

            _logService.Write(message);
        }

        public void ShutdownResources()
        {
            if (_isCleaningUp)
                return;

            _isCleaningUp = true;

            try
            {
                // 先停止PLC监听，避免退出过程中后台线程继续访问PLC或UI。
                _plcListenCts?.Cancel();
            }
            catch
            {
            }

            try
            {
                _plcListenCts?.Dispose();
                _plcListenCts = null;
            }
            catch
            {
            }

            try
            {
                _autoPlcService.Disconnect();
            }
            catch
            {
            }

            try
            {
                // 停止界面刷新定时器，防止窗口销毁后仍然刷新3D/高度图控件。
                _timerUpdatePointCloud.Stop();
            }
            catch
            {
            }

            try
            {
                // 相机SDK通常会启动非托管采集线程，退出前必须先停止采集。
                _cameraWrap?.StopCapture();
            }
            catch
            {
            }

            try
            {
                // 显式关闭相机连接，避免相机SDK后台连接导致进程残留。
                _cameraWrap?.CloseCamera();
            }
            catch
            {
            }

            try
            {
                // 释放自己申请的非托管高度图缓存。
                if (_pGrayData != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_pGrayData);
                    _pGrayData = IntPtr.Zero;
                }

                if (_pHeightData != IntPtr.Zero)
                {
                    Marshal.FreeHGlobal(_pHeightData);
                    _pHeightData = IntPtr.Zero;
                }
            }
            catch
            {
            }

            try
            {
                // 相机SDK官方要求软件退出时调用反初始化。
                CameraWrapImpl.LibUnInit();
            }
            catch
            {
            }
            try
            {
                // 如果退出时自动拍照还在等待OnDepth，取消等待，避免异步任务挂住。
                _grabWaiter?.TrySetCanceled();
                _grabWaiter = null;
            }
            catch
            {
            }
        }

        private void CameraPage_Unloaded(object sender, RoutedEventArgs e)
        {
            ShutdownResources();
        }
    }
}
