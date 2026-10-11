using System;
using System.Collections.Generic;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using ViewCraft.Engine;

namespace ViewCraft.Views
{
    public partial class MipiRawDialog : Window
    {
        private readonly string _filePath;
        private readonly long _fileSize;
        private readonly List<(int Width, int Height, MipiFormat Format, string Description)> _detectedPresets;
        private readonly DispatcherTimer _previewDebounce = new();

        public MipiRawParameters ResultParameters { get; private set; } = new();

        public MipiRawDialog(string filePath)
        {
            InitializeComponent();
            _filePath = filePath;
            var fi = new FileInfo(filePath);
            _fileSize = fi.Exists ? fi.Length : 0;

            TxtFileInfo.Text = $"File: {fi.Name} | Size: {(_fileSize / (1024.0 * 1024.0)):F2} MB ({_fileSize:N0} bytes)";

            _detectedPresets = MipiRawEngine.DetectPossibleGeometries(_fileSize);

            // Populate detected resolutions
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
                CmbPresetResolution.Items.Add("No exact match detected — Enter dimensions manually below");
                CmbPresetResolution.SelectedIndex = 0;
            }

            _previewDebounce.Interval = TimeSpan.FromMilliseconds(150);
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
            }
        }

        private void Params_Changed(object sender, RoutedEventArgs e)
        {
            _previewDebounce.Stop();
            _previewDebounce.Start();
        }

        private void SldBlackLevel_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtBlackLevelVal != null) TxtBlackLevelVal.Text = $"{(int)e.NewValue}";
            _previewDebounce.Stop();
            _previewDebounce.Start();
        }

        private void SldGain_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtRedGainVal != null && SldRedGain != null) TxtRedGainVal.Text = $"{SldRedGain.Value:F1}";
            if (TxtBlueGainVal != null && SldBlueGain != null) TxtBlueGainVal.Text = $"{SldBlueGain.Value:F1}";
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
            if (TxtWidth == null || TxtHeight == null || CmbFormat == null || CmbPattern == null || SldBlackLevel == null) return;

            if (!int.TryParse(TxtWidth.Text, out int w) || w <= 0) return;
            if (!int.TryParse(TxtHeight.Text, out int h) || h <= 0) return;
            int.TryParse(TxtStride?.Text, out int stride);
            int.TryParse(TxtHeaderOffset?.Text, out int headerOffset);

            var p = new MipiRawParameters
            {
                Width = w,
                Height = h,
                Format = (MipiFormat)CmbFormat.SelectedIndex,
                Pattern = (BayerPattern)CmbPattern.SelectedIndex,
                BlackLevel = (int)SldBlackLevel.Value,
                RedGain = SldRedGain.Value,
                BlueGain = SldBlueGain.Value,
                RowStride = stride,
                HeaderOffset = headerOffset
            };

            try
            {
                var (bmp, _) = await MipiRawEngine.DecodeMipiRawAsync(_filePath, p);
                ImgLivePreview.Source = bmp;
                TxtPreviewStatus.Text = $"Demosaiced {w} × {h} ({p.Format}, {p.Pattern})";
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

            int.TryParse(TxtStride?.Text, out int stride);
            int.TryParse(TxtHeaderOffset?.Text, out int headerOffset);

            ResultParameters = new MipiRawParameters
            {
                Width = w,
                Height = h,
                Format = (MipiFormat)CmbFormat.SelectedIndex,
                Pattern = (BayerPattern)CmbPattern.SelectedIndex,
                BlackLevel = (int)SldBlackLevel.Value,
                RedGain = SldRedGain.Value,
                BlueGain = SldBlueGain.Value,
                RowStride = stride,
                HeaderOffset = headerOffset
            };

            DialogResult = true;
            Close();
        }
    }
}
