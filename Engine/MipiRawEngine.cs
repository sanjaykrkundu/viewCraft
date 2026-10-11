using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;

namespace ViewCraft.Engine
{
    public enum MipiFormat
    {
        MipiRaw10,          // 4 pixels in 5 bytes (MIPI CSI-2 standard packed)
        MipiRaw12,          // 2 pixels in 3 bytes (MIPI CSI-2 standard packed)
        MipiRaw14,          // 4 pixels in 7 bytes (MIPI CSI-2 standard packed)
        Raw8,               // 1 pixel per byte
        QualcommPlain10Le,  // Qualcomm Spectra / CamX 10-bit unpacked in 16-bit word (LSB)
        QualcommPlain10Msb, // Qualcomm Spectra / CamX 10-bit unpacked in 16-bit word (MSB)
        QualcommPlain12Le,  // Qualcomm Spectra / CamX 12-bit unpacked in 16-bit word (LSB)
        QualcommPlain12Msb, // Qualcomm Spectra / CamX 12-bit unpacked in 16-bit word (MSB)
        QualcommPlain14Le,  // Qualcomm Spectra / CamX 14-bit unpacked in 16-bit word (LSB)
        QualcommPlain14Msb, // Qualcomm Spectra / CamX 14-bit unpacked in 16-bit word (MSB)
        Raw16Le,            // 16-bit unpacked little-endian (Exynos / Sony / Canon / DNG raw)
        Raw16Be             // 16-bit unpacked big-endian
    }

    public enum BayerPattern
    {
        RGGB,
        BGGR,
        GRBG,
        GBRG,
        Monochrome
    }

    public class MipiRawParameters
    {
        public int Width { get; set; } = 1920;
        public int Height { get; set; } = 1080;
        public MipiFormat Format { get; set; } = MipiFormat.MipiRaw10;
        public BayerPattern Pattern { get; set; } = BayerPattern.RGGB;
        public int BlackLevel { get; set; } = 64;   // typical 10-bit black level
        public int WhiteLevel { get; set; } = 1023; // max for 10-bit
        public double RedGain { get; set; } = 1.8;
        public double BlueGain { get; set; } = 1.5;
        public int RowStride { get; set; } = 0;     // 0 = tight packing
        public int HeaderOffset { get; set; } = 0;  // skip CamX / ISP dump header bytes
    }

    public static class MipiRawEngine
    {
        // Standard sensor resolutions to probe against file size
        public static readonly (int Width, int Height, string Name)[] CommonSensorResolutions = new[]
        {
            (1920, 1080, "Full HD 1080p (2.07 MP)"),
            (1280, 720,  "HD 720p (0.92 MP)"),
            (640, 480,   "VGA (0.31 MP)"),
            (2592, 1944, "OmniVision OV5647 / Pi Cam v1 (5 MP)"),
            (3280, 2464, "Sony IMX219 / Pi Cam v2 (8 MP)"),
            (4056, 3040, "Sony IMX477 / HQ Cam (12.3 MP)"),
            (4624, 3472, "Sony IMX708 / Pi Cam v3 (16 MP)"),
            (3840, 2160, "4K UHD (8.29 MP)"),
            (4000, 3000, "12 MP (4:3)"),
            (5344, 4016, "Sony IMX519 (21.4 MP)"),
            (8192, 6144, "50 MP Sony IMX766/IMX890/LYT800"),
            (8000, 6000, "48 MP Quad Bayer"),
            (9248, 6944, "64 MP Quad Bayer"),
            (12000, 9000,"108 MP Samsung HM2/HM6"),
            (16320, 12240,"200 MP Samsung HP2/HP3")
        };

        /// <summary>
        /// Probes file size to find matching possible sensor resolutions
        /// </summary>
        public static List<(int Width, int Height, MipiFormat Format, string Description)> DetectPossibleGeometries(long fileSize)
        {
            var results = new List<(int Width, int Height, MipiFormat Format, string Description)>();

            foreach (var res in CommonSensorResolutions)
            {
                // 1. MIPI RAW10: (W * H * 5) / 4
                long raw10Size = (long)res.Width * res.Height * 5 / 4;
                if (fileSize == raw10Size || Math.Abs(fileSize - raw10Size) < 512)
                {
                    results.Add((res.Width, res.Height, MipiFormat.MipiRaw10, $"{res.Name} — MIPI RAW10 Packed"));
                }

                // 2. Qualcomm 64-byte row-aligned RAW10
                int qRaw10Stride = ((res.Width * 5 / 4) + 63) & ~63;
                long qRaw10Size = (long)qRaw10Stride * res.Height;
                if (fileSize == qRaw10Size || Math.Abs(fileSize - qRaw10Size) < 512)
                {
                    results.Add((res.Width, res.Height, MipiFormat.MipiRaw10, $"{res.Name} — Qualcomm CamX Aligned RAW10"));
                }

                // 3. MIPI RAW12: (W * H * 3) / 2
                long raw12Size = (long)res.Width * res.Height * 3 / 2;
                if (fileSize == raw12Size || Math.Abs(fileSize - raw12Size) < 512)
                {
                    results.Add((res.Width, res.Height, MipiFormat.MipiRaw12, $"{res.Name} — MIPI RAW12 Packed"));
                }

                // 4. MIPI RAW14: (W * H * 7) / 4
                long raw14Size = (long)res.Width * res.Height * 7 / 4;
                if (fileSize == raw14Size || Math.Abs(fileSize - raw14Size) < 512)
                {
                    results.Add((res.Width, res.Height, MipiFormat.MipiRaw14, $"{res.Name} — MIPI RAW14 Packed"));
                }

                // 5. RAW8: W * H
                long raw8Size = (long)res.Width * res.Height;
                if (fileSize == raw8Size || Math.Abs(fileSize - raw8Size) < 512)
                {
                    results.Add((res.Width, res.Height, MipiFormat.Raw8, $"{res.Name} — RAW8 Unpacked"));
                }

                // 6. RAW16 / Qualcomm Plain16 / Exynos Unpacked: W * H * 2
                long raw16Size = (long)res.Width * res.Height * 2;
                if (fileSize == raw16Size || Math.Abs(fileSize - raw16Size) < 512)
                {
                    results.Add((res.Width, res.Height, MipiFormat.Raw16Le, $"{res.Name} — 16-bit Raw (Exynos/Sony/DNG)"));
                    results.Add((res.Width, res.Height, MipiFormat.QualcommPlain10Le, $"{res.Name} — Qualcomm Plain10 (16-bit LSB)"));
                    results.Add((res.Width, res.Height, MipiFormat.QualcommPlain12Le, $"{res.Name} — Qualcomm Plain12 (16-bit LSB)"));
                    results.Add((res.Width, res.Height, MipiFormat.QualcommPlain14Le, $"{res.Name} — Qualcomm Plain14 (16-bit LSB)"));
                }
            }

            return results;
        }

        /// <summary>
        /// Decodes a MIPI raw file from disk into a full color BitmapSource and master MagickImage
        /// </summary>
        public static async Task<(BitmapSource Bitmap, IMagickImage<ushort> MasterImage)> DecodeMipiRawAsync(
            string filePath, 
            MipiRawParameters parameters)
        {
            byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
            return await Task.Run(() =>
            {
                ushort[] bayerPixels = UnpackTo16Bit(fileBytes, parameters);
                byte[] bgraPixels = DemosaicBayer(bayerPixels, parameters);

                int width = parameters.Width;
                int height = parameters.Height;
                int stride = width * 4;

                var bmp = BitmapSource.Create(
                    width,
                    height,
                    96,
                    96,
                    PixelFormats.Bgra32,
                    null,
                    bgraPixels,
                    stride
                );
                bmp.Freeze();

                var magickImage = new MagickImage();
                var readSettings = new MagickReadSettings
                {
                    Width = (uint)width,
                    Height = (uint)height,
                    Format = MagickFormat.Bgra
                };
                magickImage.Read(bgraPixels, readSettings);

                return (bmp, (IMagickImage<ushort>)magickImage);
            });
        }

        /// <summary>
        /// Unpacks raw byte bitstream into 16-bit linear sensor pixel array
        /// </summary>
        public static ushort[] UnpackTo16Bit(byte[] data, MipiRawParameters p)
        {
            int totalPixels = p.Width * p.Height;
            ushort[] pixels = new ushort[totalPixels];
            int headerOffset = p.HeaderOffset;

            switch (p.Format)
            {
                case MipiFormat.MipiRaw10:
                {
                    // 5 bytes contain 4 pixels (10-bit)
                    int stride10 = p.RowStride > 0 ? p.RowStride : (p.Width * 5) / 4;
                    int outIdx10 = 0;

                    for (int y = 0; y < p.Height; y++)
                    {
                        int rowOffset = headerOffset + y * stride10;
                        for (int x = 0; x < p.Width && (rowOffset + 4) < data.Length; x += 4)
                        {
                            byte b0 = data[rowOffset++];
                            byte b1 = data[rowOffset++];
                            byte b2 = data[rowOffset++];
                            byte b3 = data[rowOffset++];
                            byte b4 = data[rowOffset++];

                            if (outIdx10 < totalPixels) pixels[outIdx10++] = (ushort)((b0 << 2) | ((b4 >> 0) & 0x03));
                            if (outIdx10 < totalPixels) pixels[outIdx10++] = (ushort)((b1 << 2) | ((b4 >> 2) & 0x03));
                            if (outIdx10 < totalPixels) pixels[outIdx10++] = (ushort)((b2 << 2) | ((b4 >> 4) & 0x03));
                            if (outIdx10 < totalPixels) pixels[outIdx10++] = (ushort)((b3 << 2) | ((b4 >> 6) & 0x03));
                        }
                    }
                    break;
                }

                case MipiFormat.MipiRaw12:
                {
                    // 3 bytes contain 2 pixels (12-bit)
                    int stride12 = p.RowStride > 0 ? p.RowStride : (p.Width * 3) / 2;
                    int outIdx12 = 0;

                    for (int y = 0; y < p.Height; y++)
                    {
                        int rowOffset = headerOffset + y * stride12;
                        for (int x = 0; x < p.Width && (rowOffset + 2) < data.Length; x += 2)
                        {
                            byte b0 = data[rowOffset++];
                            byte b1 = data[rowOffset++];
                            byte b2 = data[rowOffset++];

                            if (outIdx12 < totalPixels) pixels[outIdx12++] = (ushort)((b0 << 4) | (b2 & 0x0F));
                            if (outIdx12 < totalPixels) pixels[outIdx12++] = (ushort)((b1 << 4) | ((b2 >> 4) & 0x0F));
                        }
                    }
                    break;
                }

                case MipiFormat.MipiRaw14:
                {
                    // 7 bytes contain 4 pixels (14-bit)
                    int stride14 = p.RowStride > 0 ? p.RowStride : (p.Width * 7) / 4;
                    int outIdx14 = 0;

                    for (int y = 0; y < p.Height; y++)
                    {
                        int rowOffset = headerOffset + y * stride14;
                        for (int x = 0; x < p.Width && (rowOffset + 6) < data.Length; x += 4)
                        {
                            byte b0 = data[rowOffset++];
                            byte b1 = data[rowOffset++];
                            byte b2 = data[rowOffset++];
                            byte b3 = data[rowOffset++];
                            byte b4 = data[rowOffset++];
                            byte b5 = data[rowOffset++];
                            byte b6 = data[rowOffset++];

                            ushort p0 = (ushort)((b0 << 6) | (b4 & 0x3F));
                            ushort p1 = (ushort)((b1 << 6) | ((b4 >> 6) | ((b5 & 0x0F) << 2)));
                            ushort p2 = (ushort)((b2 << 6) | (((b5 >> 4) & 0x0F) | ((b6 & 0x03) << 4)));
                            ushort p3 = (ushort)((b3 << 6) | (b6 >> 2));

                            if (outIdx14 < totalPixels) pixels[outIdx14++] = p0;
                            if (outIdx14 < totalPixels) pixels[outIdx14++] = p1;
                            if (outIdx14 < totalPixels) pixels[outIdx14++] = p2;
                            if (outIdx14 < totalPixels) pixels[outIdx14++] = p3;
                        }
                    }
                    break;
                }

                case MipiFormat.Raw8:
                {
                    int stride8 = p.RowStride > 0 ? p.RowStride : p.Width;
                    for (int y = 0; y < p.Height; y++)
                    {
                        int rowOffset = headerOffset + y * stride8;
                        int outRow = y * p.Width;
                        for (int x = 0; x < p.Width; x++)
                        {
                            int byteIdx = rowOffset + x;
                            if (byteIdx < data.Length)
                            {
                                pixels[outRow + x] = (ushort)(data[byteIdx] << 2); // normalize to 10-bit
                            }
                        }
                    }
                    break;
                }

                case MipiFormat.QualcommPlain10Le:
                case MipiFormat.QualcommPlain12Le:
                case MipiFormat.QualcommPlain14Le:
                case MipiFormat.Raw16Le:
                {
                    int wordStride = p.RowStride > 0 ? (p.RowStride / 2) : p.Width;
                    for (int y = 0; y < p.Height; y++)
                    {
                        int rowOffset = headerOffset + y * wordStride * 2;
                        int outRow = y * p.Width;
                        for (int x = 0; x < p.Width; x++)
                        {
                            int byteIdx = rowOffset + x * 2;
                            if (byteIdx + 1 < data.Length)
                            {
                                ushort word = (ushort)(data[byteIdx] | (data[byteIdx + 1] << 8));
                                if (p.Format == MipiFormat.QualcommPlain10Le) word &= 0x03FF;
                                else if (p.Format == MipiFormat.QualcommPlain12Le) word &= 0x0FFF;
                                else if (p.Format == MipiFormat.QualcommPlain14Le) word &= 0x3FFF;
                                pixels[outRow + x] = word;
                            }
                        }
                    }
                    break;
                }

                case MipiFormat.QualcommPlain10Msb:
                {
                    int wordStride = p.RowStride > 0 ? (p.RowStride / 2) : p.Width;
                    for (int y = 0; y < p.Height; y++)
                    {
                        int rowOffset = headerOffset + y * wordStride * 2;
                        int outRow = y * p.Width;
                        for (int x = 0; x < p.Width; x++)
                        {
                            int byteIdx = rowOffset + x * 2;
                            if (byteIdx + 1 < data.Length)
                            {
                                ushort word = (ushort)(data[byteIdx] | (data[byteIdx + 1] << 8));
                                pixels[outRow + x] = (ushort)(word >> 6);
                            }
                        }
                    }
                    break;
                }

                case MipiFormat.QualcommPlain12Msb:
                {
                    int wordStride = p.RowStride > 0 ? (p.RowStride / 2) : p.Width;
                    for (int y = 0; y < p.Height; y++)
                    {
                        int rowOffset = headerOffset + y * wordStride * 2;
                        int outRow = y * p.Width;
                        for (int x = 0; x < p.Width; x++)
                        {
                            int byteIdx = rowOffset + x * 2;
                            if (byteIdx + 1 < data.Length)
                            {
                                ushort word = (ushort)(data[byteIdx] | (data[byteIdx + 1] << 8));
                                pixels[outRow + x] = (ushort)(word >> 4);
                            }
                        }
                    }
                    break;
                }

                case MipiFormat.QualcommPlain14Msb:
                {
                    int wordStride = p.RowStride > 0 ? (p.RowStride / 2) : p.Width;
                    for (int y = 0; y < p.Height; y++)
                    {
                        int rowOffset = headerOffset + y * wordStride * 2;
                        int outRow = y * p.Width;
                        for (int x = 0; x < p.Width; x++)
                        {
                            int byteIdx = rowOffset + x * 2;
                            if (byteIdx + 1 < data.Length)
                            {
                                ushort word = (ushort)(data[byteIdx] | (data[byteIdx + 1] << 8));
                                pixels[outRow + x] = (ushort)(word >> 2);
                            }
                        }
                    }
                    break;
                }

                case MipiFormat.Raw16Be:
                {
                    int wordStride = p.RowStride > 0 ? (p.RowStride / 2) : p.Width;
                    for (int y = 0; y < p.Height; y++)
                    {
                        int rowOffset = headerOffset + y * wordStride * 2;
                        int outRow = y * p.Width;
                        for (int x = 0; x < p.Width; x++)
                        {
                            int byteIdx = rowOffset + x * 2;
                            if (byteIdx + 1 < data.Length)
                            {
                                pixels[outRow + x] = (ushort)((data[byteIdx] << 8) | data[byteIdx + 1]);
                            }
                        }
                    }
                    break;
                }
            }

            return pixels;
        }

        /// <summary>
        /// High-speed bilinear demosaicing of Bayer CFA matrix to 32-bit BGRA
        /// </summary>
        public static byte[] DemosaicBayer(ushort[] bayer, MipiRawParameters p)
        {
            int width = p.Width;
            int height = p.Height;
            byte[] bgra = new byte[width * height * 4];

            double scale = 255.0 / Math.Max(1, p.WhiteLevel - p.BlackLevel);

            if (p.Pattern == BayerPattern.Monochrome)
            {
                for (int i = 0; i < bayer.Length; i++)
                {
                    double val = Math.Clamp((bayer[i] - p.BlackLevel) * scale, 0, 255);
                    byte b = (byte)val;
                    int idx = i * 4;
                    bgra[idx] = b;
                    bgra[idx + 1] = b;
                    bgra[idx + 2] = b;
                    bgra[idx + 3] = 255;
                }
                return bgra;
            }

            // Bilinear demosaicing with color gains
            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width;
                int prevRow = Math.Max(0, y - 1) * width;
                int nextRow = Math.Min(height - 1, y + 1) * width;

                for (int x = 0; x < width; x++)
                {
                    int prevX = Math.Max(0, x - 1);
                    int nextX = Math.Min(width - 1, x + 1);

                    int cur = rowOffset + x;
                    bool isEvenRow = (y % 2 == 0);
                    bool isEvenCol = (x % 2 == 0);

                    double r = 0, g = 0, b = 0;

                    // Match Bayer pattern
                    switch (p.Pattern)
                    {
                        case BayerPattern.RGGB:
                            if (isEvenRow && isEvenCol) // R
                            {
                                r = bayer[cur];
                                g = (bayer[rowOffset + prevX] + bayer[rowOffset + nextX] + bayer[prevRow + x] + bayer[nextRow + x]) / 4.0;
                                b = (bayer[prevRow + prevX] + bayer[prevRow + nextX] + bayer[nextRow + prevX] + bayer[nextRow + nextX]) / 4.0;
                            }
                            else if (!isEvenRow && !isEvenCol) // B
                            {
                                b = bayer[cur];
                                g = (bayer[rowOffset + prevX] + bayer[rowOffset + nextX] + bayer[prevRow + x] + bayer[nextRow + x]) / 4.0;
                                r = (bayer[prevRow + prevX] + bayer[prevRow + nextX] + bayer[nextRow + prevX] + bayer[nextRow + nextX]) / 4.0;
                            }
                            else // G
                            {
                                g = bayer[cur];
                                if (isEvenRow) // Gr (neighbors R horizontally, B vertically)
                                {
                                    r = (bayer[rowOffset + prevX] + bayer[rowOffset + nextX]) / 2.0;
                                    b = (bayer[prevRow + x] + bayer[nextRow + x]) / 2.0;
                                }
                                else // Gb (neighbors B horizontally, R vertically)
                                {
                                    b = (bayer[rowOffset + prevX] + bayer[rowOffset + nextX]) / 2.0;
                                    r = (bayer[prevRow + x] + bayer[nextRow + x]) / 2.0;
                                }
                            }
                            break;

                        case BayerPattern.BGGR:
                            if (isEvenRow && isEvenCol) // B
                            {
                                b = bayer[cur];
                                g = (bayer[rowOffset + prevX] + bayer[rowOffset + nextX] + bayer[prevRow + x] + bayer[nextRow + x]) / 4.0;
                                r = (bayer[prevRow + prevX] + bayer[prevRow + nextX] + bayer[nextRow + prevX] + bayer[nextRow + nextX]) / 4.0;
                            }
                            else if (!isEvenRow && !isEvenCol) // R
                            {
                                r = bayer[cur];
                                g = (bayer[rowOffset + prevX] + bayer[rowOffset + nextX] + bayer[prevRow + x] + bayer[nextRow + x]) / 4.0;
                                b = (bayer[prevRow + prevX] + bayer[prevRow + nextX] + bayer[nextRow + prevX] + bayer[nextRow + nextX]) / 4.0;
                            }
                            else // G
                            {
                                g = bayer[cur];
                                if (isEvenRow)
                                {
                                    b = (bayer[rowOffset + prevX] + bayer[rowOffset + nextX]) / 2.0;
                                    r = (bayer[prevRow + x] + bayer[nextRow + x]) / 2.0;
                                }
                                else
                                {
                                    r = (bayer[rowOffset + prevX] + bayer[rowOffset + nextX]) / 2.0;
                                    b = (bayer[prevRow + x] + bayer[nextRow + x]) / 2.0;
                                }
                            }
                            break;

                        case BayerPattern.GRBG:
                            if (isEvenRow && isEvenCol) // Gr
                            {
                                g = bayer[cur];
                                r = (bayer[rowOffset + prevX] + bayer[rowOffset + nextX]) / 2.0;
                                b = (bayer[prevRow + x] + bayer[nextRow + x]) / 2.0;
                            }
                            else if (isEvenRow && !isEvenCol) // R
                            {
                                r = bayer[cur];
                                g = (bayer[rowOffset + prevX] + bayer[rowOffset + nextX] + bayer[prevRow + x] + bayer[nextRow + x]) / 4.0;
                                b = (bayer[prevRow + prevX] + bayer[prevRow + nextX] + bayer[nextRow + prevX] + bayer[nextRow + nextX]) / 4.0;
                            }
                            else if (!isEvenRow && isEvenCol) // B
                            {
                                b = bayer[cur];
                                g = (bayer[rowOffset + prevX] + bayer[rowOffset + nextX] + bayer[prevRow + x] + bayer[nextRow + x]) / 4.0;
                                r = (bayer[prevRow + prevX] + bayer[prevRow + nextX] + bayer[nextRow + prevX] + bayer[nextRow + nextX]) / 4.0;
                            }
                            else // Gb
                            {
                                g = bayer[cur];
                                b = (bayer[rowOffset + prevX] + bayer[rowOffset + nextX]) / 2.0;
                                r = (bayer[prevRow + x] + bayer[nextRow + x]) / 2.0;
                            }
                            break;

                        case BayerPattern.GBRG:
                            if (isEvenRow && isEvenCol) // Gb
                            {
                                g = bayer[cur];
                                b = (bayer[rowOffset + prevX] + bayer[rowOffset + nextX]) / 2.0;
                                r = (bayer[prevRow + x] + bayer[nextRow + x]) / 2.0;
                            }
                            else if (isEvenRow && !isEvenCol) // B
                            {
                                b = bayer[cur];
                                g = (bayer[rowOffset + prevX] + bayer[rowOffset + nextX] + bayer[prevRow + x] + bayer[nextRow + x]) / 4.0;
                                r = (bayer[prevRow + prevX] + bayer[prevRow + nextX] + bayer[nextRow + prevX] + bayer[nextRow + nextX]) / 4.0;
                            }
                            else if (!isEvenRow && isEvenCol) // R
                            {
                                r = bayer[cur];
                                g = (bayer[rowOffset + prevX] + bayer[rowOffset + nextX] + bayer[prevRow + x] + bayer[nextRow + x]) / 4.0;
                                b = (bayer[prevRow + prevX] + bayer[prevRow + nextX] + bayer[nextRow + prevX] + bayer[nextRow + nextX]) / 4.0;
                            }
                            else // Gr
                            {
                                g = bayer[cur];
                                r = (bayer[rowOffset + prevX] + bayer[rowOffset + nextX]) / 2.0;
                                b = (bayer[prevRow + x] + bayer[nextRow + x]) / 2.0;
                            }
                            break;

                        default:
                            g = bayer[cur];
                            r = g;
                            b = g;
                            break;
                    }

                    // Apply black level & white balance gains
                    r = Math.Clamp((r - p.BlackLevel) * scale * p.RedGain, 0, 255);
                    g = Math.Clamp((g - p.BlackLevel) * scale, 0, 255);
                    b = Math.Clamp((b - p.BlackLevel) * scale * p.BlueGain, 0, 255);

                    int bgraIdx = cur * 4;
                    bgra[bgraIdx] = (byte)b;
                    bgra[bgraIdx + 1] = (byte)g;
                    bgra[bgraIdx + 2] = (byte)r;
                    bgra[bgraIdx + 3] = 255;
                }
            }

            return bgra;
        }
    }
}
