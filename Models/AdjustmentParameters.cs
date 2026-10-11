using System;

namespace ViewCraft.Models
{
    public enum ChannelIsolationMode
    {
        AllRgb,
        RedOnly,
        GreenOnly,
        BlueOnly,
        AlphaOnly,
        LuminanceOnly
    }

    public class AdjustmentParameters
    {
        public double Exposure { get; set; } = 0.0;       // -2.0 to +2.0 EV
        public double Contrast { get; set; } = 0.0;       // -100 to +100 %
        public double Brightness { get; set; } = 0.0;     // -100 to +100 %
        public double Saturation { get; set; } = 0.0;     // -100 to +100 %
        public double Vibrance { get; set; } = 0.0;       // -100 to +100 %
        public double Temperature { get; set; } = 0.0;    // -100 (Cool/Blue) to +100 (Warm/Amber)
        public double Tint { get; set; } = 0.0;           // -100 (Green) to +100 (Magenta)
        public double Highlights { get; set; } = 0.0;     // -100 to +100 %
        public double Shadows { get; set; } = 0.0;        // -100 to +100 %
        public double Sharpness { get; set; } = 0.0;      // 0 to 100 %

        public bool InvertColors { get; set; } = false;
        public bool Grayscale { get; set; } = false;
        public bool Sepia { get; set; } = false;

        public ChannelIsolationMode ChannelMode { get; set; } = ChannelIsolationMode.AllRgb;

        public bool IsDefault =>
            Math.Abs(Exposure) < 0.001 &&
            Math.Abs(Contrast) < 0.001 &&
            Math.Abs(Brightness) < 0.001 &&
            Math.Abs(Saturation) < 0.001 &&
            Math.Abs(Vibrance) < 0.001 &&
            Math.Abs(Temperature) < 0.001 &&
            Math.Abs(Tint) < 0.001 &&
            Math.Abs(Highlights) < 0.001 &&
            Math.Abs(Shadows) < 0.001 &&
            Math.Abs(Sharpness) < 0.001 &&
            !InvertColors &&
            !Grayscale &&
            !Sepia &&
            ChannelMode == ChannelIsolationMode.AllRgb;

        public void Reset()
        {
            Exposure = 0.0;
            Contrast = 0.0;
            Brightness = 0.0;
            Saturation = 0.0;
            Vibrance = 0.0;
            Temperature = 0.0;
            Tint = 0.0;
            Highlights = 0.0;
            Shadows = 0.0;
            Sharpness = 0.0;
            InvertColors = false;
            Grayscale = false;
            Sepia = false;
            ChannelMode = ChannelIsolationMode.AllRgb;
        }

        public AdjustmentParameters Clone()
        {
            return (AdjustmentParameters)this.MemberwiseClone();
        }
    }
}
