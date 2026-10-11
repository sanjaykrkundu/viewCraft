using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ViewCraft.Engine;

namespace ViewCraft.Views
{
    public partial class YuvDialog : Window
    {
        private readonly string _filePath;
        private readonly long _fileSize;
        private readonly List<(int Width, int Height, YuvPixelFormat Format, HardwareStridePreset StridePreset, string Description)> _detectedPresets;
        private readonly DispatcherTimer _previewDebounce = new();

        public YuvParameters ResultParameters { get; private set; } = new();

        public YuvDialog(string filePath)
        {
            InitializeComponent();
            _filePath = filePath;
            var fi = new FileInfo(filePath);
            _fileSize = fi.Exists ? fi.Length : 0;

            TxtFileInfo.Text = $"File: {fi.Name} | Size: {(_fileSize / (1024.0 * 1024.0)):F2} MB ({_fileSize:N0} bytes)";

            _detectedPresets = YuvEngine.DetectPossibleYuvGeometries(_fileSize);

            if (_detectedPresets.Count > 0)
            {
                foreach (var p in _detectedPresets)
                {
                    CmbPresetResolution.Items.Add(p.Description);
                }
                CmbPresetResolution.SelectedIndex = 0;
            }
            else
            {
                // Fallback default presets
                foreach (var res in YuvEngine.CommonYuvResolutions)
                {
                    CmbPresetResolution.Items.Add($"{res.Name} ({res.Width} × {res.Height})");
                }
                CmbPresetResolution.SelectedIndex = 0;
            }

            _previewDebounce.Interval = TimeSpan.FromMilliseconds(120);
            _previewDebounce.Tick += PreviewDebounce_Tick;

            UpdatePreview();
        }

        private void CmbPresetResolution_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            int idx = CmbPresetResolution.SelectedIndex;
            if (idx >= 0 && idx < _detectedPresets.Count)
            {
                var p = _detectedPresets[idx];
                TxtWidth.Text = p.Width.ToString();
                TxtHeight.Text = p.Height.ToString();
                CmbFormat.SelectedIndex = (int)p.Format;
                CmbStridePreset.SelectedIndex = (int)p.StridePreset;
            }
            else if (idx >= 0 && _detectedPresets.Count == 0 && idx < YuvEngine.CommonYuvResolutions.Length)
            {
                var res = YuvEngine.CommonYuvResolutions[idx];
                TxtWidth.Text = res.Width.ToString();
                TxtHeight.Text = res.Height.ToString();
            }
        }

        private void CmbStridePreset_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TxtCustomStride != null)
            {
                bool isCustom = (CmbStridePreset.SelectedIndex == (int)HardwareStridePreset.Custom);
                TxtCustomStride.IsReadOnly = !isCustom;
                if (!isCustom) TxtCustomStride.Text = "0";
            }
            Params_Changed(sender, e);
        }

        private void Params_Changed(object sender, RoutedEventArgs e)
        {
            _previewDebounce.Stop();
            _previewDebounce.Start();
        }

        private void PreviewDebounce_Tick(object? sender, EventArgs e)
        {
            _previewDebounce.Stop();
            UpdatePreview();
        }

        private async void UpdatePreview()
        {
            if (TxtWidth == null || TxtHeight == null || CmbFormat == null || CmbStridePreset == null || CmbColorSpace == null || CmbRange == null)
                return;

            if (!int.TryParse(TxtWidth.Text, out int w) || w <= 0) return;
            if (!int.TryParse(TxtHeight.Text, out int h) || h <= 0) return;
            int.TryParse(TxtCustomStride?.Text, out int customStride);
            int.TryParse(TxtHeaderOffset?.Text, out int headerOffset);

            var p = new YuvParameters
            {
                Width = w,
                Height = h,
                Format = (YuvPixelFormat)Math.Clamp(CmbFormat.SelectedIndex, 0, Enum.GetValues<YuvPixelFormat>().Length - 1),
                ColorSpace = (YuvColorSpace)Math.Clamp(CmbColorSpace.SelectedIndex, 0, 2),
                Range = (YuvRange)Math.Clamp(CmbRange.SelectedIndex, 0, 1),
                StridePreset = (HardwareStridePreset)Math.Clamp(CmbStridePreset.SelectedIndex, 0, 5),
                CustomYStride = customStride,
                HeaderOffset = headerOffset
            };

            try
            {
                var (bmp, _) = await YuvEngine.DecodeYuvAsync(_filePath, p);
                ImgLivePreview.Source = bmp;
                TxtPreviewStatus.Text = $"Decoded {w} × {h} ({p.Format}, Stride={p.GetYStride()} px, {p.ColorSpace})";
            }
            catch (Exception ex)
            {
                TxtPreviewStatus.Text = $"Preview Error: {ex.Message}";
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
            Close();
        }

        private void BtnOpen_Click(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(TxtWidth.Text, out int w) || w <= 0)
            {
                MessageBox.Show("Please enter a valid width.", "ViewCraft", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            if (!int.TryParse(TxtHeight.Text, out int h) || h <= 0)
            {
                MessageBox.Show("Please enter a valid height.", "ViewCraft", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int.TryParse(TxtCustomStride?.Text, out int customStride);
            int.TryParse(TxtHeaderOffset?.Text, out int headerOffset);

            ResultParameters = new YuvParameters
            {
                Width = w,
                Height = h,
                Format = (YuvPixelFormat)Math.Clamp(CmbFormat.SelectedIndex, 0, Enum.GetValues<YuvPixelFormat>().Length - 1),
                ColorSpace = (YuvColorSpace)Math.Clamp(CmbColorSpace.SelectedIndex, 0, 2),
                Range = (YuvRange)Math.Clamp(CmbRange.SelectedIndex, 0, 1),
                StridePreset = (HardwareStridePreset)Math.Clamp(CmbStridePreset.SelectedIndex, 0, 5),
                CustomYStride = customStride,
                HeaderOffset = headerOffset
            };

            DialogResult = true;
            Close();
        }
    }
}
