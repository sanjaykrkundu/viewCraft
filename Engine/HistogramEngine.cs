using System;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;

namespace ViewCraft.Engine
{
    public class HistogramData
    {
        public int[] RedBins { get; set; } = new int[256];
        public int[] GreenBins { get; set; } = new int[256];
        public int[] BlueBins { get; set; } = new int[256];
        public int[] LumaBins { get; set; } = new int[256];
        public int MaxBinCount { get; set; } = 1;

        public double MeanLuma { get; set; }
        public double StdDevLuma { get; set; }
    }

    public static class HistogramEngine
    {
        public static async Task<HistogramData> ComputeHistogramAsync(BitmapSource bitmap)
        {
            return await Task.Run(() =>
            {
                var data = new HistogramData();
                int width = bitmap.PixelWidth;
                int height = bitmap.PixelHeight;

                if (width == 0 || height == 0) return data;

                // Format conversion to Bgra32 if not already
                BitmapSource formattedBitmap = bitmap;
                if (bitmap.Format != System.Windows.Media.PixelFormats.Bgra32)
                {
                    formattedBitmap = new FormatConvertedBitmap(bitmap, System.Windows.Media.PixelFormats.Bgra32, null, 0);
                }

                int stride = width * 4;
                byte[] pixels = new byte[stride * height];
                formattedBitmap.CopyPixels(pixels, stride, 0);

                long totalLuma = 0;
                long totalPixels = width * (long)height;

                // Subsample for superfast real-time responsiveness if image is huge (> 2 megapixels)
                int step = 1;
                if (totalPixels > 2_000_000)
                {
                    step = (int)Math.Sqrt(totalPixels / 1_000_000.0);
                    if (step < 1) step = 1;
                }

                int countedPixels = 0;
                for (int y = 0; y < height; y += step)
                {
                    int rowOffset = y * stride;
                    for (int x = 0; x < width; x += step)
                    {
                        int index = rowOffset + (x * 4);
                        byte b = pixels[index];
                        byte g = pixels[index + 1];
                        byte r = pixels[index + 2];

                        // Rec. 709 Luma
                        byte luma = (byte)((0.2126 * r) + (0.7152 * g) + (0.0722 * b));

                        data.RedBins[r]++;
                        data.GreenBins[g]++;
                        data.BlueBins[b]++;
                        data.LumaBins[luma]++;

                        totalLuma += luma;
                        countedPixels++;
                    }
                }

                // Compute max count
                int max = 1;
                for (int i = 0; i < 256; i++)
                {
                    if (data.RedBins[i] > max) max = data.RedBins[i];
                    if (data.GreenBins[i] > max) max = data.GreenBins[i];
                    if (data.BlueBins[i] > max) max = data.BlueBins[i];
                    if (data.LumaBins[i] > max) max = data.LumaBins[i];
                }
                data.MaxBinCount = max;

                if (countedPixels > 0)
                {
                    data.MeanLuma = (double)totalLuma / countedPixels;
                }

                return data;
            });
        }
    }
}
