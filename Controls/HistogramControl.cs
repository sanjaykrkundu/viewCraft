using System.Windows;
using System.Windows.Media;
using ViewCraft.Engine;

namespace ViewCraft.Controls
{
    public class HistogramControl : FrameworkElement
    {
        public static readonly DependencyProperty HistogramDataProperty =
            DependencyProperty.Register(
                nameof(HistogramData),
                typeof(HistogramData),
                typeof(HistogramControl),
                new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ShowRedProperty =
            DependencyProperty.Register(nameof(ShowRed), typeof(bool), typeof(HistogramControl),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ShowGreenProperty =
            DependencyProperty.Register(nameof(ShowGreen), typeof(bool), typeof(HistogramControl),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ShowBlueProperty =
            DependencyProperty.Register(nameof(ShowBlue), typeof(bool), typeof(HistogramControl),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ShowLumaProperty =
            DependencyProperty.Register(nameof(ShowLuma), typeof(bool), typeof(HistogramControl),
                new FrameworkPropertyMetadata(true, FrameworkPropertyMetadataOptions.AffectsRender));

        public HistogramData? HistogramData
        {
            get => (HistogramData?)GetValue(HistogramDataProperty);
            set => SetValue(HistogramDataProperty, value);
        }

        public bool ShowRed
        {
            get => (bool)GetValue(ShowRedProperty);
            set => SetValue(ShowRedProperty, value);
        }

        public bool ShowGreen
        {
            get => (bool)GetValue(ShowGreenProperty);
            set => SetValue(ShowGreenProperty, value);
        }

        public bool ShowBlue
        {
            get => (bool)GetValue(ShowBlueProperty);
            set => SetValue(ShowBlueProperty, value);
        }

        public bool ShowLuma
        {
            get => (bool)GetValue(ShowLumaProperty);
            set => SetValue(ShowLumaProperty, value);
        }

        private static readonly Brush BackgroundBrush = new SolidColorBrush(Color.FromRgb(24, 24, 27));
        private static readonly Brush BorderBrush = new SolidColorBrush(Color.FromRgb(63, 63, 70));
        private static readonly Pen BorderPen = new Pen(BorderBrush, 1);

        private static readonly Brush RedFillBrush = new SolidColorBrush(Color.FromArgb(90, 239, 68, 68));
        private static readonly Pen RedPen = new Pen(new SolidColorBrush(Color.FromRgb(239, 68, 68)), 1.2);

        private static readonly Brush GreenFillBrush = new SolidColorBrush(Color.FromArgb(90, 34, 197, 94));
        private static readonly Pen GreenPen = new Pen(new SolidColorBrush(Color.FromRgb(34, 197, 94)), 1.2);

        private static readonly Brush BlueFillBrush = new SolidColorBrush(Color.FromArgb(90, 59, 130, 246));
        private static readonly Pen BluePen = new Pen(new SolidColorBrush(Color.FromRgb(59, 130, 246)), 1.2);

        private static readonly Brush LumaFillBrush = new SolidColorBrush(Color.FromArgb(80, 200, 200, 200));
        private static readonly Pen LumaPen = new Pen(new SolidColorBrush(Color.FromRgb(220, 220, 220)), 1.0);

        static HistogramControl()
        {
            BorderPen.Freeze();
            RedPen.Freeze();
            GreenPen.Freeze();
            BluePen.Freeze();
            LumaPen.Freeze();
            RedFillBrush.Freeze();
            GreenFillBrush.Freeze();
            BlueFillBrush.Freeze();
            LumaFillBrush.Freeze();
            BackgroundBrush.Freeze();
        }

        protected override void OnRender(DrawingContext dc)
        {
            base.OnRender(dc);
            double width = ActualWidth;
            double height = ActualHeight;

            if (width < 10 || height < 10) return;

            // Draw dark background
            dc.DrawRectangle(BackgroundBrush, BorderPen, new Rect(0, 0, width, height));

            // Grid lines
            var gridPen = new Pen(new SolidColorBrush(Color.FromArgb(40, 255, 255, 255)), 0.5);
            gridPen.Freeze();
            dc.DrawLine(gridPen, new Point(width * 0.25, 0), new Point(width * 0.25, height));
            dc.DrawLine(gridPen, new Point(width * 0.5, 0), new Point(width * 0.5, height));
            dc.DrawLine(gridPen, new Point(width * 0.75, 0), new Point(width * 0.75, height));
            dc.DrawLine(gridPen, new Point(0, height * 0.5), new Point(width, height * 0.5));

            var data = HistogramData;
            if (data == null || data.MaxBinCount <= 0) return;

            double maxVal = data.MaxBinCount;

            if (ShowLuma) DrawChannel(dc, data.LumaBins, maxVal, width, height, LumaFillBrush, LumaPen);
            if (ShowRed) DrawChannel(dc, data.RedBins, maxVal, width, height, RedFillBrush, RedPen);
            if (ShowGreen) DrawChannel(dc, data.GreenBins, maxVal, width, height, GreenFillBrush, GreenPen);
            if (ShowBlue) DrawChannel(dc, data.BlueBins, maxVal, width, height, BlueFillBrush, BluePen);
        }

        private void DrawChannel(
            DrawingContext dc, 
            int[] bins, 
            double maxVal, 
            double width, 
            double height, 
            Brush fillBrush, 
            Pen linePen)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                ctx.BeginFigure(new Point(0, height), isFilled: true, isClosed: true);

                for (int i = 0; i < 256; i++)
                {
                    double x = (i / 255.0) * width;
                    double normalized = bins[i] / maxVal;
                    // logarithmic scaling for visually pleasant peak distribution
                    double logNormalized = Math.Log10(1 + (9 * normalized));
                    double y = height - (logNormalized * (height - 4));

                    ctx.LineTo(new Point(x, y), isStroked: true, isSmoothJoin: true);
                }

                ctx.LineTo(new Point(width, height), isStroked: false, isSmoothJoin: false);
            }
            geometry.Freeze();

            dc.DrawGeometry(fillBrush, linePen, geometry);
        }
    }
}
