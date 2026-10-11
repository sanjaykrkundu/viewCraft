using System;
using System.Collections.Generic;

namespace ViewCraft.Models
{
    public class ImageMetadata
    {
        // Basic Technical
        public string Format { get; set; } = "Unknown";
        public string Compression { get; set; } = "None";
        public int Width { get; set; }
        public int Height { get; set; }
        public int BitDepth { get; set; } = 8;
        public string ColorSpace { get; set; } = "sRGB";
        public string IccProfile { get; set; } = string.Empty;
        public int FrameCount { get; set; } = 1;
        public long FileSizeBytes { get; set; }

        // Camera / EXIF
        public string CameraMake { get; set; } = string.Empty;
        public string CameraModel { get; set; } = string.Empty;
        public string LensModel { get; set; } = string.Empty;
        public string FocalLength { get; set; } = string.Empty;
        public string FocalLengthIn35mm { get; set; } = string.Empty;
        public string ShutterSpeed { get; set; } = string.Empty;
        public string Aperture { get; set; } = string.Empty;
        public string Iso { get; set; } = string.Empty;
        public string ExposureBias { get; set; } = string.Empty;
        public string MeteringMode { get; set; } = string.Empty;
        public string Flash { get; set; } = string.Empty;
        public string WhiteBalance { get; set; } = string.Empty;
        public string DateTaken { get; set; } = string.Empty;
        public string Orientation { get; set; } = "1 (Normal)";

        // GPS
        public double? Latitude { get; set; }
        public double? Longitude { get; set; }
        public double? Altitude { get; set; }
        public string GpsCoordinates => Latitude.HasValue && Longitude.HasValue 
            ? $"{Latitude.Value:F5}°, {Longitude.Value:F5}°" 
            : string.Empty;
        public string GoogleMapsUrl => Latitude.HasValue && Longitude.HasValue 
            ? $"https://www.google.com/maps?q={Latitude.Value},{Longitude.Value}" 
            : string.Empty;

        // IPTC / XMP
        public string Title { get; set; } = string.Empty;
        public string Caption { get; set; } = string.Empty;
        public string Artist { get; set; } = string.Empty;
        public string Copyright { get; set; } = string.Empty;
        public string Software { get; set; } = string.Empty;
        public List<string> Keywords { get; set; } = new();

        // Raw property dictionary for all tags
        public Dictionary<string, Dictionary<string, string>> AllTags { get; set; } = new();
    }
}
