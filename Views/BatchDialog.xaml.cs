using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Windows;
using ImageMagick;
using Microsoft.Win32;
using ViewCraft.Engine;

namespace ViewCraft.Views
{
    public partial class BatchDialog : Window
    {
        private readonly List<string> _filePaths;
        private CancellationTokenSource? _cts;

        public BatchDialog(List<string> filePaths, string defaultOutputDir)
        {
            InitializeComponent();
            _filePaths = filePaths;
            TxtItemCount.Text = $"Selected: {_filePaths.Count} image{(_filePaths.Count == 1 ? "" : "s")}";

            if (!string.IsNullOrEmpty(defaultOutputDir))
            {
                TxtOutputDir.Text = Path.Combine(defaultOutputDir, "Converted");
            }
        }

        private void SldQuality_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
        {
            if (TxtQualityVal != null)
            {
                TxtQualityVal.Text = $"{(int)e.NewValue}%";
            }
        }

        private void ChkResize_Changed(object sender, RoutedEventArgs e)
        {
            if (GridResize != null)
            {
                GridResize.IsEnabled = ChkResize.IsChecked == true;
            }
        }

        private void BtnBrowseOutput_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new OpenFolderDialog
            {
                Title = "Select Destination Folder"
            };

            if (dialog.ShowDialog() == true)
            {
                TxtOutputDir.Text = dialog.FolderName;
            }
        }

        private async void BtnStart_Click(object sender, RoutedEventArgs e)
        {
            if (string.IsNullOrWhiteSpace(TxtOutputDir.Text))
            {
                MessageBox.Show("Please choose a destination directory.", "ViewCraft", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var format = CmbFormat.SelectedIndex switch
            {
                0 => MagickFormat.WebP,
                1 => MagickFormat.Jpeg,
                2 => MagickFormat.Png,
                3 => MagickFormat.Avif,
                4 => MagickFormat.Tiff,
                5 => MagickFormat.Bmp,
                6 => MagickFormat.Ico,
                _ => MagickFormat.WebP
            };

            int.TryParse(TxtMaxWidth.Text, out int maxW);
            int.TryParse(TxtMaxHeight.Text, out int maxH);

            var options = new BatchConvertOptions
            {
                SourceFilePaths = _filePaths,
                OutputDirectory = TxtOutputDir.Text.Trim(),
                TargetFormat = format,
                Quality = (int)SldQuality.Value,
                ResizeEnabled = ChkResize.IsChecked == true,
                MaxWidth = maxW,
                MaxHeight = maxH,
                OverwriteExisting = true
            };

            BtnStart.IsEnabled = false;
            _cts = new CancellationTokenSource();

            var progress = new Progress<BatchProgress>(p =>
            {
                PrgProgress.Value = p.Percentage;
                TxtProgressPercent.Text = $"{(int)p.Percentage}%";
                TxtProgressStatus.Text = $"Converting ({p.Processed}/{p.Total}): {p.CurrentFile}";
            });

            try
            {
                await BatchEngine.RunBatchAsync(options, progress, _cts.Token);
                TxtProgressStatus.Text = $"Completed! All {_filePaths.Count} images processed successfully.";
                MessageBox.Show($"Batch conversion finished!\nSaved to: {options.OutputDirectory}", "ViewCraft Batch Complete", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Error during batch processing: {ex.Message}", "ViewCraft Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                BtnStart.IsEnabled = true;
            }
        }

        private void BtnCancel_Click(object sender, RoutedEventArgs e)
        {
            _cts?.Cancel();
            Close();
        }
    }
}
