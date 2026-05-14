using System;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BcrRobotVision.Services;

namespace BcrRobotVision.Pages
{
    public partial class SpcReportPage : UserControl
    {
        public SpcReportPage()
        {
            InitializeComponent();
            RefreshReport();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshReport();
        }

        private void SpcCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawSpcChart();
        }

        private void RefreshReport()
        {
            var records = InspectionDataStore.Records.ToList();

            if (records.Count == 0)
            {
                txtCount.Text = "0";
                txtMean.Text = "0.000";
                txtSigma.Text = "0.000";
                txtCp.Text = "0.000";
                txtCpk.Text = "0.000";
                txtUsl.Text = "0.000";
                txtLsl.Text = "0.000";
                txtCpkJudge.Text = "暂无数据";
                txtCpkJudge.Foreground = Brushes.Gray;
                txtReport.Text = "当前没有检测数据，请先在PLC页面写入OK/NG测试数据。";
                DrawSpcChart();
                return;
            }

            double mean = records.Average(x => x.MeasureValue);
            double sigma = CalculateStdDev(records.Select(x => x.MeasureValue).ToArray());
            double usl = records.Last().UpperLimit;
            double lsl = records.Last().LowerLimit;

            double cp = sigma <= 0 ? 0 : (usl - lsl) / (6 * sigma);
            double cpu = sigma <= 0 ? 0 : (usl - mean) / (3 * sigma);
            double cpl = sigma <= 0 ? 0 : (mean - lsl) / (3 * sigma);
            double cpk = Math.Min(cpu, cpl);

            txtCount.Text = records.Count.ToString();
            txtMean.Text = mean.ToString("F3");
            txtSigma.Text = sigma.ToString("F3");
            txtCp.Text = cp.ToString("F3");
            txtCpk.Text = cpk.ToString("F3");
            txtUsl.Text = usl.ToString("F3");
            txtLsl.Text = lsl.ToString("F3");

            if (cpk >= 1.67)
            {
                txtCpkJudge.Text = "优秀";
                txtCpkJudge.Foreground = Brushes.LimeGreen;
            }
            else if (cpk >= 1.33)
            {
                txtCpkJudge.Text = "合格";
                txtCpkJudge.Foreground = Brushes.DeepSkyBlue;
            }
            else if (cpk >= 1.0)
            {
                txtCpkJudge.Text = "临界";
                txtCpkJudge.Foreground = Brushes.Orange;
            }
            else
            {
                txtCpkJudge.Text = "不合格";
                txtCpkJudge.Foreground = Brushes.Red;
            }

            txtReport.Text =
                $"样本数量：{records.Count}\n" +
                $"平均值：{mean:F3}\n" +
                $"标准差：{sigma:F3}\n" +
                $"规格上限 USL：{usl:F3}\n" +
                $"规格下限 LSL：{lsl:F3}\n" +
                $"CP：{cp:F3}\n" +
                $"CPK：{cpk:F3}\n\n" +
                $"说明：CP 表示过程分布宽度相对于规格宽度的能力；CPK 同时考虑平均值偏移，通常比 CP 更适合现场判断。";

            DrawSpcChart();
        }

        private double CalculateStdDev(double[] values)
        {
            if (values.Length <= 1)
                return 0;

            double avg = values.Average();
            double sum = values.Sum(x => Math.Pow(x - avg, 2));
            return Math.Sqrt(sum / (values.Length - 1));
        }

        private void DrawSpcChart()
        {
            spcCanvas.Children.Clear();

            var records = InspectionDataStore.Records.ToList();

            if (records.Count == 0)
            {
                DrawText("暂无SPC数据", 30, 30, Brushes.Gray, 24);
                return;
            }

            double width = spcCanvas.ActualWidth;
            double height = spcCanvas.ActualHeight;

            if (width <= 30 || height <= 30)
                return;

            double left = 60;
            double right = 30;
            double top = 30;
            double bottom = 40;

            double chartWidth = width - left - right;
            double chartHeight = height - top - bottom;

            double mean = records.Average(x => x.MeasureValue);
            double sigma = CalculateStdDev(records.Select(x => x.MeasureValue).ToArray());
            double usl = records.Last().UpperLimit;
            double lsl = records.Last().LowerLimit;

            double ucl = mean + 3 * sigma;
            double lcl = mean - 3 * sigma;

            double maxValue = records.Max(x => x.MeasureValue);
            double minValue = records.Min(x => x.MeasureValue);

            maxValue = Math.Max(maxValue, Math.Max(usl, ucl));
            minValue = Math.Min(minValue, Math.Min(lsl, lcl));

            if (Math.Abs(maxValue - minValue) < 0.0001)
            {
                maxValue += 1;
                minValue -= 1;
            }

            double range = maxValue - minValue;

            DrawAxis(left, top, chartWidth, chartHeight);

            DrawHorizontalLine(left, top, chartWidth, chartHeight, maxValue, minValue, usl, "USL", Brushes.OrangeRed);
            DrawHorizontalLine(left, top, chartWidth, chartHeight, maxValue, minValue, lsl, "LSL", Brushes.Orange);
            DrawHorizontalLine(left, top, chartWidth, chartHeight, maxValue, minValue, mean, "CL", Brushes.DeepSkyBlue);

            if (sigma > 0)
            {
                DrawHorizontalLine(left, top, chartWidth, chartHeight, maxValue, minValue, ucl, "UCL", Brushes.MediumPurple);
                DrawHorizontalLine(left, top, chartWidth, chartHeight, maxValue, minValue, lcl, "LCL", Brushes.MediumPurple);
            }

            var line = new Polyline
            {
                Stroke = Brushes.LimeGreen,
                StrokeThickness = 2
            };

            for (int i = 0; i < records.Count; i++)
            {
                double x = left + i * chartWidth / Math.Max(1, records.Count - 1);
                double y = top + (maxValue - records[i].MeasureValue) / range * chartHeight;

                line.Points.Add(new Point(x, y));

                Brush dotBrush = records[i].ResultCode == 1 ? Brushes.LimeGreen : Brushes.Red;

                var dot = new Ellipse
                {
                    Width = 7,
                    Height = 7,
                    Fill = dotBrush
                };

                Canvas.SetLeft(dot, x - 3.5);
                Canvas.SetTop(dot, y - 3.5);
                spcCanvas.Children.Add(dot);
            }

            spcCanvas.Children.Add(line);
        }

        private void DrawAxis(double left, double top, double chartWidth, double chartHeight)
        {
            Brush axisBrush = new SolidColorBrush(Color.FromRgb(70, 105, 135));

            spcCanvas.Children.Add(new Line
            {
                X1 = left,
                Y1 = top,
                X2 = left,
                Y2 = top + chartHeight,
                Stroke = axisBrush,
                StrokeThickness = 1
            });

            spcCanvas.Children.Add(new Line
            {
                X1 = left,
                Y1 = top + chartHeight,
                X2 = left + chartWidth,
                Y2 = top + chartHeight,
                Stroke = axisBrush,
                StrokeThickness = 1
            });
        }

        private void DrawHorizontalLine(
            double left,
            double top,
            double chartWidth,
            double chartHeight,
            double maxValue,
            double minValue,
            double value,
            string label,
            Brush brush)
        {
            double range = maxValue - minValue;
            double y = top + (maxValue - value) / range * chartHeight;

            spcCanvas.Children.Add(new Line
            {
                X1 = left,
                Y1 = y,
                X2 = left + chartWidth,
                Y2 = y,
                Stroke = brush,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 4 }
            });

            DrawText($"{label}:{value:F3}", left + chartWidth - 95, y - 18, brush, 12);
        }

        private void DrawText(string text, double x, double y, Brush brush, double fontSize)
        {
            var tb = new TextBlock
            {
                Text = text,
                Foreground = brush,
                FontSize = fontSize
            };

            Canvas.SetLeft(tb, x);
            Canvas.SetTop(tb, y);

            spcCanvas.Children.Add(tb);
        }
    }
}