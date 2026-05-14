using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace BcrRobotVision.Camera
{
    class Common
    {
        public struct IpInfo
        {
            public string localIp;
            public string cameraIp;
        }

        public static IpInfo[] analysisIpInfos(string ips)
        {             
            string[] sArrayByAdapter = ips.Split(new[] { "###" }, StringSplitOptions.RemoveEmptyEntries);
            IpInfo[] allDectInfo = new IpInfo[sArrayByAdapter.Length];
            for (int i = 0; i < sArrayByAdapter.Length; i++)
            {
                string sItem = sArrayByAdapter[i];
                sItem = sItem.Trim();
                if (sItem.Length > 0)
                {
                    string[] sArrayTmp = sItem.Split(new[] { "@@@" }, StringSplitOptions.RemoveEmptyEntries);
                    string[] sSplitLocalAndCameras = sArrayTmp[0].Split(new[] { ";" }, StringSplitOptions.RemoveEmptyEntries);
                    if(sSplitLocalAndCameras.Length==2)
                    {
                        allDectInfo[i].localIp = sSplitLocalAndCameras[1];
                        string[] CameraIps = sSplitLocalAndCameras[0].Split(new[] { "(" }, StringSplitOptions.RemoveEmptyEntries);
                        allDectInfo[i].cameraIp = CameraIps[0];
                    }
                }
            }
            return allDectInfo;
        }
    }
}
