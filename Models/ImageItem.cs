using System;
using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Windows.Media.Imaging;

namespace ViewCraft.Models
{
    public class ImageItem : INotifyPropertyChanged
    {
        private int _rating;
        private string _colorLabel = "None";
        private BitmapSource? _thumbnail;
        private bool _isLoaded;
        private int _width;
        private int _height;

        public string FilePath { get; set; } = string.Empty;
        public string FileName => Path.GetFileName(FilePath);
        public string DirectoryPath => Path.GetDirectoryName(FilePath) ?? string.Empty;
        public string Extension => Path.GetExtension(FilePath).ToLowerInvariant();
        public long FileSizeBytes { get; set; }

        public int Width
        {
            get => _width;
            set { _width = value; OnPropertyChanged(); OnPropertyChanged(nameof(DimensionsString)); OnPropertyChanged(nameof(MegaPixelsString)); }
        }

        public int Height
        {
            get => _height;
            set { _height = value; OnPropertyChanged(); OnPropertyChanged(nameof(DimensionsString)); OnPropertyChanged(nameof(MegaPixelsString)); }
        }

        public string FormatName { get; set; } = string.Empty;
        public DateTime DateModified { get; set; }

        public BitmapSource? Thumbnail
        {
            get => _thumbnail;
            set { _thumbnail = value; OnPropertyChanged(); }
        }

        public bool IsLoaded
        {
            get => _isLoaded;
            set { _isLoaded = value; OnPropertyChanged(); }
        }

        // DAM Ratings & Labels (Adobe Bridge standard)
        public int Rating
        {
            get => _rating;
            set
            {
                if (_rating != value)
                {
                    _rating = Math.Clamp(value, 0, 5);
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(RatingStars));
                    OnPropertyChanged(nameof(HasRating));
                }
            }
        }

        public string ColorLabel
        {
            get => _colorLabel;
            set
            {
                if (_colorLabel != value)
                {
                    _colorLabel = value;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(ColorLabelHex));
                    OnPropertyChanged(nameof(HasColorLabel));
                }
            }
        }

        public bool HasRating => Rating > 0;
        public string RatingStars => Rating > 0 ? new string('★', Rating) : string.Empty;
        public bool HasColorLabel => !string.IsNullOrEmpty(ColorLabel) && ColorLabel != "None";

        public string ColorLabelHex => ColorLabel switch
        {
            "Red" => "#ef4444",
            "Yellow" => "#eab308",
            "Green" => "#22c55e",
            "Blue" => "#3b82f6",
            "Purple" => "#a855f7",
            _ => "Transparent"
        };

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

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? propName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propName));
        }
    }
}
