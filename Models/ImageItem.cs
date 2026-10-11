using System;
using System.IO;
using System.Windows.Media.Imaging;

namespace ViewCraft.Models
{
    public class ImageItem
    {
        public string FilePath { get; set; } = string.Empty;
        public string FileName => Path.GetFileName(FilePath);
        public string DirectoryPath => Path.GetDirectoryName(FilePath) ?? string.Empty;
        public string Extension => Path.GetExtension(FilePath).ToLowerInvariant();
        public long FileSizeBytes { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public string FormatName { get; set; } = string.Empty;
        public DateTime DateModified { get; set; }
        public BitmapSource? Thumbnail { get; set; }
        public bool IsLoaded { get; set; }

        public string DimensionsString => Width > 0 && Height > 0 ? $"{Width} × {Height} px" : "Loading...";
        public string MegaPixelsString => Width > 0 && Height > 0 ? $"{(Width * (double)Height / 1_000_000.0):F1} MP" : string.Empty;

        public string FileSizeFormatted
        {
            get
            {
                if (FileSizeBytes < 1024) return $"{FileSizeBytes} B";
                if (FileSizeBytes < 1024 * 1024) return $"{FileSizeBytes / 1024.0:F1} KB";
                if (FileSizeBytes < 1024 * 1024 * 1024) return $"{FileSizeBytes / (1024.0 * 1024.0):F2} MB";
                return $"{FileSizeBytes / (1024.0 * 1024.0 * 1024.0):F2} GB";
            }
        }
    }
}
