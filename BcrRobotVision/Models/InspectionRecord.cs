using System;

namespace BcrRobotVision.Models
{
    public class InspectionRecord
    {
        public int Index { get; set; }
        public DateTime Time { get; set; } = DateTime.Now;
        public string Mode { get; set; } = "全部正面";
        public int CameraNo { get; set; }

        /// <summary>
        /// 1=OK，2=NG
        /// </summary>
        public int ResultCode { get; set; }

        public string ResultText => ResultCode == 1 ? "OK" : "NG";

        public double MeasureValue { get; set; }

        public double UpperLimit { get; set; } = 10.20;
        public double LowerLimit { get; set; } = 9.80;

        public double MeanZLeft { get; set; }
        public double MeanZRight { get; set; }
        public double TiltDiff { get; set; }
        public double DevLRegion { get; set; }
        public double DevRRegion { get; set; }
        public double MaxDeviation { get; set; }

        public string HalconResult { get; set; } = "";
        public string ImagePath { get; set; } = "";
    }
}