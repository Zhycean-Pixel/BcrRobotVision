using Microsoft.Win32;
using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using BcrRobotVision.Services;

namespace BcrRobotVision.Pages
{
    public partial class ReportPage : UserControl
    {
        public ReportPage()
        {
            InitializeComponent();

            dataGrid.ItemsSource = InspectionDataStore.Records;

            RefreshReport();
        }

        private void BtnRefresh_Click(object sender, RoutedEventArgs e)
        {
            RefreshReport();
        }

        private void ChartCanvas_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            DrawChart();
        }

        private void RefreshReport()
        {
            txtTotal.Text = InspectionDataStore.TotalCount.ToString();
            txtOk.Text = InspectionDataStore.OkCount.ToString();
            txtNg.Text = InspectionDataStore.NgCount.ToString();
            txtYield.Text = $"{InspectionDataStore.YieldRate:F2}%";
            txtAverage.Text = $"{InspectionDataStore.Average:F3}";
            txtStdDev.Text = $"{InspectionDataStore.StdDev:F3}";
            txtCpk.Text = $"{InspectionDataStore.Cpk:F3}";

            DrawChart();
        }

        private void DrawChart()
        {
            chartCanvas.Children.Clear();

            var records = InspectionDataStore.Records.ToList();

            if (records.Count == 0)
            {
                DrawEmptyText("暂无检测数据");
                return;
            }

            double width = chartCanvas.ActualWidth;
            double height = chartCanvas.ActualHeight;

            if (width <= 20 || height <= 20)
                return;

            double left = 55;
            double right = 25;
            double top = 25;
            double bottom = 35;

            double chartWidth = width - left - right;
            double chartHeight = height - top - bottom;

            if (chartWidth <= 0 || chartHeight <= 0)
                return;

            double maxValue = records.Max(x => x.MeasureValue);
            double minValue = records.Min(x => x.MeasureValue);

            double upper = records.Last().UpperLimit;
            double lower = records.Last().LowerLimit;

            maxValue = Math.Max(maxValue, upper);
            minValue = Math.Min(minValue, lower);

            if (Math.Abs(maxValue - minValue) < 0.0001)
            {
                maxValue += 1;
                minValue -= 1;
            }

            double range = maxValue - minValue;

            DrawAxis(left, top, chartWidth, chartHeight);
            DrawLimitLine(left, top, chartWidth, chartHeight, upper, minValue, range, "USL");
            DrawLimitLine(left, top, chartWidth, chartHeight, lower, minValue, range, "LSL");

            var polyline = new Polyline
            {
                Stroke = Brushes.DeepSkyBlue,
                StrokeThickness = 2
            };

            for (int i = 0; i < records.Count; i++)
            {
                double x = left + i * chartWidth / Math.Max(1, records.Count - 1);
                double y = top + (maxValue - records[i].MeasureValue) / range * chartHeight;

                polyline.Points.Add(new Point(x, y));

                var dot = new Ellipse
                {
                    Width = 7,
                    Height = 7,
                    Fill = records[i].ResultCode == 1 ? Brushes.LimeGreen : Brushes.Red
                };

                Canvas.SetLeft(dot, x - 3.5);
                Canvas.SetTop(dot, y - 3.5);
                chartCanvas.Children.Add(dot);
            }

            chartCanvas.Children.Add(polyline);
        }

        private void DrawAxis(double left, double top, double chartWidth, double chartHeight)
        {
            var axisBrush = new SolidColorBrush(Color.FromRgb(70, 100, 130));

            var yAxis = new Line
            {
                X1 = left,
                Y1 = top,
                X2 = left,
                Y2 = top + chartHeight,
                Stroke = axisBrush,
                StrokeThickness = 1
            };

            var xAxis = new Line
            {
                X1 = left,
                Y1 = top + chartHeight,
                X2 = left + chartWidth,
                Y2 = top + chartHeight,
                Stroke = axisBrush,
                StrokeThickness = 1
            };

            chartCanvas.Children.Add(yAxis);
            chartCanvas.Children.Add(xAxis);
        }

        private void DrawLimitLine(
            double left,
            double top,
            double chartWidth,
            double chartHeight,
            double value,
            double minValue,
            double range,
            string label)
        {
            double maxValue = minValue + range;
            double y = top + (maxValue - value) / range * chartHeight;

            var line = new Line
            {
                X1 = left,
                Y1 = y,
                X2 = left + chartWidth,
                Y2 = y,
                Stroke = label == "USL" ? Brushes.OrangeRed : Brushes.Orange,
                StrokeThickness = 1,
                StrokeDashArray = new DoubleCollection { 4, 4 }
            };

            var text = new TextBlock
            {
                Text = $"{label}:{value:F3}",
                Foreground = label == "USL" ? Brushes.OrangeRed : Brushes.Orange,
                FontSize = 12
            };

            Canvas.SetLeft(text, left + chartWidth - 90);
            Canvas.SetTop(text, y - 18);

            chartCanvas.Children.Add(line);
            chartCanvas.Children.Add(text);
        }

        private void DrawEmptyText(string text)
        {
            var tb = new TextBlock
            {
                Text = text,
                Foreground = Brushes.Gray,
                FontSize = 22
            };

            Canvas.SetLeft(tb, 30);
            Canvas.SetTop(tb, 30);

            chartCanvas.Children.Add(tb);
        }

        private void BtnExportCsv_Click(object sender, RoutedEventArgs e)
        {
            var records = InspectionDataStore.Records.ToList();

            if (records.Count == 0)
            {
                MessageBox.Show("没有数据可以导出");
                return;
            }

            var dialog = new SaveFileDialog
            {
                Filter = "CSV文件|*.csv",
                FileName = $"检测报表_{DateTime.Now:yyyyMMdd_HHmmss}.csv"
            };

            if (dialog.ShowDialog() != true)
                return;

            var sb = new StringBuilder();

            sb.AppendLine("序号,时间,模式,拍照位,结果码,结果,检测值,上限,下限");
            foreach (var item in records)
            {
                sb.AppendLine(
                    $"{item.Index}," +
                    $"{item.Time:yyyy-MM-dd HH:mm:ss}," +
                    $"{item.Mode}," +
                    $"{item.CameraNo}," +
                    $"{item.ResultCode}," +
                    $"{item.ResultText}," +
                    $"{item.MeasureValue:F3}," +
                    $"{item.UpperLimit:F3}," +
                    $"{item.LowerLimit:F3}");
            }

            File.WriteAllText(dialog.FileName, sb.ToString(), Encoding.UTF8);

            MessageBox.Show("导出完成");
        }
    }
}