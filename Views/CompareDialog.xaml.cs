using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace ViewCraft.Views
{
    public partial class CompareDialog : Window
    {
        private readonly BitmapSource _before;
        private readonly BitmapSource _after;

        public CompareDialog(BitmapSource before, BitmapSource after)
        {
            InitializeComponent();
            _before = before;
            _after = after;

            ImgBefore.Source = _before;
            ImgAfter.Source = _after;
            ImgSideLeft.Source = _before;
            ImgSideRight.Source = _after;

            UpdateSplitClip();
        }

        private void CompareMode_Changed(object sender, RoutedEventArgs e)
        {
            if (GridSplitView == null || GridSideBySideView == null) return;

            if (RbSplit.IsChecked == true)
            {
                GridSplitView.Visibility = Visibility.Visible;
                GridSideBySideView.Visibility = Visibility.Collapsed;
                TxtCompareInfo.Text = "Use slider below to wipe between Before and After images.";
            }
            else
            {
                GridSplitView.Visibility = Visibility.Collapsed;
                GridSideBySideView.Visibility = Visibility.Visible;
                TxtCompareInfo.Text = "Showing Image 1 (Left) and Image 2 (Right) side by side.";
            }
        }

        private void SldSplitPosition_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            UpdateSplitClip();
        }

        private void SplitView_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateSplitClip();
        }

        private void UpdateSplitClip()
        {
            if (GridSplitView == null || GridBeforeContainer == null || LineSplitter == null) return;

            double width = GridSplitView.ActualWidth;
            double height = GridSplitView.ActualHeight;

            if (width <= 0 || height <= 0) return;

            double splitX = width * SldSplitPosition.Value;

            // Clip the Before image to [0 .. splitX]
            GridBeforeContainer.Clip = new RectangleGeometry(new Rect(0, 0, splitX, height));

            // Position splitter line
            LineSplitter.X1 = splitX;
            LineSplitter.X2 = splitX;
            LineSplitter.Y1 = 0;
            LineSplitter.Y2 = height;
        }

        private void BtnResetView_Click(object sender, RoutedEventArgs e)
        {
            SldSplitPosition.Value = 0.5;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }
    }
}
