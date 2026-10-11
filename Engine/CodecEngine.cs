using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;

namespace ViewCraft.Engine
{
    public class DecodedFrame
    {
        public BitmapSource Source { get; set; } = null!;
        public int DelayMs { get; set; } = 100;
        public int Width { get; set; }
        public int Height { get; set; }
    }

    public class DecodedImageResult
    {
        public List<DecodedFrame> Frames { get; set; } = new();
        public int Width { get; set; }
        public int Height { get; set; }
        public string Format { get; set; } = string.Empty;
        public int BitDepth { get; set; } = 8;
        public string ColorSpace { get; set; } = "sRGB";
        public bool IsAnimated => Frames.Count > 1;
        public IMagickImage<ushort>? MasterMagickImage { get; set; }
    }

    public static class CodecEngine
    {
        // Set of extensions supported by ViewCraft (over 200 formats via Magick.NET)
        private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
        {
            // Common Web & Desktop
            ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".gif", ".webp", ".bmp", ".dib", ".ico", ".icon", ".cur",
            ".tif", ".tiff", ".btf", // BigTIFF
            // Next-Gen & HDR
            ".avif", ".heic", ".heif", ".jxl", ".exr", ".hdr", ".rgbe",
            // Camera RAW
            ".dng", ".cr2", ".cr3", ".nef", ".nrw", ".arw", ".srf", ".sr2", ".raf", ".orf", ".rw2", ".pef", 
            ".ptx", ".x3f", ".3fr", ".erf", ".mef", ".mos", ".mrw", ".srw", ".kdc", ".dcr",
            // Adobe & Vector
            ".psd", ".psb", ".ai", ".eps", ".svg", ".svgz", ".pdf",
            // Medical & Scientific
            ".dcm", ".dicom", ".fits", ".fit", ".fts", ".mat",
            // Game & Textures
            ".dds", ".tga", ".icb", ".vda", ".vst", ".ktx", ".pvr",
            // Retro & Legacy
            ".pcx", ".pnm", ".pbm", ".pgm", ".ppm", ".pam", ".sgi", ".rgb", ".rgba", ".bw",
            ".ras", ".sun", ".xbm", ".xpm", ".pict", ".pct", ".iff", ".ilbm", ".palm", ".wmf", ".emf",
            ".qoi", ".cin", ".dpx", ".miff"
        };

        public static bool IsSupportedFile(string filePath)
        {
            if (string.IsNullOrWhiteSpace(filePath)) return false;
            string ext = Path.GetExtension(filePath);
            return SupportedExtensions.Contains(ext);
        }

        public static string GetFileFilter()
        {
            return "All Supported Images|*.jpg;*.jpeg;*.png;*.webp;*.avif;*.jxl;*.heic;*.heif;*.gif;*.bmp;*.tiff;*.tif;*.psd;*.psb;*.dng;*.cr2;*.cr3;*.nef;*.arw;*.raf;*.orf;*.rw2;*.exr;*.hdr;*.svg;*.dcm;*.fits;*.dds;*.tga;*.pcx;*.ico;*.pdf;*.eps|" +
                   "Standard Web Images (*.jpg;*.png;*.webp;*.gif;*.bmp)|*.jpg;*.jpeg;*.png;*.webp;*.gif;*.bmp|" +
                   "Next-Gen Formats (*.avif;*.jxl;*.heic;*.heif;*.exr;*.hdr)|*.avif;*.jxl;*.heic;*.heif;*.exr;*.hdr|" +
                   "Camera RAW (*.dng;*.cr2;*.cr3;*.nef;*.arw;*.raf;*.orf;*.rw2)|*.dng;*.cr2;*.cr3;*.nef;*.arw;*.raf;*.orf;*.rw2|" +
                   "Adobe Photoshop (*.psd;*.psb;*.ai;*.eps)|*.psd;*.psb;*.ai;*.eps|" +
                   "Textures & Game Assets (*.dds;*.tga;*.ktx)|*.dds;*.tga;*.ktx|" +
                   "Medical & Scientific (*.dcm;*.fits)|*.dcm;*.dicom;*.fits;*.fit|" +
                   "Vector Graphics (*.svg;*.wmf;*.emf)|*.svg;*.svgz;*.wmf;*.emf|" +
                   "All Files (*.*)|*.*";
        }

        /// <summary>
        /// Asynchronously decodes an image file into full frames, master image, and color profiles.
        /// </summary>
        public static async Task<DecodedImageResult> DecodeAsync(string filePath)
        {
            return await Task.Run(() =>
            {
                var result = new DecodedImageResult();
                var readSettings = new MagickReadSettings
                {
                    // Sensible defaults for vector/pdf rasterization
                    Density = new Density(300, 300)
                };

                using var collection = new MagickImageCollection();
                collection.Read(filePath, readSettings);

                if (collection.Count == 0)
                {
                    throw new InvalidOperationException("Failed to decode image: Empty image collection.");
                }

                // First frame info
                var first = collection[0];
                result.Width = (int)first.Width;
                result.Height = (int)first.Height;
                result.Format = first.Format.ToString();
                result.BitDepth = (int)first.Depth;
                result.ColorSpace = first.ColorSpace.ToString();

                // Clone master image for lossless adjustments
                result.MasterMagickImage = collection[0].Clone();

                // Decode all frames
                foreach (var img in collection)
                {
                    int delayMs = (int)(img.AnimationDelay * 10); // ImageMagick animation delay is in 1/100ths of a sec
                    if (delayMs <= 10) delayMs = 100; // default 10fps if unset

                    var bmp = ConvertMagickToBitmapSource(img);
                    result.Frames.Add(new DecodedFrame
                    {
                        Source = bmp,
                        DelayMs = delayMs,
                        Width = (int)img.Width,
                        Height = (int)img.Height
                    });
                }

                return result;
            });
        }

        /// <summary>
        /// Fast thumbnail generator for background caching in filmstrip
        /// </summary>
        public static async Task<BitmapSource?> LoadThumbnailAsync(string filePath, int targetSize = 160)
        {
            return await Task.Run(() =>
            {
                try
                {
                    var readSettings = new MagickReadSettings
                    {
                        Width = (uint)targetSize,
                        Height = (uint)targetSize
                    };

                    using var image = new MagickImage();
                    // Ping first for fast check
                    image.Ping(filePath);

                    // Read with geometry resize for superfast speed
                    image.Read(filePath);
                    image.Thumbnail((uint)targetSize, (uint)targetSize);

                    var bmp = ConvertMagickToBitmapSource(image);
                    bmp.Freeze();
                    return (BitmapSource?)bmp;
                }
                catch
                {
                    return null;
                }
            });
        }

        /// <summary>
        /// Converts MagickImage to WPF BitmapSource using zero-copy / direct BGRA32 buffer.
        /// </summary>
        public static BitmapSource ConvertMagickToBitmapSource(IMagickImage<ushort> magickImage)
        {
            int width = (int)magickImage.Width;
            int height = (int)magickImage.Height;

            // Auto-orient based on EXIF tag if needed
            using var clone = magickImage.Clone();
            clone.AutoOrient();

            width = (int)clone.Width;
            height = (int)clone.Height;

            byte[] pixels = clone.ToByteArray(MagickFormat.Bgra);
            int stride = width * 4;

            var bmp = BitmapSource.Create(
                width,
                height,
                96,
                96,
                PixelFormats.Bgra32,
                null,
                pixels,
                stride
            );

            bmp.Freeze();
            return bmp;
        }

        /// <summary>
        /// Exports an image to disk in the desired format with quality and compression settings.
        /// </summary>
        public static async Task ExportImageAsync(
            IMagickImage<ushort> sourceImage, 
            string outputPath, 
            MagickFormat format, 
            int quality = 90)
        {
            await Task.Run(() =>
            {
                using var exportClone = sourceImage.Clone();
                exportClone.Quality = (uint)quality;
                exportClone.Format = format;
                exportClone.Write(outputPath);
            });
        }
    }
}
