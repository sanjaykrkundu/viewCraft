using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using MetadataExtractor;
using MetadataExtractor.Formats.Exif;
using MetadataExtractor.Formats.Iptc;
using MetadataExtractor.Formats.Xmp;
using ViewCraft.Models;
using ImageMagick;

namespace ViewCraft.Engine
{
    public static class MetadataEngine
    {
        public static async Task<ImageMetadata> ExtractMetadataAsync(string filePath, IMagickImage<ushort>? magickImage = null)
        {
            return await Task.Run(() =>
            {
                var meta = new ImageMetadata();
                var fi = new FileInfo(filePath);
                meta.FileSizeBytes = fi.Exists ? fi.Length : 0;

                if (magickImage != null)
                {
                    meta.Width = (int)magickImage.Width;
                    meta.Height = (int)magickImage.Height;
                    meta.Format = magickImage.Format.ToString();
                    meta.BitDepth = (int)magickImage.Depth;
                    meta.ColorSpace = magickImage.ColorSpace.ToString();
                    meta.Compression = magickImage.Compression.ToString();
                    
                    var profile = magickImage.GetColorProfile();
                    if (profile != null)
                    {
                        meta.IccProfile = profile.Name ?? "Embedded ICC Profile";
                    }
                }

                try
                {
                    var directories = ImageMetadataReader.ReadMetadata(filePath);

                    foreach (var dir in directories)
                    {
                        string dirName = dir.Name;
                        if (!meta.AllTags.ContainsKey(dirName))
                        {
                            meta.AllTags[dirName] = new Dictionary<string, string>();
                        }

                        foreach (var tag in dir.Tags)
                        {
                            meta.AllTags[dirName][tag.Name] = tag.Description ?? string.Empty;
                        }
                    }

                    // Extract EXIF SubIFD Directory
                    var subIfd = directories.OfType<ExifSubIfdDirectory>().FirstOrDefault();
                    if (subIfd != null)
                    {
                        meta.DateTaken = subIfd.GetDescription(ExifDirectoryBase.TagDateTimeOriginal) 
                                         ?? subIfd.GetDescription(ExifDirectoryBase.TagDateTimeDigitized) 
                                         ?? string.Empty;
                        meta.ShutterSpeed = subIfd.GetDescription(ExifDirectoryBase.TagExposureTime) 
                                            ?? subIfd.GetDescription(ExifDirectoryBase.TagShutterSpeed) 
                                            ?? string.Empty;
                        meta.Aperture = subIfd.GetDescription(ExifDirectoryBase.TagFNumber) 
                                        ?? subIfd.GetDescription(ExifDirectoryBase.TagAperture) 
                                        ?? string.Empty;
                        meta.Iso = subIfd.GetDescription(ExifDirectoryBase.TagIsoEquivalent) ?? string.Empty;
                        meta.FocalLength = subIfd.GetDescription(ExifDirectoryBase.TagFocalLength) ?? string.Empty;
                        meta.FocalLengthIn35mm = subIfd.GetDescription(0xA405) ?? string.Empty;
                        meta.ExposureBias = subIfd.GetDescription(ExifDirectoryBase.TagExposureBias) ?? string.Empty;
                        meta.MeteringMode = subIfd.GetDescription(ExifDirectoryBase.TagMeteringMode) ?? string.Empty;
                        meta.Flash = subIfd.GetDescription(ExifDirectoryBase.TagFlash) ?? string.Empty;
                        meta.WhiteBalance = subIfd.GetDescription(ExifDirectoryBase.TagWhiteBalance) ?? string.Empty;
                        meta.LensModel = subIfd.GetDescription(ExifDirectoryBase.TagLensModel) ?? string.Empty;
                    }

                    // Extract EXIF IFD0
                    var ifd0 = directories.OfType<ExifIfd0Directory>().FirstOrDefault();
                    if (ifd0 != null)
                    {
                        meta.CameraMake = ifd0.GetDescription(ExifDirectoryBase.TagMake) ?? string.Empty;
                        meta.CameraModel = ifd0.GetDescription(ExifDirectoryBase.TagModel) ?? string.Empty;
                        meta.Software = ifd0.GetDescription(ExifDirectoryBase.TagSoftware) ?? string.Empty;
                        meta.Orientation = ifd0.GetDescription(ExifDirectoryBase.TagOrientation) ?? "1 (Normal)";
                    }

                    // Extract GPS
                    var gps = directories.OfType<GpsDirectory>().FirstOrDefault();
                    if (gps != null)
                    {
                        var geo = gps.GetGeoLocation();
                        if (geo.HasValue && !geo.Value.IsZero)
                        {
                            meta.Latitude = geo.Value.Latitude;
                            meta.Longitude = geo.Value.Longitude;
                        }

                        if (gps.TryGetRational(GpsDirectory.TagAltitude, out var altRational))
                        {
                            meta.Altitude = altRational.ToDouble();
                        }
                    }

                    // Extract IPTC
                    var iptc = directories.OfType<IptcDirectory>().FirstOrDefault();
                    if (iptc != null)
                    {
                        meta.Title = iptc.GetDescription(IptcDirectory.TagObjectName) ?? string.Empty;
                        meta.Caption = iptc.GetDescription(IptcDirectory.TagCaption) ?? string.Empty;
                        meta.Artist = iptc.GetDescription(IptcDirectory.TagByLine) ?? string.Empty;
                        meta.Copyright = iptc.GetDescription(IptcDirectory.TagCopyrightNotice) ?? string.Empty;

                        var keywords = iptc.GetStringArray(IptcDirectory.TagKeywords);
                        if (keywords != null)
                        {
                            meta.Keywords.AddRange(keywords);
                        }
                    }
                }
                catch
                {
                    // Fallback to basic file properties if metadata extraction fails
                }

                return meta;
            });
        }
    }
}
