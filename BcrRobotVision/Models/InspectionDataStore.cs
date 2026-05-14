using System;
using System.Collections.ObjectModel;
using System.Linq;
using BcrRobotVision.Models;

namespace BcrRobotVision.Services
{
    public static class InspectionDataStore
    {
        public static ObservableCollection<InspectionRecord> Records { get; } = new();

        public static int TotalCount => Records.Count;
        public static int OkCount => Records.Count(x => x.ResultCode == 1);
        public static int NgCount => Records.Count(x => x.ResultCode == 2);

        public static double YieldRate =>
            TotalCount == 0 ? 0 : OkCount * 100.0 / TotalCount;

        public static double Average =>
            Records.Count == 0 ? 0 : Records.Average(x => x.MeasureValue);

        public static double StdDev
        {
            get
            {
                if (Records.Count <= 1)
                    return 0;

                double avg = Average;
                double sum = Records.Sum(x => Math.Pow(x.MeasureValue - avg, 2));
                return Math.Sqrt(sum / (Records.Count - 1));
            }
        }

        public static double Cpk
        {
            get
            {
                if (Records.Count <= 1 || StdDev <= 0)
                    return 0;

                double usl = Records.Last().UpperLimit;
                double lsl = Records.Last().LowerLimit;
                double avg = Average;

                double cpu = (usl - avg) / (3 * StdDev);
                double cpl = (avg - lsl) / (3 * StdDev);

                return Math.Min(cpu, cpl);
            }
        }

        public static void AddRecord(
    int cameraNo,
    int resultCode,
    double measureValue,
    double meanZLeft,
    double meanZRight,
    double tiltDiff,
    double devLRegion,
    double devRRegion,
    double maxDeviation,
    string halconResult,
    string imagePath)
        {
            Records.Add(new InspectionRecord
            {
                Index = Records.Count + 1,
                Time = DateTime.Now,
                Mode = AppSession.CurrentMode.ToString(),
                CameraNo = cameraNo,
                ResultCode = resultCode,
                MeasureValue = measureValue,
                MeanZLeft = meanZLeft,
                MeanZRight = meanZRight,
                TiltDiff = tiltDiff,
                DevLRegion = devLRegion,
                DevRRegion = devRRegion,
                MaxDeviation = maxDeviation,
                HalconResult = halconResult,
                ImagePath = imagePath
            });
        }

        public static void AddRecord(int cameraNo, int resultCode, double measureValue)
        {
            Records.Add(new InspectionRecord
            {
                Index = Records.Count + 1,
                Time = DateTime.Now,
                Mode = AppSession.CurrentMode.ToString(),
                CameraNo = cameraNo,
                ResultCode = resultCode,
                MeasureValue = measureValue,

                // 手动PLC测试时没有HALCON七个数据，先默认给0
                MeanZLeft = 0,
                MeanZRight = 0,
                TiltDiff = 0,
                DevLRegion = 0,
                DevRRegion = 0,
                MaxDeviation = measureValue,

                HalconResult = resultCode == 1 ? "OK" : "NG",
                ImagePath = ""
            });
        }



    }
    }
