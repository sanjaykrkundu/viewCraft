using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ImageMagick;

namespace ViewCraft.Engine
{
    public enum YuvPixelFormat
    {
        // Semi-Planar (Android, Qualcomm CamX, Exynos standard)
        NV12,       // Y plane + interleaved UV (4:2:0)
        NV21,       // Y plane + interleaved VU (4:2:0 - Android camera preview)
        NV16,       // Y plane + interleaved UV (4:2:2)
        NV61,       // Y plane + interleaved VU (4:2:2)

        // Planar
        I420,       // Y + U + V (4:2:0 Planar / YUV420P)
        YV12,       // Y + V + U (4:2:0 Planar)
        I422,       // Y + U + V (4:2:2 Planar)
        I444,       // Y + U + V (4:4:4 Planar)

        // Packed
        YUYV,       // YUYV / YUY2 (4:2:2 Packed)
        UYVY,       // UYVY (4:2:2 Packed)
        YVYU,       // YVYU (4:2:2 Packed)
        VYUY,       // VYUY (4:2:2 Packed)

        // 10-bit HDR (Qualcomm Spectra, Exynos HDR10/HEVC)
        P010_LSB,   // 10-bit 4:2:0 Semi-Planar, 16-bit word, LSB aligned [0..1023]
        P010_MSB,   // 10-bit 4:2:0 Semi-Planar, 16-bit word, MSB aligned [bits 15:6]
        TP10        // Qualcomm 10-bit packed: 3 samples per 32-bit dword + 2-bit pad
    }

    public enum YuvColorSpace
    {
        Bt601,  // SDTV / Standard Android camera preview
        Bt709,  // HDTV / 1080p video
        Bt2020  // UHDTV / 4K / 8K HDR
    }

    public enum YuvRange
    {
        Full,   // PC / JPEG / ISP Raw Range: Y in [0, 255], UV in [0, 255]
        Video   // Studio / Limited Range: Y in [16, 235], UV in [16, 240]
    }

    public enum HardwareStridePreset
    {
        Linear,             // Tight stride = width
        Qualcomm64,         // Qualcomm CamX: align(W, 64), align(H, 32)
        Qualcomm128,        // Qualcomm Adreno / Spectra: align(W, 128), align(H, 32)
        Exynos16,           // Samsung Exynos: align(W, 16), align(H, 16)
        Exynos64,           // Samsung Exynos: align(W, 64), align(H, 32)
        Custom              // User specified stride
    }

    public class YuvParameters
    {
        public int Width { get; set; } = 1920;
        public int Height { get; set; } = 1080;
        public YuvPixelFormat Format { get; set; } = YuvPixelFormat.NV12;
        public YuvColorSpace ColorSpace { get; set; } = YuvColorSpace.Bt709;
        public YuvRange Range { get; set; } = YuvRange.Full;
        public HardwareStridePreset StridePreset { get; set; } = HardwareStridePreset.Linear;
        public int CustomYStride { get; set; } = 0;
        public int CustomUVStride { get; set; } = 0;
        public int HeaderOffset { get; set; } = 0; // Skip ISP dump header

        public int GetYStride()
        {
            if (CustomYStride > 0) return CustomYStride;
            return StridePreset switch
            {
                HardwareStridePreset.Qualcomm64 => Align(Width, 64),
                HardwareStridePreset.Qualcomm128 => Align(Width, 128),
                HardwareStridePreset.Exynos16 => Align(Width, 16),
                HardwareStridePreset.Exynos64 => Align(Width, 64),
                _ => Width
            };
        }

        public int GetScanlineHeight()
        {
            return StridePreset switch
            {
                HardwareStridePreset.Qualcomm64 => Align(Height, 32),
                HardwareStridePreset.Qualcomm128 => Align(Height, 32),
                HardwareStridePreset.Exynos16 => Align(Height, 16),
                HardwareStridePreset.Exynos64 => Align(Height, 32),
                _ => Height
            };
        }

        private static int Align(int val, int alignment)
        {
            return (val + alignment - 1) & ~(alignment - 1);
        }
    }

    public static class YuvEngine
    {
        public static readonly (int Width, int Height, string Name)[] CommonYuvResolutions = new[]
        {
            (1920, 1080, "Full HD 1080p (16:9)"),
            (1280, 720,  "HD 720p (16:9)"),
            (3840, 2160, "4K UHD (16:9)"),
            (2560, 1440, "2K QHD (16:9)"),
            (4096, 2160, "4K DCI (Cinema)"),
            (7680, 4320, "8K UHD (16:9)"),
            (640, 480,   "VGA (4:3)"),
            (720, 480,   "NTSC DVD (3:2)"),
            (720, 576,   "PAL (5:4)"),
            (1280, 960,  "1.2 MP (4:3)"),
            (1600, 1200, "2 MP UXGA (4:3)"),
            (2048, 1536, "3 MP QXGA (4:3)"),
            (2592, 1944, "5 MP (4:3)"),
            (3264, 2448, "8 MP (4:3)"),
            (4000, 3000, "12 MP (4:3)"),
            (4624, 3472, "16 MP (4:3)"),
            (8000, 6000, "48 MP Quad Bayer"),
            (12000, 9000,"108 MP Sensor"),
            (16320, 12240,"200 MP Samsung HP2/HP3")
        };

        public static List<(int Width, int Height, YuvPixelFormat Format, HardwareStridePreset StridePreset, string Description)> DetectPossibleYuvGeometries(long fileSize)
        {
            var results = new List<(int Width, int Height, YuvPixelFormat Format, HardwareStridePreset StridePreset, string Description)>();

            foreach (var res in CommonYuvResolutions)
            {
                long pixels = (long)res.Width * res.Height;

                // 1. Semi-Planar & Planar 4:2:0 (NV12, NV21, I420, YV12) = 1.5 bytes per pixel
                long yuv420Size = pixels * 3 / 2;
                if (fileSize == yuv420Size || Math.Abs(fileSize - yuv420Size) < 512)
                {
                    results.Add((res.Width, res.Height, YuvPixelFormat.NV12, HardwareStridePreset.Linear, $"{res.Name} — NV12 (Android/Qualcomm/Exynos 4:2:0)"));
                    results.Add((res.Width, res.Height, YuvPixelFormat.NV21, HardwareStridePreset.Linear, $"{res.Name} — NV21 (Android Camera Preview 4:2:0)"));
                    results.Add((res.Width, res.Height, YuvPixelFormat.I420, HardwareStridePreset.Linear, $"{res.Name} — I420 / YUV420P (Planar 4:2:0)"));
                }

                // 2. Qualcomm 64-byte aligned NV12/NV21
                int q64Stride = (res.Width + 63) & ~63;
                int q64Scanlines = (res.Height + 31) & ~31;
                long q64Size = (long)q64Stride * q64Scanlines * 3 / 2;
                if (fileSize == q64Size || Math.Abs(fileSize - q64Size) < 512)
                {
                    results.Add((res.Width, res.Height, YuvPixelFormat.NV12, HardwareStridePreset.Qualcomm64, $"{res.Name} — Qualcomm CamX 64-byte Stride NV12"));
                    results.Add((res.Width, res.Height, YuvPixelFormat.NV21, HardwareStridePreset.Qualcomm64, $"{res.Name} — Qualcomm CamX 64-byte Stride NV21"));
                }

                // 3. 4:2:2 Packed (YUYV/YUY2, UYVY) = 2.0 bytes per pixel
                long yuv422Size = pixels * 2;
                if (fileSize == yuv422Size || Math.Abs(fileSize - yuv422Size) < 512)
                {
                    results.Add((res.Width, res.Height, YuvPixelFormat.YUYV, HardwareStridePreset.Linear, $"{res.Name} — YUYV / YUY2 (Packed 4:2:2 Webcam/V4L2)"));
                    results.Add((res.Width, res.Height, YuvPixelFormat.UYVY, HardwareStridePreset.Linear, $"{res.Name} — UYVY (Packed 4:2:2)"));
                }

                // 4. 10-bit Semi-Planar (P010) = 3.0 bytes per pixel (16-bit word * 1.5)
                long p010Size = pixels * 3;
                if (fileSize == p010Size || Math.Abs(fileSize - p010Size) < 512)
                {
                    results.Add((res.Width, res.Height, YuvPixelFormat.P010_LSB, HardwareStridePreset.Linear, $"{res.Name} — P010 10-bit HDR (Qualcomm/Exynos LSB)"));
                    results.Add((res.Width, res.Height, YuvPixelFormat.P010_MSB, HardwareStridePreset.Linear, $"{res.Name} — P010 10-bit HDR (MSB Aligned)"));
                }

                // 5. Qualcomm TP10 (Tightly Packed 10-bit: 4 bytes per 3 pixels = 2.0 bytes per pixel for 4:2:0)
                // Y plane: (W * 4 / 3) * H bytes; UV plane: (W * 4 / 3) * (H / 2) bytes = W * H * 2.0 bytes
                long tp10Size = pixels * 2;
                if (fileSize == tp10Size || Math.Abs(fileSize - tp10Size) < 512)
                {
                    results.Add((res.Width, res.Height, YuvPixelFormat.TP10, HardwareStridePreset.Linear, $"{res.Name} — Qualcomm TP10 (10-bit Packed CamX/Spectra)"));
                }

                // 6. 4:4:4 Planar = 3.0 bytes per pixel
                long yuv444Size = pixels * 3;
                if (fileSize == yuv444Size || Math.Abs(fileSize - yuv444Size) < 512)
                {
                    results.Add((res.Width, res.Height, YuvPixelFormat.I444, HardwareStridePreset.Linear, $"{res.Name} — I444 / YUV444P (Full Chroma 4:4:4)"));
                }
            }

            return results;
        }

        public static async Task<(BitmapSource Bitmap, IMagickImage<ushort> MasterImage)> DecodeYuvAsync(
            string filePath,
            YuvParameters p)
        {
            byte[] fileBytes = await File.ReadAllBytesAsync(filePath);
            return await Task.Run(() =>
            {
                byte[] bgraPixels = ConvertYuvToBgra(fileBytes, p);

                int width = p.Width;
                int height = p.Height;
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

        public static byte[] ConvertYuvToBgra(byte[] data, YuvParameters p)
        {
            int width = p.Width;
            int height = p.Height;
            byte[] bgra = new byte[width * height * 4];

            int yStride = p.GetYStride();
            int scanlineHeight = p.GetScanlineHeight();
            int headerOffset = p.HeaderOffset;

            // Pre-calculate color matrix factors
            // BT.601, BT.709, BT.2020 coefficients
            double rV, gU, gV, bU;
            switch (p.ColorSpace)
            {
                case YuvColorSpace.Bt709:
                    rV = 1.5748; gU = 0.1873; gV = 0.4681; bU = 1.8556;
                    break;
                case YuvColorSpace.Bt2020:
                    rV = 1.4746; gU = 0.1645; gV = 0.5714; bU = 1.8814;
                    break;
                case YuvColorSpace.Bt601:
                default:
                    rV = 1.4020; gU = 0.3441; gV = 0.7141; bU = 1.7720;
                    break;
            }

            bool isFullRange = (p.Range == YuvRange.Full);

            switch (p.Format)
            {
                case YuvPixelFormat.NV12:
                case YuvPixelFormat.NV21:
                    DecodeSemiPlanar420(data, bgra, width, height, yStride, scanlineHeight, headerOffset, p.Format == YuvPixelFormat.NV21, rV, gU, gV, bU, isFullRange);
                    break;

                case YuvPixelFormat.I420:
                case YuvPixelFormat.YV12:
                    DecodePlanar420(data, bgra, width, height, yStride, scanlineHeight, headerOffset, p.Format == YuvPixelFormat.YV12, rV, gU, gV, bU, isFullRange);
                    break;

                case YuvPixelFormat.YUYV:
                case YuvPixelFormat.UYVY:
                case YuvPixelFormat.YVYU:
                case YuvPixelFormat.VYUY:
                    DecodePacked422(data, bgra, width, height, headerOffset, p.Format, rV, gU, gV, bU, isFullRange);
                    break;

                case YuvPixelFormat.P010_LSB:
                case YuvPixelFormat.P010_MSB:
                    DecodeP010(data, bgra, width, height, yStride, scanlineHeight, headerOffset, p.Format == YuvPixelFormat.P010_MSB, rV, gU, gV, bU, isFullRange);
                    break;

                case YuvPixelFormat.TP10:
                    DecodeQualcommTP10(data, bgra, width, height, yStride, scanlineHeight, headerOffset, rV, gU, gV, bU, isFullRange);
                    break;

                case YuvPixelFormat.I444:
                    DecodePlanar444(data, bgra, width, height, headerOffset, rV, gU, gV, bU, isFullRange);
                    break;

                case YuvPixelFormat.NV16:
                case YuvPixelFormat.NV61:
                    DecodeSemiPlanar422(data, bgra, width, height, yStride, scanlineHeight, headerOffset, p.Format == YuvPixelFormat.NV61, rV, gU, gV, bU, isFullRange);
                    break;
            }

            return bgra;
        }

        private static void ConvertPixel(double yVal, double uVal, double vVal, double rV, double gU, double gV, double bU, bool isFullRange, out byte rOut, out byte gOut, out byte bOut)
        {
            double yNorm, uNorm, vNorm;
            if (isFullRange)
            {
                yNorm = yVal;
                uNorm = uVal - 128.0;
                vNorm = vVal - 128.0;
            }
            else
            {
                // Video Range: Y in [16..235], UV in [16..240]
                yNorm = (yVal - 16.0) * (255.0 / 219.0);
                uNorm = (uVal - 128.0) * (255.0 / 224.0);
                vNorm = (vVal - 128.0) * (255.0 / 224.0);
            }

            double r = yNorm + rV * vNorm;
            double g = yNorm - gU * uNorm - gV * vNorm;
            double b = yNorm + bU * uNorm;

            rOut = (byte)Math.Clamp(r, 0, 255);
            gOut = (byte)Math.Clamp(g, 0, 255);
            bOut = (byte)Math.Clamp(b, 0, 255);
        }

        private static void DecodeSemiPlanar420(
            byte[] data, byte[] bgra, int width, int height,
            int yStride, int scanlines, int headerOffset,
            bool isNv21, double rV, double gU, double gV, double bU, bool isFullRange)
        {
            int uvPlaneOffset = headerOffset + yStride * scanlines;

            for (int y = 0; y < height; y++)
            {
                int yRowStart = headerOffset + y * yStride;
                int uvRowStart = uvPlaneOffset + (y / 2) * yStride;
                int bgraRowStart = y * width * 4;

                for (int x = 0; x < width; x++)
                {
                    int yIdx = yRowStart + x;
                    int uvIdx = uvRowStart + (x / 2) * 2;

                    byte yVal = (yIdx < data.Length) ? data[yIdx] : (byte)0;
                    byte uVal, vVal;

                    if (isNv21)
                    {
                        // VU interleaved
                        vVal = (uvIdx < data.Length) ? data[uvIdx] : (byte)128;
                        uVal = (uvIdx + 1 < data.Length) ? data[uvIdx + 1] : (byte)128;
                    }
                    else
                    {
                        // UV interleaved (NV12)
                        uVal = (uvIdx < data.Length) ? data[uvIdx] : (byte)128;
                        vVal = (uvIdx + 1 < data.Length) ? data[uvIdx + 1] : (byte)128;
                    }

                    ConvertPixel(yVal, uVal, vVal, rV, gU, gV, bU, isFullRange, out byte r, out byte g, out byte b);

                    int outIdx = bgraRowStart + x * 4;
                    bgra[outIdx] = b;
                    bgra[outIdx + 1] = g;
                    bgra[outIdx + 2] = r;
                    bgra[outIdx + 3] = 255;
                }
            }
        }

        private static void DecodePlanar420(
            byte[] data, byte[] bgra, int width, int height,
            int yStride, int scanlines, int headerOffset,
            bool isYv12, double rV, double gU, double gV, double bU, bool isFullRange)
        {
            int chromaWidth = width / 2;
            int chromaHeight = height / 2;
            int ySize = yStride * scanlines;
            int chromaStride = yStride / 2;
            int chromaPlaneSize = chromaStride * (scanlines / 2);

            int plane1Offset = headerOffset + ySize;
            int plane2Offset = plane1Offset + chromaPlaneSize;

            int uPlaneOffset = isYv12 ? plane2Offset : plane1Offset;
            int vPlaneOffset = isYv12 ? plane1Offset : plane2Offset;

            for (int y = 0; y < height; y++)
            {
                int yRowStart = headerOffset + y * yStride;
                int chromaY = y / 2;
                int uRowStart = uPlaneOffset + chromaY * chromaStride;
                int vRowStart = vPlaneOffset + chromaY * chromaStride;
                int bgraRowStart = y * width * 4;

                for (int x = 0; x < width; x++)
                {
                    int yIdx = yRowStart + x;
                    int chromaX = x / 2;
                    int uIdx = uRowStart + chromaX;
                    int vIdx = vRowStart + chromaX;

                    byte yVal = (yIdx < data.Length) ? data[yIdx] : (byte)0;
                    byte uVal = (uIdx < data.Length) ? data[uIdx] : (byte)128;
                    byte vVal = (vIdx < data.Length) ? data[vIdx] : (byte)128;

                    ConvertPixel(yVal, uVal, vVal, rV, gU, gV, bU, isFullRange, out byte r, out byte g, out byte b);

                    int outIdx = bgraRowStart + x * 4;
                    bgra[outIdx] = b;
                    bgra[outIdx + 1] = g;
                    bgra[outIdx + 2] = r;
                    bgra[outIdx + 3] = 255;
                }
            }
        }

        private static void DecodePacked422(
            byte[] data, byte[] bgra, int width, int height,
            int headerOffset, YuvPixelFormat fmt,
            double rV, double gU, double gV, double bU, bool isFullRange)
        {
            int rowBytes = width * 2;

            for (int y = 0; y < height; y++)
            {
                int rowStart = headerOffset + y * rowBytes;
                int bgraRowStart = y * width * 4;

                for (int x = 0; x < width; x += 2)
                {
                    int pIdx = rowStart + x * 2;
                    if (pIdx + 3 >= data.Length) break;

                    byte b0 = data[pIdx];
                    byte b1 = data[pIdx + 1];
                    byte b2 = data[pIdx + 2];
                    byte b3 = data[pIdx + 3];

                    byte y0, y1, u, v;
                    switch (fmt)
                    {
                        case YuvPixelFormat.YUYV: // Y0 U0 Y1 V0
                            y0 = b0; u = b1; y1 = b2; v = b3;
                            break;
                        case YuvPixelFormat.UYVY: // U0 Y0 V0 Y1
                            u = b0; y0 = b1; v = b2; y1 = b3;
                            break;
                        case YuvPixelFormat.YVYU: // Y0 V0 Y1 U0
                            y0 = b0; v = b1; y1 = b2; u = b3;
                            break;
                        case YuvPixelFormat.VYUY: // V0 Y0 U0 Y1
                            v = b0; y0 = b1; u = b2; y1 = b3;
                            break;
                        default:
                            y0 = b0; u = b1; y1 = b2; v = b3;
                            break;
                    }

                    // Pixel 0
                    ConvertPixel(y0, u, v, rV, gU, gV, bU, isFullRange, out byte r0, out byte g0, out byte b_0);
                    int out0 = bgraRowStart + x * 4;
                    bgra[out0] = b_0;
                    bgra[out0 + 1] = g0;
                    bgra[out0 + 2] = r0;
                    bgra[out0 + 3] = 255;

                    // Pixel 1
                    if (x + 1 < width)
                    {
                        ConvertPixel(y1, u, v, rV, gU, gV, bU, isFullRange, out byte r1, out byte g1, out byte b_1);
                        int out1 = bgraRowStart + (x + 1) * 4;
                        bgra[out1] = b_1;
                        bgra[out1 + 1] = g1;
                        bgra[out1 + 2] = r1;
                        bgra[out1 + 3] = 255;
                    }
                }
            }
        }

        private static void DecodeP010(
            byte[] data, byte[] bgra, int width, int height,
            int yStride, int scanlines, int headerOffset,
            bool isMsbAligned, double rV, double gU, double gV, double bU, bool isFullRange)
        {
            // P010: each sample is 16-bit word (2 bytes)
            int yWordStride = Math.Max(width, yStride);
            int uvPlaneByteOffset = headerOffset + yWordStride * scanlines * 2;

            for (int y = 0; y < height; y++)
            {
                int yRowStart = headerOffset + y * yWordStride * 2;
                int uvRowStart = uvPlaneByteOffset + (y / 2) * yWordStride * 2;
                int bgraRowStart = y * width * 4;

                for (int x = 0; x < width; x++)
                {
                    int yByteIdx = yRowStart + x * 2;
                    int uvByteIdx = uvRowStart + (x / 2) * 4;

                    ushort yWord = 0, uWord = 512, vWord = 512;
                    if (yByteIdx + 1 < data.Length)
                    {
                        yWord = (ushort)(data[yByteIdx] | (data[yByteIdx + 1] << 8));
                    }
                    if (uvByteIdx + 3 < data.Length)
                    {
                        uWord = (ushort)(data[uvByteIdx] | (data[uvByteIdx + 1] << 8));
                        vWord = (ushort)(data[uvByteIdx + 2] | (data[uvByteIdx + 3] << 8));
                    }

                    if (isMsbAligned)
                    {
                        yWord >>= 6;
                        uWord >>= 6;
                        vWord >>= 6;
                    }
                    else
                    {
                        yWord &= 0x3FF;
                        uWord &= 0x3FF;
                        vWord &= 0x3FF;
                    }

                    // Scale 10-bit [0..1023] to [0..255]
                    double yVal = yWord * (255.0 / 1023.0);
                    double uVal = uWord * (255.0 / 1023.0);
                    double vVal = vWord * (255.0 / 1023.0);

                    ConvertPixel(yVal, uVal, vVal, rV, gU, gV, bU, isFullRange, out byte r, out byte g, out byte b);

                    int outIdx = bgraRowStart + x * 4;
                    bgra[outIdx] = b;
                    bgra[outIdx + 1] = g;
                    bgra[outIdx + 2] = r;
                    bgra[outIdx + 3] = 255;
                }
            }
        }

        private static void DecodeQualcommTP10(
            byte[] data, byte[] bgra, int width, int height,
            int yStride, int scanlines, int headerOffset,
            double rV, double gU, double gV, double bU, bool isFullRange)
        {
            // Qualcomm TP10: 3 10-bit samples packed in 32-bit dword (4 bytes)
            // Stride for Y plane in bytes: (width + 2) / 3 * 4
            int yByteStride = (yStride + 2) / 3 * 4;
            int uvPlaneByteOffset = headerOffset + yByteStride * scanlines;

            for (int y = 0; y < height; y++)
            {
                int yRowStart = headerOffset + y * yByteStride;
                int uvRowStart = uvPlaneByteOffset + (y / 2) * yByteStride;
                int bgraRowStart = y * width * 4;

                for (int x = 0; x < width; x++)
                {
                    // Unpack Y sample
                    int yGroup = x / 3;
                    int ySub = x % 3;
                    int yDwordOffset = yRowStart + yGroup * 4;

                    ushort yWord = 0;
                    if (yDwordOffset + 3 < data.Length)
                    {
                        uint dword = (uint)(data[yDwordOffset] | (data[yDwordOffset + 1] << 8) | (data[yDwordOffset + 2] << 16) | (data[yDwordOffset + 3] << 24));
                        yWord = (ushort)((dword >> (ySub * 10)) & 0x3FF);
                    }

                    // Unpack UV sample (UV interleaved)
                    int uvPairIdx = x / 2; // pair index
                    int uvSampleIdx = uvPairIdx * 2; // 0=U, 1=V, 2=U, 3=V...
                    int uDwordGroup = uvSampleIdx / 3;
                    int uSub = uvSampleIdx % 3;
                    int vSampleIdx = uvSampleIdx + 1;
                    int vDwordGroup = vSampleIdx / 3;
                    int vSub = vSampleIdx % 3;

                    ushort uWord = 512, vWord = 512;
                    int uDwordOffset = uvRowStart + uDwordGroup * 4;
                    if (uDwordOffset + 3 < data.Length)
                    {
                        uint dwordU = (uint)(data[uDwordOffset] | (data[uDwordOffset + 1] << 8) | (data[uDwordOffset + 2] << 16) | (data[uDwordOffset + 3] << 24));
                        uWord = (ushort)((dwordU >> (uSub * 10)) & 0x3FF);
                    }
                    int vDwordOffset = uvRowStart + vDwordGroup * 4;
                    if (vDwordOffset + 3 < data.Length)
                    {
                        uint dwordV = (uint)(data[vDwordOffset] | (data[vDwordOffset + 1] << 8) | (data[vDwordOffset + 2] << 16) | (data[vDwordOffset + 3] << 24));
                        vWord = (ushort)((dwordV >> (vSub * 10)) & 0x3FF);
                    }

                    double yVal = yWord * (255.0 / 1023.0);
                    double uVal = uWord * (255.0 / 1023.0);
                    double vVal = vWord * (255.0 / 1023.0);

                    ConvertPixel(yVal, uVal, vVal, rV, gU, gV, bU, isFullRange, out byte r, out byte g, out byte b);

                    int outIdx = bgraRowStart + x * 4;
                    bgra[outIdx] = b;
                    bgra[outIdx + 1] = g;
                    bgra[outIdx + 2] = r;
                    bgra[outIdx + 3] = 255;
                }
            }
        }

        private static void DecodeSemiPlanar422(
            byte[] data, byte[] bgra, int width, int height,
            int yStride, int scanlines, int headerOffset,
            bool isNv61, double rV, double gU, double gV, double bU, bool isFullRange)
        {
            int uvPlaneOffset = headerOffset + yStride * scanlines;

            for (int y = 0; y < height; y++)
            {
                int yRowStart = headerOffset + y * yStride;
                int uvRowStart = uvPlaneOffset + y * yStride; // 4:2:2 has full vertical chroma resolution
                int bgraRowStart = y * width * 4;

                for (int x = 0; x < width; x++)
                {
                    int yIdx = yRowStart + x;
                    int uvIdx = uvRowStart + (x / 2) * 2;

                    byte yVal = (yIdx < data.Length) ? data[yIdx] : (byte)0;
                    byte uVal, vVal;

                    if (isNv61)
                    {
                        vVal = (uvIdx < data.Length) ? data[uvIdx] : (byte)128;
                        uVal = (uvIdx + 1 < data.Length) ? data[uvIdx + 1] : (byte)128;
                    }
                    else
                    {
                        uVal = (uvIdx < data.Length) ? data[uvIdx] : (byte)128;
                        vVal = (uvIdx + 1 < data.Length) ? data[uvIdx + 1] : (byte)128;
                    }

                    ConvertPixel(yVal, uVal, vVal, rV, gU, gV, bU, isFullRange, out byte r, out byte g, out byte b);

                    int outIdx = bgraRowStart + x * 4;
                    bgra[outIdx] = b;
                    bgra[outIdx + 1] = g;
                    bgra[outIdx + 2] = r;
                    bgra[outIdx + 3] = 255;
                }
            }
        }

        private static void DecodePlanar444(
            byte[] data, byte[] bgra, int width, int height,
            int headerOffset, double rV, double gU, double gV, double bU, bool isFullRange)
        {
            int planeSize = width * height;
            int uOffset = headerOffset + planeSize;
            int vOffset = uOffset + planeSize;

            for (int y = 0; y < height; y++)
            {
                int rowOffset = y * width;
                int bgraRowStart = y * width * 4;

                for (int x = 0; x < width; x++)
                {
                    int idx = rowOffset + x;
                    byte yVal = (headerOffset + idx < data.Length) ? data[headerOffset + idx] : (byte)0;
                    byte uVal = (uOffset + idx < data.Length) ? data[uOffset + idx] : (byte)128;
                    byte vVal = (vOffset + idx < data.Length) ? data[vOffset + idx] : (byte)128;

                    ConvertPixel(yVal, uVal, vVal, rV, gU, gV, bU, isFullRange, out byte r, out byte g, out byte b);

                    int outIdx = bgraRowStart + x * 4;
                    bgra[outIdx] = b;
                    bgra[outIdx + 1] = g;
                    bgra[outIdx + 2] = r;
                    bgra[outIdx + 3] = 255;
                }
            }
        }
    }
}
