using System;
using System.Threading.Tasks;
using System.Windows.Media.Imaging;
using ImageMagick;
using ViewCraft.Models;

namespace ViewCraft.Engine
{
    public static class AdjustmentPipeline
    {
        /// <summary>
        /// Applies the full non-destructive adjustment stack to the master MagickImage and renders a BitmapSource.
        /// </summary>
        public static async Task<BitmapSource> ApplyAdjustmentsAsync(
            IMagickImage<ushort> masterImage, 
            AdjustmentParameters parameters)
        {
            return await Task.Run(() =>
            {
                using var clone = masterImage.Clone();
                clone.AutoOrient();

                // 1. Exposure & Brightness
                if (Math.Abs(parameters.Exposure) > 0.001 || Math.Abs(parameters.Brightness) > 0.001)
                {
                    // Exposure: 2^EV factor
                    double exposureMultiplier = Math.Pow(2.0, parameters.Exposure);
                    double brightnessPercentage = (exposureMultiplier - 1.0) * 100.0 + parameters.Brightness;
                    
                    if (Math.Abs(brightnessPercentage) > 0.1)
                    {
                        clone.Modulate(new Percentage(100 + brightnessPercentage), new Percentage(100), new Percentage(100));
                    }
                }

                // 2. Contrast
                if (Math.Abs(parameters.Contrast) > 0.001)
                {
                    // Magick Contrast / SigmoidalContrast
                    if (parameters.Contrast > 0)
                    {
                        clone.SigmoidalContrast(parameters.Contrast / 20.0, new Percentage(50));
                    }
                    else
                    {
                        clone.BrightnessContrast(new Percentage(0), new Percentage(parameters.Contrast));
                    }
                }

                // 3. Saturation & Vibrance
                if (Math.Abs(parameters.Saturation) > 0.001 || Math.Abs(parameters.Vibrance) > 0.001)
                {
                    double totalSat = parameters.Saturation + (parameters.Vibrance * 0.7);
                    clone.Modulate(new Percentage(100), new Percentage(Math.Max(0, 100 + totalSat)), new Percentage(100));
                }

                // 4. White Balance: Temperature & Tint
                if (Math.Abs(parameters.Temperature) > 0.001 || Math.Abs(parameters.Tint) > 0.001)
                {
                    // Temperature: Warm increases Red and decreases Blue; Cool does opposite
                    // Tint: Green vs Magenta
                    double redScale = 1.0 + (parameters.Temperature * 0.003) + (parameters.Tint * 0.002);
                    double greenScale = 1.0 - (parameters.Tint * 0.004);
                    double blueScale = 1.0 - (parameters.Temperature * 0.003) + (parameters.Tint * 0.002);

                    redScale = Math.Clamp(redScale, 0.5, 2.0);
                    greenScale = Math.Clamp(greenScale, 0.5, 2.0);
                    blueScale = Math.Clamp(blueScale, 0.5, 2.0);

                    clone.ColorMatrix(new MagickColorMatrix(5, new double[]
                    {
                        redScale, 0, 0, 0, 0,
                        0, greenScale, 0, 0, 0,
                        0, 0, blueScale, 0, 0,
                        0, 0, 0, 1, 0,
                        0, 0, 0, 0, 1
                    }));
                }

                // 5. Highlights and Shadows (Tone curve adjustment)
                if (Math.Abs(parameters.Highlights) > 0.001 || Math.Abs(parameters.Shadows) > 0.001)
                {
                    double shadowLevel = Math.Clamp(parameters.Shadows * 0.2, -30, 30);
                    double highlightLevel = Math.Clamp(parameters.Highlights * 0.2, -30, 30);
                    clone.Level(new Percentage(Math.Max(0, shadowLevel)), new Percentage(Math.Min(100, 100 - highlightLevel)));
                }

                // 6. Sharpness
                if (parameters.Sharpness > 0.1)
                {
                    double radius = 0.5 + (parameters.Sharpness / 50.0);
                    double amount = parameters.Sharpness / 30.0;
                    clone.UnsharpMask(radius, 1.0, amount, 0.05);
                }

                // 7. Artistic Filters (Grayscale, Sepia, Invert)
                if (parameters.Grayscale)
                {
                    clone.Grayscale();
                }

                if (parameters.Sepia)
                {
                    clone.SepiaTone(new Percentage(80));
                }

                if (parameters.InvertColors)
                {
                    clone.Negate();
                }

                // 8. Channel Isolation
                switch (parameters.ChannelMode)
                {
                    case ChannelIsolationMode.RedOnly:
                        clone.Separate(Channels.Red);
                        break;
                    case ChannelIsolationMode.GreenOnly:
                        clone.Separate(Channels.Green);
                        break;
                    case ChannelIsolationMode.BlueOnly:
                        clone.Separate(Channels.Blue);
                        break;
                    case ChannelIsolationMode.AlphaOnly:
                        if (clone.HasAlpha)
                        {
                            clone.Separate(Channels.Alpha);
                        }
                        break;
                    case ChannelIsolationMode.LuminanceOnly:
                        clone.ColorSpace = ColorSpace.Gray;
                        break;
                }

                var bmp = CodecEngine.ConvertMagickToBitmapSource(clone);
                return bmp;
            });
        }
    }
}
