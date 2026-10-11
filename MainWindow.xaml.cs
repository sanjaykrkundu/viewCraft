using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;
using System.Windows.Threading;
using ImageMagick;
using Microsoft.Win32;
using ViewCraft.Controls;
using ViewCraft.Engine;
using ViewCraft.Models;
using ViewCraft.Views;
using Path = System.IO.Path;
using ImageMetadata = ViewCraft.Models.ImageMetadata;

namespace ViewCraft
{
    public partial class MainWindow : Window
    {
        // Session state
        private readonly ObservableCollection<ImageItem> _imageItems = new();
        private int _currentIndex = -1;
        private DecodedImageResult? _currentDecoded;
        private ImageMetadata? _currentMetadata;
        private readonly AdjustmentParameters _adjustments = new();

        // Canvas Pan & Zoom
        private double _currentZoom = 1.0;
        private Point _panOffset = new(0, 0);
        private Point _lastMouseDown;
        private bool _isPanning;
        private bool _lockZoom;
        private bool _isAdjusting;

        // Animation Player
        private readonly DispatcherTimer _animTimer = new();
        private int _currentFrameIndex;
        private bool _isAnimPlaying;

        // Slideshow
        private readonly DispatcherTimer _slideshowTimer = new();
        private bool _isSlideshowRunning;

        // Debounce timer for sliders
        private readonly DispatcherTimer _adjustmentDebounce = new();

        public MainWindow()
        {
            InitializeComponent();

            ListFilmstrip.ItemsSource = _imageItems;

            _animTimer.Tick += AnimTimer_Tick;
            _slideshowTimer.Interval = TimeSpan.FromSeconds(3);
            _slideshowTimer.Tick += SlideshowTimer_Tick;

            _adjustmentDebounce.Interval = TimeSpan.FromMilliseconds(40);
            _adjustmentDebounce.Tick += AdjustmentDebounce_Tick;

            // Enable Drag and Drop
            AllowDrop = true;
            Drop += MainWindow_Drop;
        }

        #region File Loading & Navigation

        private void MainWindow_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                string[] files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files.Length > 0)
                {
                    string target = files[0];
                    if (Directory.Exists(target))
                    {
                        LoadDirectory(target);
                    }
                    else if (File.Exists(target))
                    {
                        LoadFileAndDirectory(target);
                    }
                }
            }
        }

        private void MenuOpenImage_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Title = "Open Image — ViewCraft",
                Filter = CodecEngine.GetFileFilter(),
                Multiselect = true
            };

            if (dlg.ShowDialog() == true && dlg.FileNames.Length > 0)
            {
                LoadFileAndDirectory(dlg.FileNames[0]);
            }
        }

        private void MenuOpenFolder_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFolderDialog
            {
                Title = "Open Folder — ViewCraft"
            };

            if (dlg.ShowDialog() == true)
            {
                LoadDirectory(dlg.FolderName);
            }
        }

        private void LoadFileAndDirectory(string selectedFilePath)
        {
            string dir = Path.GetDirectoryName(selectedFilePath) ?? string.Empty;
            if (string.IsNullOrEmpty(dir)) return;

            LoadDirectory(dir, selectedFilePath);
        }

        private void LoadDirectory(string directoryPath, string? selectedFilePath = null)
        {
            try
            {
                var files = Directory.EnumerateFiles(directoryPath)
                    .Where(CodecEngine.IsSupportedFile)
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                    .ToList();

                _imageItems.Clear();
                int selectIdx = 0;

                for (int i = 0; i < files.Count; i++)
                {
                    string f = files[i];
                    var item = new ImageItem { FilePath = f };
                    _imageItems.Add(item);

                    if (selectedFilePath != null && string.Equals(f, selectedFilePath, StringComparison.OrdinalIgnoreCase))
                    {
                        selectIdx = i;
                    }
                }

                if (_imageItems.Count > 0)
                {
                    SelectImageIndex(selectIdx);
                    _ = LoadThumbnailsBackgroundAsync();
                }
                else
                {
                    PanelEmptyState.Visibility = Visibility.Visible;
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Could not open directory: {ex.Message}", "ViewCraft", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private async Task LoadThumbnailsBackgroundAsync()
        {
            foreach (var item in _imageItems)
            {
                if (item.Thumbnail == null)
                {
                    var thumb = await CodecEngine.LoadThumbnailAsync(item.FilePath, 160);
                    item.Thumbnail = thumb;
                    item.IsLoaded = true;
                }
            }
        }

        private async void SelectImageIndex(int index)
        {
            if (index < 0 || index >= _imageItems.Count) return;

            _currentIndex = index;
            var item = _imageItems[index];

            ListFilmstrip.SelectedIndex = index;
            ListFilmstrip.ScrollIntoView(item);

            PanelEmptyState.Visibility = Visibility.Collapsed;
            Title = $"{item.FileName} — ViewCraft Studio";

            StatusIndex.Text = $"Image {index + 1} of {_imageItems.Count}";
            StatusFileSize.Text = item.FileSizeFormatted;

            // Stop any running animations
            StopAnimation();

            try
            {
                // Decode full image
                var result = await CodecEngine.DecodeAsync(item.FilePath);
                _currentDecoded = result;

                item.Width = result.Width;
                item.Height = result.Height;
                item.FormatName = result.Format;

                StatusDimensions.Text = $"{result.Width} × {result.Height} px";
                StatusFormat.Text = result.Format.ToUpperInvariant();

                // Reset or preserve zoom
                if (!_lockZoom)
                {
                    FitToWindow();
                }

                // Reset non-destructive adjustments
                _adjustments.Reset();
                UpdateAdjustmentControlsFromModel();

                // Display first frame
                UpdateDisplayImage(result.Frames[0].Source);

                // Setup multi-frame animation player
                if (result.IsAnimated)
                {
                    BorderAnimationTimeline.Visibility = Visibility.Visible;
                    SldAnimTimeline.Maximum = result.Frames.Count - 1;
                    SldAnimTimeline.Value = 0;
                    _currentFrameIndex = 0;
                    TxtAnimFrameCount.Text = $"Frame 1 of {result.Frames.Count}";
                    StartAnimation();
                }
                else
                {
                    BorderAnimationTimeline.Visibility = Visibility.Collapsed;
                }

                // Extract Metadata
                _currentMetadata = await MetadataEngine.ExtractMetadataAsync(item.FilePath, result.MasterMagickImage);
                PopulateMetadataView(_currentMetadata);

                // Compute initial Histogram
                _ = UpdateHistogramAsync(result.Frames[0].Source);

                // Update Mini-Map
                ImgMiniMap.Source = result.Frames[0].Source;
                UpdateMiniMapViewport();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Failed to load image: {ex.Message}", "ViewCraft Decode Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ListFilmstrip_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (ListFilmstrip.SelectedIndex >= 0 && ListFilmstrip.SelectedIndex != _currentIndex)
            {
                SelectImageIndex(ListFilmstrip.SelectedIndex);
            }
        }

        private void BtnPrev_Click(object sender, RoutedEventArgs e) => NavigatePrev();
        private void BtnNext_Click(object sender, RoutedEventArgs e) => NavigateNext();

        private void NavigatePrev()
        {
            if (_imageItems.Count == 0) return;
            int next = _currentIndex - 1;
            if (next < 0) next = _imageItems.Count - 1;
            SelectImageIndex(next);
        }

        private void NavigateNext()
        {
            if (_imageItems.Count == 0) return;
            int next = _currentIndex + 1;
            if (next >= _imageItems.Count) next = 0;
            SelectImageIndex(next);
        }

        #endregion

        #region Display, Pan & Zoom

        private void UpdateDisplayImage(BitmapSource bmp)
        {
            MainImageDisplay.Source = bmp;

            // Set NearestNeighbor sampling for high zoom
            if (_currentZoom >= 4.0)
            {
                RenderOptions.SetBitmapScalingMode(MainImageDisplay, BitmapScalingMode.NearestNeighbor);
            }
            else
            {
                RenderOptions.SetBitmapScalingMode(MainImageDisplay, BitmapScalingMode.Linear);
            }

            UpdateMiniMapViewport();
        }

        private void FitToWindow()
        {
            if (MainImageDisplay.Source == null) return;

            double viewW = ScrollViewport.ActualWidth;
            double viewH = ScrollViewport.ActualHeight;
            if (viewW <= 0 || viewH <= 0)
            {
                viewW = 1000;
                viewH = 650;
            }

            double imgW = MainImageDisplay.Source.Width;
            double imgH = MainImageDisplay.Source.Height;

            if (imgW <= 0 || imgH <= 0) return;

            double scaleX = (viewW - 20) / imgW;
            double scaleY = (viewH - 20) / imgH;
            _currentZoom = Math.Min(scaleX, scaleY);

            if (_currentZoom > 1.0) _currentZoom = 1.0;
            if (_currentZoom < 0.01) _currentZoom = 0.01;

            _panOffset = new Point(0, 0);
            ApplyTransform();
        }

        private void SetZoom(double zoom)
        {
            _currentZoom = Math.Clamp(zoom, 0.01, 64.0);
            ApplyTransform();
        }

        private void ApplyTransform()
        {
            TransformScale.ScaleX = _currentZoom;
            TransformScale.ScaleY = _currentZoom;
            TransformTranslate.X = _panOffset.X;
            TransformTranslate.Y = _panOffset.Y;

            TxtZoomLevel.Text = $"{(int)(_currentZoom * 100)}%";

            if (MainImageDisplay.Source != null)
            {
                if (_currentZoom >= 4.0)
                {
                    RenderOptions.SetBitmapScalingMode(MainImageDisplay, BitmapScalingMode.NearestNeighbor);
                }
                else
                {
                    RenderOptions.SetBitmapScalingMode(MainImageDisplay, BitmapScalingMode.Linear);
                }
            }

            UpdateMiniMapViewport();
        }

        private void ScrollViewport_PreviewMouseWheel(object sender, MouseWheelEventArgs e)
        {
            e.Handled = true;

            Point mousePos = e.GetPosition(GridCanvasHost);
            double zoomFactor = e.Delta > 0 ? 1.2 : 0.8333;
            double newZoom = Math.Clamp(_currentZoom * zoomFactor, 0.01, 64.0);

            // Zoom centered at mouse position
            _panOffset.X = mousePos.X - ((mousePos.X - _panOffset.X) * (newZoom / _currentZoom));
            _panOffset.Y = mousePos.Y - ((mousePos.Y - _panOffset.Y) * (newZoom / _currentZoom));

            _currentZoom = newZoom;
            ApplyTransform();
        }

        private void ScrollViewport_MouseDown(object sender, MouseButtonEventArgs e)
        {
            if (e.LeftButton == MouseButtonState.Pressed || 
                e.MiddleButton == MouseButtonState.Pressed || 
                e.RightButton == MouseButtonState.Pressed)
            {
                _isPanning = true;
                _lastMouseDown = e.GetPosition(this);
                ScrollViewport.CaptureMouse();
                Cursor = Cursors.Hand;
            }
        }

        private void ScrollViewport_MouseMove(object sender, MouseEventArgs e)
        {
            if (_isPanning)
            {
                Point current = e.GetPosition(this);
                Vector delta = current - _lastMouseDown;
                _panOffset.X += delta.X;
                _panOffset.Y += delta.Y;
                _lastMouseDown = current;
                ApplyTransform();
            }
        }

        private void ScrollViewport_MouseUp(object sender, MouseButtonEventArgs e)
        {
            if (_isPanning)
            {
                _isPanning = false;
                ScrollViewport.ReleaseMouseCapture();
                Cursor = Cursors.Arrow;
            }
        }

        private void MainImageDisplay_MouseMove(object sender, MouseEventArgs e)
        {
            if (MainImageDisplay.Source is not BitmapSource bmp) return;

            Point pos = e.GetPosition(MainImageDisplay);
            int x = (int)pos.X;
            int y = (int)pos.Y;

            if (x >= 0 && x < bmp.PixelWidth && y >= 0 && y < bmp.PixelHeight)
            {
                TxtPixelCoord.Text = $"X: {x}  Y: {y}";

                try
                {
                    // Sample pixel color
                    var cropped = new CroppedBitmap(bmp, new Int32Rect(x, y, 1, 1));
                    var bgra = new FormatConvertedBitmap(cropped, PixelFormats.Bgra32, null, 0);
                    byte[] pixels = new byte[4];
                    bgra.CopyPixels(pixels, 4, 0);

                    byte b = pixels[0];
                    byte g = pixels[1];
                    byte r = pixels[2];
                    byte a = pixels[3];

                    string hex = $"#{r:X2}{g:X2}{b:X2}";
                    TxtPixelColor.Text = $"RGB: {r}, {g}, {b}  Hex: {hex}";
                    BorderColorSwatch.Background = new SolidColorBrush(Color.FromArgb(a, r, g, b));
                    StatusCursorInfo.Text = $"Pixel ({x}, {y})  RGB:({r},{g},{b})  {hex}";
                }
                catch
                {
                    // ignored
                }
            }
        }

        private void MainImageDisplay_MouseLeave(object sender, MouseEventArgs e)
        {
            TxtPixelCoord.Text = "X: —  Y: —";
            TxtPixelColor.Text = "RGB: —  Hex: —";
            StatusCursorInfo.Text = "";
        }

        private void UpdateMiniMapViewport()
        {
            if (MainImageDisplay.Source == null || BorderMiniMap.Visibility != Visibility.Visible) return;

            double mapW = GridMiniMap.ActualWidth;
            double mapH = GridMiniMap.ActualHeight;
            if (mapW <= 0 || mapH <= 0) return;

            double viewW = ScrollViewport.ActualWidth;
            double viewH = ScrollViewport.ActualHeight;

            double imgW = MainImageDisplay.Source.Width * _currentZoom;
            double imgH = MainImageDisplay.Source.Height * _currentZoom;

            if (imgW <= 0 || imgH <= 0) return;

            double ratioW = Math.Min(1.0, viewW / imgW);
            double ratioH = Math.Min(1.0, viewH / imgH);

            RectMiniMapViewport.Width = mapW * ratioW;
            RectMiniMapViewport.Height = mapH * ratioH;

            double normX = -_panOffset.X / imgW;
            double normY = -_panOffset.Y / imgH;

            Canvas.SetLeft(RectMiniMapViewport, Math.Clamp(normX * mapW, 0, mapW - RectMiniMapViewport.Width));
            Canvas.SetTop(RectMiniMapViewport, Math.Clamp(normY * mapH, 0, mapH - RectMiniMapViewport.Height));
        }

        #endregion

        #region Non-Destructive Adjustments & Histogram

        private void AdjustmentSlider_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (_isAdjusting) return;

            if (ValExposure != null) ValExposure.Text = $"{SldExposure.Value:F1}";
            if (ValContrast != null) ValContrast.Text = $"{(int)SldContrast.Value}";
            if (ValBrightness != null) ValBrightness.Text = $"{(int)SldBrightness.Value}";
            if (ValSaturation != null) ValSaturation.Text = $"{(int)SldSaturation.Value}";
            if (ValVibrance != null) ValVibrance.Text = $"{(int)SldVibrance.Value}";
            if (ValTemperature != null) ValTemperature.Text = $"{(int)SldTemperature.Value}";
            if (ValTint != null) ValTint.Text = $"{(int)SldTint.Value}";
            if (ValHighlights != null) ValHighlights.Text = $"{(int)SldHighlights.Value}";
            if (ValShadows != null) ValShadows.Text = $"{(int)SldShadows.Value}";
            if (ValSharpness != null) ValSharpness.Text = $"{(int)SldSharpness.Value}";

            _adjustmentDebounce.Stop();
            _adjustmentDebounce.Start();
        }

        private void Filter_Changed(object sender, RoutedEventArgs e)
        {
            if (_isAdjusting) return;
            _adjustmentDebounce.Stop();
            _adjustmentDebounce.Start();
        }

        private async void AdjustmentDebounce_Tick(object? sender, EventArgs e)
        {
            _adjustmentDebounce.Stop();
            if (_currentDecoded?.MasterMagickImage == null) return;

            _adjustments.Exposure = SldExposure.Value;
            _adjustments.Contrast = SldContrast.Value;
            _adjustments.Brightness = SldBrightness.Value;
            _adjustments.Saturation = SldSaturation.Value;
            _adjustments.Vibrance = SldVibrance.Value;
            _adjustments.Temperature = SldTemperature.Value;
            _adjustments.Tint = SldTint.Value;
            _adjustments.Highlights = SldHighlights.Value;
            _adjustments.Shadows = SldShadows.Value;
            _adjustments.Sharpness = SldSharpness.Value;

            _adjustments.InvertColors = ChkInvert.IsChecked == true;
            _adjustments.Grayscale = ChkGrayscale.IsChecked == true;
            _adjustments.Sepia = ChkSepia.IsChecked == true;

            try
            {
                var adjustedBmp = await AdjustmentPipeline.ApplyAdjustmentsAsync(_currentDecoded.MasterMagickImage, _adjustments);
                UpdateDisplayImage(adjustedBmp);
                _ = UpdateHistogramAsync(adjustedBmp);
            }
            catch
            {
                // ignored
            }
        }

        private void UpdateAdjustmentControlsFromModel()
        {
            _isAdjusting = true;
            try
            {
                SldExposure.Value = _adjustments.Exposure;
                SldContrast.Value = _adjustments.Contrast;
                SldBrightness.Value = _adjustments.Brightness;
                SldSaturation.Value = _adjustments.Saturation;
                SldVibrance.Value = _adjustments.Vibrance;
                SldTemperature.Value = _adjustments.Temperature;
                SldTint.Value = _adjustments.Tint;
                SldHighlights.Value = _adjustments.Highlights;
                SldShadows.Value = _adjustments.Shadows;
                SldSharpness.Value = _adjustments.Sharpness;

                ChkInvert.IsChecked = _adjustments.InvertColors;
                ChkGrayscale.IsChecked = _adjustments.Grayscale;
                ChkSepia.IsChecked = _adjustments.Sepia;
            }
            finally
            {
                _isAdjusting = false;
            }
        }

        private async Task UpdateHistogramAsync(BitmapSource bmp)
        {
            var data = await HistogramEngine.ComputeHistogramAsync(bmp);
            HistogramGraph.HistogramData = data;
            HistoMean.Text = $"{data.MeanLuma:F1} / 255";
            HistoMaxCount.Text = $"{data.MaxBinCount:N0}";
        }

        private void HistoCheck_Changed(object sender, RoutedEventArgs e)
        {
            if (HistogramGraph == null) return;
            HistogramGraph.ShowLuma = ChkHistoLuma.IsChecked == true;
            HistogramGraph.ShowRed = ChkHistoRed.IsChecked == true;
            HistogramGraph.ShowGreen = ChkHistoGreen.IsChecked == true;
            HistogramGraph.ShowBlue = ChkHistoBlue.IsChecked == true;
        }

        private void BtnChannelRGB_Click(object sender, RoutedEventArgs e) => SetChannelMode(ChannelIsolationMode.AllRgb);
        private void BtnChannelRed_Click(object sender, RoutedEventArgs e) => SetChannelMode(ChannelIsolationMode.RedOnly);
        private void BtnChannelGreen_Click(object sender, RoutedEventArgs e) => SetChannelMode(ChannelIsolationMode.GreenOnly);
        private void BtnChannelBlue_Click(object sender, RoutedEventArgs e) => SetChannelMode(ChannelIsolationMode.BlueOnly);

        private void SetChannelMode(ChannelIsolationMode mode)
        {
            _adjustments.ChannelMode = mode;
            AdjustmentDebounce_Tick(null, EventArgs.Empty);
        }

        private void MenuResetAdjustments_Click(object sender, RoutedEventArgs e)
        {
            _adjustments.Reset();
            UpdateAdjustmentControlsFromModel();
            if (_currentDecoded != null && _currentDecoded.Frames.Count > 0)
            {
                UpdateDisplayImage(_currentDecoded.Frames[0].Source);
                _ = UpdateHistogramAsync(_currentDecoded.Frames[0].Source);
            }
        }

        private void MenuAutoEnhance_Click(object sender, RoutedEventArgs e)
        {
            SldExposure.Value = 0.2;
            SldContrast.Value = 15;
            SldVibrance.Value = 20;
            SldHighlights.Value = -10;
            SldShadows.Value = 15;
            SldSharpness.Value = 25;
        }

        private void MenuInvert_Click(object sender, RoutedEventArgs e) => ChkInvert.IsChecked = !ChkInvert.IsChecked;
        private void MenuGrayscale_Click(object sender, RoutedEventArgs e) => ChkGrayscale.IsChecked = !ChkGrayscale.IsChecked;
        private void MenuSepia_Click(object sender, RoutedEventArgs e) => ChkSepia.IsChecked = !ChkSepia.IsChecked;

        #endregion

        #region Animation Player

        private void StartAnimation()
        {
            if (_currentDecoded?.Frames.Count > 1)
            {
                _isAnimPlaying = true;
                BtnAnimPlayPause.Content = "⏸ Pause";
                ScheduleNextFrame();
            }
        }

        private void StopAnimation()
        {
            _animTimer.Stop();
            _isAnimPlaying = false;
            BtnAnimPlayPause.Content = "▶ Play";
        }

        private void ScheduleNextFrame()
        {
            if (_currentDecoded == null || !_isAnimPlaying) return;
            var frame = _currentDecoded.Frames[_currentFrameIndex];
            _animTimer.Interval = TimeSpan.FromMilliseconds(Math.Max(20, frame.DelayMs));
            _animTimer.Start();
        }

        private void AnimTimer_Tick(object? sender, EventArgs e)
        {
            _animTimer.Stop();
            if (_currentDecoded == null || _currentDecoded.Frames.Count <= 1) return;

            _currentFrameIndex = (_currentFrameIndex + 1) % _currentDecoded.Frames.Count;
            SldAnimTimeline.Value = _currentFrameIndex;
            TxtAnimFrameCount.Text = $"Frame {_currentFrameIndex + 1} of {_currentDecoded.Frames.Count}";

            UpdateDisplayImage(_currentDecoded.Frames[_currentFrameIndex].Source);

            if (_isAnimPlaying)
            {
                ScheduleNextFrame();
            }
        }

        private void BtnAnimPlayPause_Click(object sender, RoutedEventArgs e)
        {
            if (_isAnimPlaying) StopAnimation();
            else StartAnimation();
        }

        private void BtnAnimPrevFrame_Click(object sender, RoutedEventArgs e)
        {
            StopAnimation();
            if (_currentDecoded == null || _currentDecoded.Frames.Count == 0) return;
            _currentFrameIndex = (_currentFrameIndex - 1 + _currentDecoded.Frames.Count) % _currentDecoded.Frames.Count;
            SldAnimTimeline.Value = _currentFrameIndex;
            TxtAnimFrameCount.Text = $"Frame {_currentFrameIndex + 1} of {_currentDecoded.Frames.Count}";
            UpdateDisplayImage(_currentDecoded.Frames[_currentFrameIndex].Source);
        }

        private void BtnAnimNextFrame_Click(object sender, RoutedEventArgs e)
        {
            StopAnimation();
            if (_currentDecoded == null || _currentDecoded.Frames.Count == 0) return;
            _currentFrameIndex = (_currentFrameIndex + 1) % _currentDecoded.Frames.Count;
            SldAnimTimeline.Value = _currentFrameIndex;
            TxtAnimFrameCount.Text = $"Frame {_currentFrameIndex + 1} of {_currentDecoded.Frames.Count}";
            UpdateDisplayImage(_currentDecoded.Frames[_currentFrameIndex].Source);
        }

        private void SldAnimTimeline_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            int idx = (int)e.NewValue;
            if (_currentDecoded != null && idx >= 0 && idx < _currentDecoded.Frames.Count && idx != _currentFrameIndex)
            {
                _currentFrameIndex = idx;
                TxtAnimFrameCount.Text = $"Frame {_currentFrameIndex + 1} of {_currentDecoded.Frames.Count}";
                UpdateDisplayImage(_currentDecoded.Frames[_currentFrameIndex].Source);
            }
        }

        private async void MenuExportFrames_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDecoded == null || _currentIndex < 0) return;

            var dlg = new OpenFolderDialog { Title = "Select Folder to Export Animation Frames" };
            if (dlg.ShowDialog() == true)
            {
                string outDir = dlg.FolderName;
                string baseName = Path.GetFileNameWithoutExtension(_imageItems[_currentIndex].FilePath);

                await Task.Run(() =>
                {
                    for (int i = 0; i < _currentDecoded.Frames.Count; i++)
                    {
                        var frame = _currentDecoded.Frames[i];
                        string outPath = Path.Combine(outDir, $"{baseName}_frame_{i + 1:D3}.png");

                        using var fileStream = new FileStream(outPath, FileMode.Create);
                        var encoder = new PngBitmapEncoder();
                        encoder.Frames.Add(BitmapFrame.Create(frame.Source));
                        encoder.Save(fileStream);
                    }
                });

                MessageBox.Show($"Exported {_currentDecoded.Frames.Count} frames to {outDir}", "ViewCraft Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        #endregion

        #region Slideshow

        private void MenuSlideshow_Click(object sender, RoutedEventArgs e)
        {
            if (_isSlideshowRunning)
            {
                _slideshowTimer.Stop();
                _isSlideshowRunning = false;
                BtnSlideshowToggle.Content = "▶ Slideshow";
            }
            else
            {
                _slideshowTimer.Start();
                _isSlideshowRunning = true;
                BtnSlideshowToggle.Content = "⏹ Stop Slides";
            }
        }

        private void SlideshowTimer_Tick(object? sender, EventArgs e)
        {
            NavigateNext();
        }

        #endregion

        #region Transformations (Rotate & Flip)

        private void MenuRotateCW_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDecoded?.MasterMagickImage == null) return;
            _currentDecoded.MasterMagickImage.Rotate(90);
            var bmp = CodecEngine.ConvertMagickToBitmapSource(_currentDecoded.MasterMagickImage);
            UpdateDisplayImage(bmp);
            FitToWindow();
        }

        private void MenuRotateCCW_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDecoded?.MasterMagickImage == null) return;
            _currentDecoded.MasterMagickImage.Rotate(270);
            var bmp = CodecEngine.ConvertMagickToBitmapSource(_currentDecoded.MasterMagickImage);
            UpdateDisplayImage(bmp);
            FitToWindow();
        }

        private void MenuRotate180_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDecoded?.MasterMagickImage == null) return;
            _currentDecoded.MasterMagickImage.Rotate(180);
            var bmp = CodecEngine.ConvertMagickToBitmapSource(_currentDecoded.MasterMagickImage);
            UpdateDisplayImage(bmp);
        }

        private void MenuFlipH_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDecoded?.MasterMagickImage == null) return;
            _currentDecoded.MasterMagickImage.Flop();
            var bmp = CodecEngine.ConvertMagickToBitmapSource(_currentDecoded.MasterMagickImage);
            UpdateDisplayImage(bmp);
        }

        private void MenuFlipV_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDecoded?.MasterMagickImage == null) return;
            _currentDecoded.MasterMagickImage.Flip();
            var bmp = CodecEngine.ConvertMagickToBitmapSource(_currentDecoded.MasterMagickImage);
            UpdateDisplayImage(bmp);
        }

        #endregion

        #region Dialogs & Tools

        private void MenuCompare_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDecoded == null || _currentDecoded.Frames.Count == 0) return;

            var originalBmp = _currentDecoded.Frames[0].Source;
            var currentBmp = (BitmapSource)MainImageDisplay.Source;

            var dlg = new CompareDialog(originalBmp, currentBmp) { Owner = this };
            dlg.ShowDialog();
        }

        private void MenuBatch_Click(object sender, RoutedEventArgs e)
        {
            if (_imageItems.Count == 0)
            {
                MessageBox.Show("Please open a folder or images first to perform batch processing.", "ViewCraft", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            var paths = _imageItems.Select(x => x.FilePath).ToList();
            string curDir = _currentIndex >= 0 ? _imageItems[_currentIndex].DirectoryPath : string.Empty;

            var dlg = new BatchDialog(paths, curDir) { Owner = this };
            dlg.ShowDialog();
        }

        private void MenuExport_Click(object sender, RoutedEventArgs e)
        {
            if (_currentDecoded?.MasterMagickImage == null) return;

            var dlg = new SaveFileDialog
            {
                Title = "Export Image — ViewCraft",
                Filter = "WebP Image (*.webp)|*.webp|JPEG Image (*.jpg)|*.jpg|PNG Image (*.png)|*.png|AVIF Image (*.avif)|*.avif|TIFF Image (*.tiff)|*.tiff|Windows BMP (*.bmp)|*.bmp",
                FileName = _currentIndex >= 0 ? Path.GetFileNameWithoutExtension(_imageItems[_currentIndex].FilePath) : "Export"
            };

            if (dlg.ShowDialog() == true)
            {
                string ext = Path.GetExtension(dlg.FileName).ToLowerInvariant();
                var format = ext switch
                {
                    ".webp" => MagickFormat.WebP,
                    ".jpg" or ".jpeg" => MagickFormat.Jpeg,
                    ".png" => MagickFormat.Png,
                    ".avif" => MagickFormat.Avif,
                    ".tiff" or ".tif" => MagickFormat.Tiff,
                    ".bmp" => MagickFormat.Bmp,
                    _ => MagickFormat.Png
                };

                _ = CodecEngine.ExportImageAsync(_currentDecoded.MasterMagickImage, dlg.FileName, format, 92);
                MessageBox.Show($"Exported successfully to:\n{dlg.FileName}", "ViewCraft Export", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void MenuCopyImage_Click(object sender, RoutedEventArgs e)
        {
            if (MainImageDisplay.Source is BitmapSource bmp)
            {
                Clipboard.SetImage(bmp);
            }
        }

        private void MenuCopyPath_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIndex >= 0)
            {
                Clipboard.SetText(_imageItems[_currentIndex].FilePath);
            }
        }

        private void MenuRevealExplorer_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIndex >= 0 && File.Exists(_imageItems[_currentIndex].FilePath))
            {
                Process.Start("explorer.exe", $"/select,\"{_imageItems[_currentIndex].FilePath}\"");
            }
        }

        private void MenuDelete_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIndex < 0) return;
            var item = _imageItems[_currentIndex];

            var res = MessageBox.Show($"Are you sure you want to delete '{item.FileName}'?", "Delete Image", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (res == MessageBoxResult.Yes)
            {
                try
                {
                    File.Delete(item.FilePath);
                    int delIdx = _currentIndex;
                    _imageItems.RemoveAt(delIdx);
                    if (_imageItems.Count > 0)
                    {
                        SelectImageIndex(Math.Min(delIdx, _imageItems.Count - 1));
                    }
                    else
                    {
                        PanelEmptyState.Visibility = Visibility.Visible;
                    }
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Could not delete file: {ex.Message}", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void MenuReload_Click(object sender, RoutedEventArgs e)
        {
            if (_currentIndex >= 0)
            {
                SelectImageIndex(_currentIndex);
            }
        }

        private void MenuAbout_Click(object sender, RoutedEventArgs e)
        {
            MessageBox.Show(
                "ViewCraft — The Universal Studio Image Viewer\n\n" +
                "Sibling to LightCraft and PhotoCraft in the ArtCraft suite.\n\n" +
                "Engine Capabilities:\n" +
                "• 200+ Decodable Formats (Camera RAW, PSD/PSB, AVIF, JXL, HEIC, WebP, EXR, HDR, TIFF, SVG, DICOM, FITS, DDS, TGA, PCX)\n" +
                "• Hardware-Accelerated Pan & Deep Zoom (1% - 6400%)\n" +
                "• Real-time Non-Destructive Tone & Color Adjustments\n" +
                "• Live RGB & Luminance Histogram\n" +
                "• Full EXIF, IPTC, XMP & GPS Studio Inspector\n" +
                "• Interactive Split Wipe Slider & Side-by-Side Comparison\n" +
                "• Multi-Threaded Batch Conversion & Resizing",
                "About ViewCraft",
                MessageBoxButton.OK,
                MessageBoxImage.Information);
        }

        private void BtnOpenMap_Click(object sender, RoutedEventArgs e)
        {
            if (_currentMetadata != null && !string.IsNullOrEmpty(_currentMetadata.GoogleMapsUrl))
            {
                Process.Start(new ProcessStartInfo(_currentMetadata.GoogleMapsUrl) { UseShellExecute = true });
            }
        }

        #endregion

        #region Metadata Population

        private void PopulateMetadataView(ImageMetadata meta)
        {
            MetaFormat.Text = meta.Format.ToUpperInvariant();
            MetaDimensions.Text = $"{meta.Width} × {meta.Height} px";
            MetaMegapixels.Text = $"{(meta.Width * (double)meta.Height / 1_000_000.0):F1} MP";
            MetaFileSize.Text = meta.FileSizeBytes > 0 ? $"{meta.FileSizeBytes / (1024.0 * 1024.0):F2} MB" : "—";
            MetaColorSpace.Text = meta.ColorSpace;
            MetaBitDepth.Text = $"{meta.BitDepth}-bit";

            MetaCamera.Text = !string.IsNullOrEmpty(meta.CameraModel) ? $"{meta.CameraMake} {meta.CameraModel}".Trim() : "—";
            MetaLens.Text = !string.IsNullOrEmpty(meta.LensModel) ? meta.LensModel : "—";
            MetaFocal.Text = !string.IsNullOrEmpty(meta.FocalLength) ? meta.FocalLength : "—";
            MetaAperture.Text = !string.IsNullOrEmpty(meta.Aperture) ? meta.Aperture : "—";
            MetaShutter.Text = !string.IsNullOrEmpty(meta.ShutterSpeed) ? meta.ShutterSpeed : "—";
            MetaIso.Text = !string.IsNullOrEmpty(meta.Iso) ? $"ISO {meta.Iso}" : "—";
            MetaDateTaken.Text = !string.IsNullOrEmpty(meta.DateTaken) ? meta.DateTaken : "—";

            if (!string.IsNullOrEmpty(meta.GpsCoordinates))
            {
                MetaGps.Text = meta.GpsCoordinates;
                BtnOpenMap.Visibility = Visibility.Visible;
            }
            else
            {
                MetaGps.Text = "No GPS Data";
                BtnOpenMap.Visibility = Visibility.Collapsed;
            }
        }

        #endregion

        #region View Toggles & Shortcuts

        private void MenuFit_Click(object sender, RoutedEventArgs e) => FitToWindow();
        private void MenuActualSize_Click(object sender, RoutedEventArgs e) => SetZoom(1.0);
        private void MenuZoomIn_Click(object sender, RoutedEventArgs e) => SetZoom(_currentZoom * 1.25);
        private void MenuZoomOut_Click(object sender, RoutedEventArgs e) => SetZoom(_currentZoom * 0.8);

        private void MenuLockZoom_Checked(object sender, RoutedEventArgs e) => _lockZoom = MenuLockZoom.IsChecked;

        private void MenuToggleFilmstrip_Click(object sender, RoutedEventArgs e)
        {
            BorderFilmstrip.Visibility = BorderFilmstrip.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        private void MenuToggleInspector_Click(object sender, RoutedEventArgs e)
        {
            BorderInspector.Visibility = BorderInspector.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        private void MenuToggleMiniMap_Click(object sender, RoutedEventArgs e)
        {
            BorderMiniMap.Visibility = BorderMiniMap.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
        }

        private void MenuFullScreen_Click(object sender, RoutedEventArgs e)
        {
            if (WindowState == WindowState.Maximized && WindowStyle == WindowStyle.None)
            {
                WindowStyle = WindowStyle.SingleBorderWindow;
                WindowState = WindowState.Normal;
            }
            else
            {
                WindowStyle = WindowStyle.None;
                WindowState = WindowState.Maximized;
            }
        }

        private void MenuExit_Click(object sender, RoutedEventArgs e) => Close();

        private void Window_KeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.Left or Key.PageUp:
                    NavigatePrev();
                    e.Handled = true;
                    break;
                case Key.Right or Key.PageDown:
                    NavigateNext();
                    e.Handled = true;
                    break;
                case Key.Home:
                    if (_imageItems.Count > 0) SelectImageIndex(0);
                    e.Handled = true;
                    break;
                case Key.End:
                    if (_imageItems.Count > 0) SelectImageIndex(_imageItems.Count - 1);
                    e.Handled = true;
                    break;
                case Key.D0 or Key.NumPad0:
                    FitToWindow();
                    e.Handled = true;
                    break;
                case Key.D1 or Key.NumPad1:
                    SetZoom(1.0);
                    e.Handled = true;
                    break;
                case Key.OemPlus or Key.Add:
                    SetZoom(_currentZoom * 1.25);
                    e.Handled = true;
                    break;
                case Key.OemMinus or Key.Subtract:
                    SetZoom(_currentZoom * 0.8);
                    e.Handled = true;
                    break;
                case Key.F11:
                    MenuFullScreen_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.F:
                    MenuToggleFilmstrip_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.I:
                    MenuToggleInspector_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.M:
                    MenuToggleMiniMap_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.C:
                    MenuCompare_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.R:
                    if ((Keyboard.Modifiers & ModifierKeys.Shift) == ModifierKeys.Shift)
                        MenuRotateCCW_Click(this, new RoutedEventArgs());
                    else
                        MenuRotateCW_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.H:
                    MenuFlipH_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.V:
                    MenuFlipV_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
                case Key.Space:
                    if (_currentDecoded?.IsAnimated == true)
                        BtnAnimPlayPause_Click(this, new RoutedEventArgs());
                    else
                        MenuSlideshow_Click(this, new RoutedEventArgs());
                    e.Handled = true;
                    break;
            }
        }

        #endregion
    }
}