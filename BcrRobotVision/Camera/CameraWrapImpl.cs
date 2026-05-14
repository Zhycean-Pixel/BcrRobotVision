using Camera;
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using static Camera.CameraWrapper;

namespace BcrRobotVision.Camera
{

    public class CameraWrapImpl
    {
        protected CameraSyn m_cameraObj = null;
        Dictionary<PARAMID, CAM_PARAM_EX> m_CamParams = new Dictionary<PARAMID, CAM_PARAM_EX>();
        SGEXPORT_ROI m_curRoi;
        SGEXPORT_MODE m_curMode;
        SGEXPORT_CAMCONFIG m_curCfg;
        SGEXPORT_PRODUCTINFO m_curProctInfo;
        private static double GetParasVal(CAM_PARAM_EX cAM_PARAM)
        {
            switch (cAM_PARAM.valueType)
            {
                case VALUETYPE.VT_BYTE8:
                    return cAM_PARAM._ucValue;
                case VALUETYPE.VT_SHORT16:
                    return cAM_PARAM._usValue;
                case VALUETYPE.VT_INT32:
                    return cAM_PARAM._uiValue;
                case VALUETYPE.VT_FLOAT:
                    return cAM_PARAM._ufValue;
                case VALUETYPE.VT_DOUBLE:
                    return cAM_PARAM._udValue;
                default:
                    return 0;
            }
        }
        private static CAM_PARAM_EX SetParamVal(CAM_PARAM_EX param, double val)
        {
            CAM_PARAM_EX tmp = param;
            switch (tmp.valueType)
            {
                case VALUETYPE.VT_BYTE8:
                    tmp._ucValue = (byte)val;
                    break;
                case VALUETYPE.VT_SHORT16:
                    tmp._usValue = (ushort)val;
                    break;
                case VALUETYPE.VT_INT32:
                    tmp._uiValue = (uint)val;
                    break;
                case VALUETYPE.VT_FLOAT:
                    tmp._ufValue = (float)val;
                    break;
                case VALUETYPE.VT_DOUBLE:
                    tmp._udValue = (double)val;
                    break;
            }
            return tmp;
        }
        private bool GetAndSetParams(PARAMID[] vecPARAMIDs, double[] vecValues)
        {
            if (vecPARAMIDs.Length <= 0 || (vecPARAMIDs.Length != vecValues.Length))
            {
                return false;
            }
            int idataCount = vecPARAMIDs.Length;
            CAM_PARAM_EX[] vecCamParam = new CAM_PARAM_EX[idataCount];
            SGERROR_CODE ret = m_cameraObj.GetCamParamsSyn(vecPARAMIDs, out vecCamParam);
            if (ret != SGERROR_CODE.SGERR_OK)
            {
                return false;
            }
            if (idataCount != 0)
            {
                for (int i = 0; i < idataCount; i++)
                {
                    if (vecCamParam[i].paramId == vecPARAMIDs[i] && vecCamParam[i].isSupport)
                    {
                        if (GetParasVal(vecCamParam[i]) != vecValues[i])
                        {
                            vecCamParam[i] = SetParamVal(vecCamParam[i], vecValues[i]);
                        }
                    }
                }
                int failCount = vecCamParam.Length;
                SET_PARAM_RSP[] msgs = new SET_PARAM_RSP[failCount];
                ret = m_cameraObj.SetCamParamsSyn(vecCamParam, out msgs);
                if (ret == SGERROR_CODE.SGERR_OK)
                {
                    return true;
                }
                else if (ret == SGERROR_CODE.SGERR_CAM_RETURN_ERROR)
                {
                    //多个参数设置返回失败时，可以解析SET_PARAM_RSP信息		
                    //根据_usParamId判断是哪个参数设置错误，_sResultCode在SGERROR_CODE查看错误码类型
                    //When multiple parameter settings fail to return, SET-PARAM-RSP information can be parsed
                    //Determine which parameter setting is incorrect based on the _usParamId, and check the error code type in SGERROR-CODE for the _sResultCode
                    Console.WriteLine($"ParamId:{msgs[0]._usParamId},ErrorCode:{msgs[0]._sResultCode}");
                }
            }
            return false;
        }
        public CameraWrapImpl()
        {
            m_cameraObj = new CameraSyn();
        }
        public static bool LibInit()
        {
            return SgLibInit() == SGERROR_CODE.SGERR_OK;
        }
        // 软件退出时，必须调用 LibUnInit
        public static bool LibUnInit()
        {
            return CameraSyn.LibRelease() == SGERROR_CODE.SGERR_OK;
        }
        public static IntPtr DetectCamera()
        {
            return SgDetectNetCameras();
        }
        public void SG_CAP_DEPTH_CB(Action<IntPtr, IntPtr, CameraWrapper.SG_DEPTHDATA_PARAM, IntPtr> pCallback)
        {
            m_cameraObj.DepthEvent += pCallback;
        }
        public void SG_CAP_IMGROFILE_CB(Action<IntPtr, IntPtr, SG_IMGDATA_PARAM, IntPtr> pCallback)
        {
            m_cameraObj.ImageProfileEvent += pCallback;//调试图像回调，图像+轮廓，两种图像回调根据相机版本注册
        }
        public void SG_CAP_IMG_CB(Action<IntPtr, SG_IMGDATA_PARAM, IntPtr> pCallback)
        {
            m_cameraObj.ImageEvent += pCallback;//调试图像回调，单图像，两种图像回调根据相机版本注册
        }
        public void releaseImageBuf(IntPtr pBuf)
        {
            m_cameraObj.ReleaseImageBuffer(pBuf);
        }
        public bool Connect2Camera(string pLocalIp, string pRemoteIp)//相机连接 Camera connect
        {
            if (m_cameraObj.Connect2Camera(pLocalIp, pRemoteIp))
            {
                return SyncAllParams();
            }
            return false;
        }
        public bool SyncAllParams()
        {
            if (IsSupportParamId() == false)
            {
                if (!m_cameraObj.GetRoiAndMedia(ref m_curRoi))
                {
                    return false;
                }
                if (!m_cameraObj.GetCamMode(ref m_curMode))
                {
                    return false;
                }
                if (!m_cameraObj.GetCamConfig(ref m_curCfg))
                {
                    return false;
                }
                if (!m_cameraObj.GetCamProductInfos(ref m_curProctInfo))
                {
                    return false;
                }
            }
            else
            {
                //新相机初始化获取全部参数
                List<PARAMID> vecPARAMIDs = new List<PARAMID>{PARAMID.PID_ucCaptureMode,PARAMID.PID_ucUserOperatorMode,
                    PARAMID.PID_ucDataType,PARAMID.PID_ucTransMode,PARAMID.PID_uiBatchGrabNumber,PARAMID.PID_usFrame,PARAMID.PID_uiExpo };
                int readCount = vecPARAMIDs.Count;
                CAM_PARAM_EX[] vecCamParam = new CAM_PARAM_EX[readCount];
                SGERROR_CODE ret = m_cameraObj.GetCamParamsSyn(vecPARAMIDs.ToArray(), out vecCamParam);
                if (ret != SGERROR_CODE.SGERR_OK)
                {
                    return false;
                }
                for (int i = 0; i < vecCamParam.Length; i++)
                {
                    if (vecCamParam[i].paramId == PARAMID.PID_ucCaptureMode && vecCamParam[i].isSupport)
                    {
                        m_CamParams[PARAMID.PID_ucCaptureMode] = vecCamParam[i];
                    }
                    else if (vecCamParam[i].paramId == PARAMID.PID_ucUserOperatorMode && vecCamParam[i].isSupport)
                    {
                        m_CamParams[PARAMID.PID_ucUserOperatorMode] = vecCamParam[i];
                    }
                    else if (vecCamParam[i].paramId == PARAMID.PID_ucDataType && vecCamParam[i].isSupport)
                    {
                        m_CamParams[PARAMID.PID_ucDataType] = vecCamParam[i];
                    }
                    else if (vecCamParam[i].paramId == PARAMID.PID_ucTransMode && vecCamParam[i].isSupport)
                    {
                        m_CamParams[PARAMID.PID_ucTransMode] = vecCamParam[i];
                    }
                    else if (vecCamParam[i].paramId == PARAMID.PID_uiBatchGrabNumber && vecCamParam[i].isSupport)
                    {
                        m_CamParams[PARAMID.PID_uiBatchGrabNumber] = vecCamParam[i];
                    }
                    else if (vecCamParam[i].paramId == PARAMID.PID_usFrame && vecCamParam[i].isSupport)
                    {
                        m_CamParams[PARAMID.PID_usFrame] = vecCamParam[i];
                    }
                    else if (vecCamParam[i].paramId == PARAMID.PID_uiExpo && vecCamParam[i].isSupport)
                    {
                        m_CamParams[PARAMID.PID_uiExpo] = vecCamParam[i];
                    }
                }
            }

            return true;
        }
        public void CloseCamera()//关闭相机连接   Close camera connection
        {
            m_cameraObj.CloseCamera();
        }
        public bool IsSupportParamId()//判断是否为新相机  Determine if it is a new camera
        {
            return m_cameraObj.IsSupportParamId();
        }
        public bool GetCameraIsConnect()//获取相机连接状态 Get camera connection status
        {
            return m_cameraObj.ISCONNECT;
        }
        public bool GetCameraIsGrab()//获取相机采集状态 Obtain camera acquisition status
        {
            return m_cameraObj.ISSTARTGRAB;
        }

        public byte GetCaptureMode()
        {
            if (IsSupportParamId() == false)
            {
                return m_curMode._ucCaptureMode;
            }
            else
            {
                return (byte)GetParasVal(m_CamParams[PARAMID.PID_ucCaptureMode]);
            }
        }
        public byte GetUserOperatorMode()
        {
            if (IsSupportParamId() == false)
            {
                return m_curMode._ucUserOperatorMode;
            }
            else
            {
                return (byte)GetParasVal(m_CamParams[PARAMID.PID_ucUserOperatorMode]);
            }
        }
        public byte GetDataType()
        {
            if (IsSupportParamId() == false)
            {
                return m_curMode._ucDataType;
            }
            else
            {
                return (byte)GetParasVal(m_CamParams[PARAMID.PID_ucDataType]);
            }
        }
        public byte GetTransMode()
        {
            if (IsSupportParamId() == false)
            {
                return m_curMode._ucTransMode;
            }
            else
            {
                return (byte)GetParasVal(m_CamParams[PARAMID.PID_ucTransMode]);
            }
        }
        public uint GetBatchGrabNumber()
        {
            if (IsSupportParamId() == false)
            {
                return m_curMode._uiGrabNumber;
            }
            else
            {
                return (uint)GetParasVal(m_CamParams[PARAMID.PID_uiBatchGrabNumber]);
            }
        }
        public ushort GetFrame()
        {
            if (IsSupportParamId() == false)
            {
                return m_curCfg._usFrame;
            }
            else
            {
                return (ushort)GetParasVal(m_CamParams[PARAMID.PID_usFrame]);
            }
        }
        public uint GetExpo()
        {
            if (IsSupportParamId() == false)
            {
                return m_curCfg._uiExpo;
            }
            else
            {
                return (uint)GetParasVal(m_CamParams[PARAMID.PID_uiExpo]);
            }
        }
        public uint GetMaxFrame()
        {
            uint maxFrame = 0;
            if (IsSupportParamId() == false)
            {
                m_cameraObj.GetMaxFrame(ref maxFrame);
                if (GetFrame() > maxFrame)
                {
                    m_curCfg._usFrame = (ushort)maxFrame;
                }
                return maxFrame;
            }
            else
            {
                PARAMID[] ids = new PARAMID[1] { PARAMID.PID_usMaxFrame };
                CAM_PARAM_EX[] datas = new CAM_PARAM_EX[1];
                SGERROR_CODE ret = m_cameraObj.GetCamParamsSyn(ids, out datas);
                if (ret == SGERROR_CODE.SGERR_OK)
                {
                    maxFrame = datas[0]._usValue;
                    if (GetFrame() > maxFrame)
                    {
                        m_CamParams[PARAMID.PID_usFrame] = SetParamVal(m_CamParams[PARAMID.PID_usFrame], maxFrame);
                    }
                }
                return maxFrame;
            }
        }
        public int GetPointCloudCallBackNum()
        {
            int iNum = 0;
            m_cameraObj.GetPointCloudCallBackNum(ref iNum);
            return iNum;
        }
        unsafe public string GetEmbedVer()
        {
            if (IsSupportParamId() == false)
            {
                fixed (byte* ptr = m_curProctInfo._szEmbedVer)
                {
                    string EmbedVer = Marshal.PtrToStringAnsi(new IntPtr(ptr));
                    return EmbedVer;
                }
            }
            return "";
        }
        public bool SetCamParams(byte ucDataType, byte ucTransMode, uint uiBatchGrabNumber)
        {
            if (IsSupportParamId() == false)
            {
                SGEXPORT_MODE mode = m_curMode;
                mode._ucDataType = ucDataType;
                mode._ucTransMode = ucTransMode;
                mode._uiGrabNumber = uiBatchGrabNumber;
                if (m_cameraObj.SetCamMode(mode))
                {
                    m_curMode = mode;
                    return true;
                }
            }
            else
            {
                List<PARAMID> Params = new List<PARAMID> { PARAMID.PID_ucDataType, PARAMID.PID_ucTransMode, PARAMID.PID_uiBatchGrabNumber };
                List<double> Values = new List<double> { (double)ucDataType, (double)ucTransMode, (double)uiBatchGrabNumber };
                if (GetAndSetParams(Params.ToArray(), Values.ToArray()))
                {
                    m_CamParams[PARAMID.PID_ucDataType] = SetParamVal(m_CamParams[PARAMID.PID_ucDataType], (double)ucDataType);
                    m_CamParams[PARAMID.PID_ucTransMode] = SetParamVal(m_CamParams[PARAMID.PID_ucTransMode], (double)ucTransMode);
                    m_CamParams[PARAMID.PID_uiBatchGrabNumber] = SetParamVal(m_CamParams[PARAMID.PID_uiBatchGrabNumber], (double)uiBatchGrabNumber);
                    return true;
                }
            }
            return false;
        }
        public bool SetCaptureMode(byte ucCaptureMode)
        {
            if (IsSupportParamId() == false)
            {
                SGEXPORT_MODE mode = m_curMode;
                mode._ucCaptureMode = ucCaptureMode;
                if (m_cameraObj.SetCamMode(mode))
                {
                    m_curMode = mode;
                    return true;
                }
            }
            else
            {
                CAM_PARAM_EX param = new CAM_PARAM_EX();
                foreach (var item in m_CamParams)
                {
                    if (item.Key == PARAMID.PID_ucCaptureMode)
                    {
                        param = SetParamVal(item.Value, ucCaptureMode);
                    }
                }
                SGERROR_CODE ret = m_cameraObj.SetCamParamsSyn(new[] { param }, out SET_PARAM_RSP[] msgs);
                if (ret == SGERROR_CODE.SGERR_OK)
                {
                    m_CamParams[PARAMID.PID_ucCaptureMode] = param;
                    return true;
                }
            }
            return false;
        }
        public bool SetUserOperatorMode(byte ucUserOperatorMode)
        {
            if (IsSupportParamId() == false)
            {
                SGEXPORT_MODE mode = m_curMode;
                mode._ucUserOperatorMode = ucUserOperatorMode;
                if (m_cameraObj.SetCamMode(mode))
                {
                    m_curMode = mode;
                    return true;
                }
            }
            else
            {
                CAM_PARAM_EX param = new CAM_PARAM_EX();
                foreach (var item in m_CamParams)
                {
                    if (item.Key == PARAMID.PID_ucUserOperatorMode)
                    {
                        param = SetParamVal(item.Value, ucUserOperatorMode);
                    }
                }
                SGERROR_CODE ret = m_cameraObj.SetCamParamsSyn(new[] { param }, out SET_PARAM_RSP[] msgs);
                if (ret == SGERROR_CODE.SGERR_OK)
                {
                    m_CamParams[PARAMID.PID_ucUserOperatorMode] = param;
                    return true;
                }
            }
            return false;
        }
        public bool SetDataType(byte ucDataType)
        {
            if (IsSupportParamId() == false)
            {
                SGEXPORT_MODE mode = m_curMode;
                mode._ucDataType = ucDataType;
                if (m_cameraObj.SetCamMode(mode))
                {
                    m_curMode = mode;
                    return true;
                }
            }
            else
            {
                CAM_PARAM_EX param = new CAM_PARAM_EX();
                foreach (var item in m_CamParams)
                {
                    if (item.Key == PARAMID.PID_ucDataType)
                    {
                        param = SetParamVal(item.Value, ucDataType);
                    }
                }
                SGERROR_CODE ret = m_cameraObj.SetCamParamsSyn(new[] { param }, out SET_PARAM_RSP[] msgs);
                if (ret == SGERROR_CODE.SGERR_OK)
                {
                    m_CamParams[PARAMID.PID_ucDataType] = param;
                    return true;
                }
            }
            return false;
        }
        public bool SetTransMode(byte ucTransMode)
        {
            if (IsSupportParamId() == false)
            {
                SGEXPORT_MODE mode = m_curMode;
                mode._ucTransMode = ucTransMode;
                if (m_cameraObj.SetCamMode(mode))
                {
                    m_curMode = mode;
                    return true;
                }
            }
            else
            {
                CAM_PARAM_EX param = new CAM_PARAM_EX();
                foreach (var item in m_CamParams)
                {
                    if (item.Key == PARAMID.PID_ucTransMode)
                    {
                        param = SetParamVal(item.Value, ucTransMode);
                    }
                }
                SGERROR_CODE ret = m_cameraObj.SetCamParamsSyn(new[] { param }, out SET_PARAM_RSP[] msgs);
                if (ret == SGERROR_CODE.SGERR_OK)
                {
                    m_CamParams[PARAMID.PID_ucTransMode] = param;
                    return true;
                }
            }
            return false;
        }
        public bool SetBatchGrabNumber(uint uiBatchGrabNumber)
        {
            if (IsSupportParamId() == false)
            {
                SGEXPORT_MODE mode = m_curMode;
                mode._uiGrabNumber = uiBatchGrabNumber;
                if (m_cameraObj.SetCamMode(mode))
                {
                    m_curMode = mode;
                    return true;
                }
            }
            else
            {
                CAM_PARAM_EX param = new CAM_PARAM_EX();
                foreach (var item in m_CamParams)
                {
                    if (item.Key == PARAMID.PID_uiBatchGrabNumber)
                    {
                        param = SetParamVal(item.Value, uiBatchGrabNumber);
                    }
                }
                SGERROR_CODE ret = m_cameraObj.SetCamParamsSyn(new[] { param }, out SET_PARAM_RSP[] msgs);
                if (ret == SGERROR_CODE.SGERR_OK)
                {
                    m_CamParams[PARAMID.PID_uiBatchGrabNumber] = param;
                    return true;
                }
            }
            return false;
        }
        public bool SetFrame(ushort usFrame)
        {
            if (IsSupportParamId() == false)
            {
                SGEXPORT_CAMCONFIG cfg = m_curCfg;
                cfg._usFrame = usFrame;
                if (m_cameraObj.SetCamConfig(cfg))
                {
                    m_curCfg = cfg;
                    return true;
                }
            }
            else
            {
                CAM_PARAM_EX param = new CAM_PARAM_EX();
                foreach (var item in m_CamParams)
                {
                    if (item.Key == PARAMID.PID_usFrame)
                    {
                        param = SetParamVal(item.Value, usFrame);
                    }
                }
                SGERROR_CODE ret = m_cameraObj.SetCamParamsSyn(new[] { param }, out SET_PARAM_RSP[] msgs);
                if (ret == SGERROR_CODE.SGERR_OK)
                {
                    m_CamParams[PARAMID.PID_usFrame] = param;
                    return true;
                }
            }
            return false;
        }
        public bool SetExpo(uint uiExpo)
        {
            if (IsSupportParamId() == false)
            {
                SGEXPORT_CAMCONFIG cfg = m_curCfg;
                cfg._uiExpo = uiExpo;
                if (m_cameraObj.SetCamConfig(cfg))
                {
                    m_curCfg = cfg;
                    return true;
                }
            }
            else
            {
                CAM_PARAM_EX param = new CAM_PARAM_EX();
                foreach (var item in m_CamParams)
                {
                    if (item.Key == PARAMID.PID_uiExpo)
                    {
                        param = SetParamVal(item.Value, uiExpo);
                    }
                }
                SGERROR_CODE ret = m_cameraObj.SetCamParamsSyn(new[] { param }, out SET_PARAM_RSP[] msgs);
                if (ret == SGERROR_CODE.SGERR_OK)
                {
                    m_CamParams[PARAMID.PID_uiExpo] = param;
                    return true;
                }
            }
            return false;
        }
        public bool SetEncoderOrExternalCfg(byte ucEncFilter, uint uiEncPulseNumber, byte isLowPrecisionEncoder, ushort usFrame, float fEncoderLenPerPulse, float fEncoderMoveSpeed, byte ucEncoderAssistSetMethod)
        {
            if (IsSupportParamId() == false)
            {
                SGEXPORT_CAMCONFIG Cfg = m_curCfg;
                Cfg._encParam._ucEncFilter = ucEncFilter;
                Cfg._encParam._uiPulseNumber = uiEncPulseNumber;
                Cfg._encParam._isLowPrecisionEncoder = isLowPrecisionEncoder;
                Cfg._usEncoderExpectationFrame = usFrame;
                Cfg._fEncoderLenPerPulse = fEncoderLenPerPulse;
                Cfg._fEncoderThirdParam = fEncoderMoveSpeed;
                Cfg._ucEncoderAssistSetMethod = ucEncoderAssistSetMethod;
                if (m_cameraObj.SetCamConfig(Cfg))
                {
                    m_curCfg = Cfg;
                    return true;
                }
            }
            else
            {
                List<PARAMID> vecPARAMIDs = new List<PARAMID> { PARAMID.PID_ucEncFilter, PARAMID.PID_uiEncPulseNumber, PARAMID.PID_ucIsLowPrecisionEncoder, PARAMID.PID_usEncoderExpectationFrame, PARAMID.PID_fEncoderLenPerPulse, PARAMID.PID_fEncoderMoveSpeed, PARAMID.PID_ucEncoderAssistSetMethod };
                List<double> vecVals = new List<double> { ucEncFilter, uiEncPulseNumber, isLowPrecisionEncoder, usFrame, fEncoderLenPerPulse, fEncoderMoveSpeed, ucEncoderAssistSetMethod };
                return GetAndSetParams(vecPARAMIDs.ToArray(), vecVals.ToArray());
            }
            return false;
        }
        public bool SetYScaling(float fYScaling)
        {
            if (IsSupportParamId() == false)
            {
                SGEXPORT_CAMCONFIG Cfg = m_curCfg;
                Cfg._fYScaling = fYScaling;
                if (m_cameraObj.SetCamConfig(Cfg))
                {
                    m_curCfg = Cfg;
                    return true;
                }
            }
            else
            {
                List<PARAMID> vecPARAMIDs = new List<PARAMID> { PARAMID.PID_fYScaling };
                List<double> vecVals = new List<double> { (double)fYScaling };
                return GetAndSetParams(vecPARAMIDs.ToArray(), vecVals.ToArray());
            }
            return false;
        }
        public bool SetPointCloudCallBackNum(int iNum)
        {
            if (m_cameraObj.SetPointCloudCallBackNum(iNum) == SGERROR_CODE.SGERR_OK)
            {
                return true;
            }
            return false;
        }

        //相机采集和标定的一些操作，新旧相机一致  
        //Some operations for camera acquisition and calibration, consistent with old and new cameras
        public bool StartCapture()
        {
            return m_cameraObj.StartCapture();
        }
        public bool StopCapture()
        {
            return m_cameraObj.StopCapture();
        }
        public bool SendGrabSignalToCamera(bool bEndGrab = false)
        {
            return m_cameraObj.SendGrabSignalToCameraEx(bEndGrab);
        }
        // 倾斜标定功能（需要处于抓取模式下，并且已经执行过Start）
        //Tilt calibration function (needs to be in gripping mode and Start has already been executed)
        public bool SlantCalibrate(int iRegionNum, float fBegin1, float fEnd1, float fBegin2, float fEnd2, ref float fAngle)
        {
            return m_cameraObj.SlantCalibrate(iRegionNum, fBegin1, fEnd1, fBegin2, fEnd2, ref fAngle);
        }
        public bool GetSlantCalibrationInfo(ref bool bUseSlantCal, ref int iRegionNum, ref float fBegin1, ref float fEnd1, ref float fBegin2, ref float fEnd2)
        {
            return m_cameraObj.GetSlantCalibrationInfo(ref bUseSlantCal, ref iRegionNum, ref fBegin1, ref fEnd1, ref fBegin2, ref fEnd2);
        }

        public bool SetSlantCalibrationInfo(bool bUseSlantCal)
        {
            return m_cameraObj.SetSlantCalibrationInfo(bUseSlantCal);
        }
        public bool GetEncoderCount(ref uint uiEncoderCount)
        {
            return m_cameraObj.GetEncoderCount(ref uiEncoderCount, false);
        }
        public bool GetEncoderOrExternalParamsbySpeed(double dSpeed, int iFrame, double dSinglePulseLength, double dScanLength, int iSubNums, bool isExternalTri, ref byte ucEncFilter, ref uint uiPulseInterval, ref byte ucIsLowPrecisionEncoder, ref double dYScale, ref uint uiGrabNumber)
        {
            if (isExternalTri)
            {
                ucIsLowPrecisionEncoder = 1;
                return SgGetExternalParamsbySpeed(dSpeed, iFrame, dSinglePulseLength, dScanLength, iSubNums, ref ucEncFilter, ref uiPulseInterval, ref dYScale, ref uiGrabNumber) == SGERROR_CODE.SGERR_OK;
            }
            else
            {
                return SgGetEncoderParamsbySpeed(dSpeed, iFrame, dSinglePulseLength, dScanLength, iSubNums, ref ucEncFilter, ref uiPulseInterval, ref ucIsLowPrecisionEncoder, ref dYScale, ref uiGrabNumber) == SGERROR_CODE.SGERR_OK;
            }
        }
        public bool GetEncoderOrExternalParamsbyPulseInterval(uint uiPulseInterval, int iFrame, double dSinglePulseLength, double dScanLength, int iSubNums, bool isExternalTri, ref byte ucEncFilter, ref double dSpeed, ref byte ucIsLowPrecisionEncoder, ref double dYScale, ref uint uiGrabNumber)
        {
            if (isExternalTri)
            {
                ucIsLowPrecisionEncoder = 1;
                return SgGetExternalParamsbyPulseInterval(uiPulseInterval, iFrame, dSinglePulseLength, dScanLength, iSubNums, ref ucEncFilter, ref dSpeed, ref dYScale, ref uiGrabNumber) == SGERROR_CODE.SGERR_OK;
            }
            else
            {
                return SgGetEncoderParamsbyPulseInterval(uiPulseInterval, iFrame, dSinglePulseLength, dScanLength, iSubNums, ref ucEncFilter, ref dSpeed, ref ucIsLowPrecisionEncoder, ref dYScale, ref uiGrabNumber) == SGERROR_CODE.SGERR_OK;
            }
        }
        public bool GetEncoderOrExternalParamsbyYScale(double dYScale, int iFrame, double dSinglePulseLength, double dScanLength, int iSubNums, bool isExternalTri, ref byte ucEncFilter, ref double dSpeed, ref byte ucIsLowPrecisionEncoder, ref uint uiPulseInterval, ref uint uiGrabNumber)
        {
            if (isExternalTri)
            {
                ucIsLowPrecisionEncoder = 1;
                return SgGetExternalParamsbyYScale(dYScale, iFrame, dSinglePulseLength, dScanLength, iSubNums, ref ucEncFilter, ref dSpeed, ref uiPulseInterval, ref uiGrabNumber) == SGERROR_CODE.SGERR_OK;
            }
            else
            {
                return SgGetEncoderParamsbyYScale(dYScale, iFrame, dSinglePulseLength, dScanLength, iSubNums, ref ucEncFilter, ref dSpeed, ref ucIsLowPrecisionEncoder, ref uiPulseInterval, ref uiGrabNumber) == SGERROR_CODE.SGERR_OK;
            }
        }
    }
}
