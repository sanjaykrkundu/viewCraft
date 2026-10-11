using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace ViewCraft.Views
{
    public partial class CropDialog : Window
    {
        private readonly BitmapSource _sourceBmp;
        private Rect _cropRect; // in virtual canvas coordinates [0..imgW, 0..imgH]
        private double _imgWidth;
        private double _imgHeight;
        private double? _targetAspectRatio; // w / h

        private bool _isDraggingBox;
        private int _activeHandle = -1; // 0=NW, 1=NE, 2=SW, 3=SE
        private Point _startDrag;
        private Rect _initialCropRect;

        public Int32Rect? ResultCropRect { get; private set; }

        public CropDialog(BitmapSource sourceBmp)
        {
            InitializeComponent();
            _sourceBmp = sourceBmp;

            _imgWidth = _sourceBmp.PixelWidth;
            _imgHeight = _sourceBmp.PixelHeight;

            GridCanvas.Width = _imgWidth;
            GridCanvas.Height = _imgHeight;

            ImgPreview.Source = _sourceBmp;

            // Initial crop rect: 80% centered
            double initW = _imgWidth * 0.8;
            double initH = _imgHeight * 0.8;
            double initX = (_imgWidth - initW) / 2.0;
            double initY = (_imgHeight - initH) / 2.0;

            _cropRect = new Rect(initX, initY, initW, initH);
            UpdateVisuals();
        }

        private void AspectRatio_Changed(object sender, RoutedEventArgs e)
        {
            if (RbFree == null) return;

            if (RbFree.IsChecked == true) _targetAspectRatio = null;
            else if (Rb1x1.IsChecked == true) _targetAspectRatio = 1.0;
            else if (Rb16x9.IsChecked == true) _targetAspectRatio = 16.0 / 9.0;
            else if (Rb9x16.IsChecked == true) _targetAspectRatio = 9.0 / 16.0;
            else if (Rb4x3.IsChecked == true) _targetAspectRatio = 4.0 / 3.0;
            else if (Rb3x2.IsChecked == true) _targetAspectRatio = 3.0 / 2.0;

            if (_targetAspectRatio.HasValue)
            {
                ApplyAspectRatioConstraint();
            }
        }

        private void ApplyAspectRatioConstraint()
        {
            if (!_targetAspectRatio.HasValue) return;

            double targetRatio = _targetAspectRatio.Value;
            double w = _cropRect.Width;
            double h = w / targetRatio;

            if (h > _imgHeight)
            {
                h = _imgHeight * 0.8;
                w = h * targetRatio;
            }

            double x = Math.Clamp(_cropRect.X, 0, _imgWidth - w);
            double y = Math.Clamp(_cropRect.Y, 0, _imgHeight - h);

            _cropRect = new Rect(x, y, w, h);
            UpdateVisuals();
        }

        private void UpdateVisuals()
        {
            Canvas.SetLeft(RectCrop, _cropRect.X);
            Canvas.SetTop(RectCrop, _cropRect.Y);
            RectCrop.Width = _cropRect.Width;
            RectCrop.Height = _cropRect.Height;

            // Handles
            Canvas.SetLeft(HandleNW, _cropRect.X - 5);
            Canvas.SetTop(HandleNW, _cropRect.Y - 5);

            Canvas.SetLeft(HandleNE, _cropRect.Right - 5);
            Canvas.SetTop(HandleNE, _cropRect.Y - 5);

            Canvas.SetLeft(HandleSW, _cropRect.X - 5);
            Canvas.SetTop(HandleSW, _cropRect.Bottom - 5);

            Canvas.SetLeft(HandleSE, _cropRect.Right - 5);
            Canvas.SetTop(HandleSE, _cropRect.Bottom - 5);

            // Rule of thirds lines
            double oneThirdW = _cropRect.Width / 3.0;
            double oneThirdH = _cropRect.Height / 3.0;

            LineGridV1.X1 = _cropRect.X + oneThirdW;
            LineGridV1.X2 = _cropRect.X + oneThirdW;
            LineGridV1.Y1 = _cropRect.Y;
            LineGridV1.Y2 = _cropRect.Bottom;

            LineGridV2.X1 = _cropRect.X + (oneThirdW * 2);
            LineGridV2.X2 = _cropRect.X + (oneThirdW * 2);
            LineGridV2.Y1 = _cropRect.Y;
            LineGridV2.Y2 = _cropRect.Bottom;

            LineGridH1.X1 = _cropRect.X;
            LineGridH1.X2 = _cropRect.Right;
            LineGridH1.Y1 = _cropRect.Y + oneThirdH;
            LineGridH1.Y2 = _cropRect.Y + oneThirdH;

            LineGridH2.X1 = _cropRect.X;
            LineGridH2.X2 = _cropRect.Right;
            LineGridH2.Y1 = _cropRect.Y + (oneThirdH * 2);
            LineGridH2.Y2 = _cropRect.Y + (oneThirdH * 2);

            // Dimmer mask geometry (image rect with hole for crop rect)
            var group = new CombinedGeometry(
                GeometryCombineMode.Exclude,
                new RectangleGeometry(new Rect(0, 0, _imgWidth, _imgHeight)),
                new RectangleGeometry(_cropRect)
            );
            PathDimmer.Data = group;

            TxtCropDimensions.Text = $"Cropped Size: {(int)_cropRect.Width} × {(int)_cropRect.Height} px  ({((int)_cropRect.Width * (int)_cropRect.Height) / 1_000_000.0:F1} MP)";
        }

        private void CanvasCrop_MouseDown(object sender, MouseButtonEventArgs e)
        {
            Point pt = e.GetPosition(GridCanvas);

            // Check corner handles
            if (IsPointNear(pt, new Point(_cropRect.X, _cropRect.Y))) _activeHandle = 0;
            else if (IsPointNear(pt, new Point(_cropRect.Right, _cropRect.Y))) _activeHandle = 1;
            else if (IsPointNear(pt, new Point(_cropRect.X, _cropRect.Bottom))) _activeHandle = 2;
            else if (IsPointNear(pt, new Point(_cropRect.Right, _cropRect.Bottom))) _activeHandle = 3;
            else if (_cropRect.Contains(pt))
            {
                _isDraggingBox = true;
            }

            if (_activeHandle >= 0 || _isDraggingBox)
            {
                _startDrag = pt;
                _initialCropRect = _cropRect;
                CanvasCropOverlay.CaptureMouse();
            }
        }

        private bool IsPointNear(Point p1, Point p2, double threshold = 20)
        {
            return (p1 - p2).Length <= threshold;
        }

        private void CanvasCrop_MouseMove(object sender, MouseEventArgs e)
        {
            if (_activeHandle < 0 && !_isDraggingBox) return;

            Point current = e.GetPosition(GridCanvas);
            Vector delta = current - _startDrag;

            if (_isDraggingBox)
            {
                double newX = Math.Clamp(_initialCropRect.X + delta.X, 0, _imgWidth - _initialCropRect.Width);
                double newY = Math.Clamp(_initialCropRect.Y + delta.Y, 0, _imgHeight - _initialCropRect.Height);
                _cropRect = new Rect(newX, newY, _initialCropRect.Width, _initialCropRect.Height);
            }
            else
            {
                double x = _initialCropRect.X;
                double y = _initialCropRect.Y;
                double w = _initialCropRect.Width;
                double h = _initialCropRect.Height;

                switch (_activeHandle)
                {
                    case 0: // NW
                        x = Math.Clamp(_initialCropRect.X + delta.X, 0, _initialCropRect.Right - 50);
                        y = Math.Clamp(_initialCropRect.Y + delta.Y, 0, _initialCropRect.Bottom - 50);
                        w = _initialCropRect.Right - x;
                        h = _initialCropRect.Bottom - y;
                        break;
                    case 1: // NE
                        y = Math.Clamp(_initialCropRect.Y + delta.Y, 0, _initialCropRect.Bottom - 50);
                        w = Math.Clamp(_initialCropRect.Width + delta.X, 50, _imgWidth - x);
                        h = _initialCropRect.Bottom - y;
                        break;
                    case 2: // SW
                        x = Math.Clamp(_initialCropRect.X + delta.X, 0, _initialCropRect.Right - 50);
                        w = _initialCropRect.Right - x;
                        h = Math.Clamp(_initialCropRect.Height + delta.Y, 50, _imgHeight - y);
                        break;
                    case 3: // SE
                        w = Math.Clamp(_initialCropRect.Width + delta.X, 50, _imgWidth - x);
                        h = Math.Clamp(_initialCropRect.Height + delta.Y, 50, _imgHeight - y);
                        break;
                }

                if (_targetAspectRatio.HasValue)
                {
                    h = w / _targetAspectRatio.Value;
                }

                _cropRect = new Rect(x, y, w, h);
            }

            UpdateVisuals();
        }

        private void CanvasCrop_MouseUp(object sender, MouseButtonEventArgs e)
        {
            _activeHandle = -1;
            _isDraggingBox = false;
            CanvasCropOverlay.ReleaseMouseCapture();
        }

        private void BtnReset_Click(object sender, RoutedEventArgs e)
        {
            double initW = _imgWidth * 0.8;
            double initH = _imgHeight * 0.8;
            _cropRect = new Rect((_imgWidth - initW) / 2.0, (_imgHeight - initH) / 2.0, initW, initH);
            UpdateVisuals();
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnApply_Click(object sender, RoutedEventArgs e)
        {
            ResultCropRect = new Int32Rect((int)_cropRect.X, (int)_cropRect.Y, (int)_cropRect.Width, (int)_cropRect.Height);
            DialogResult = true;
            Close();
        }
    }
}
