using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;

namespace ViewCraft.Engine
{
    public class ImageCatalogEntry
    {
        public int Rating { get; set; }
        public string ColorLabel { get; set; } = "None";
        public string Notes { get; set; } = string.Empty;
    }

    public static class SidecarEngine
    {
        private const string CatalogFileName = ".viewcraft.json";

        public static async Task<Dictionary<string, ImageCatalogEntry>> LoadCatalogAsync(string directoryPath)
        {
            string path = Path.Combine(directoryPath, CatalogFileName);
            if (!File.Exists(path)) return new Dictionary<string, ImageCatalogEntry>(StringComparer.OrdinalIgnoreCase);

            try
            {
                using var stream = File.OpenRead(path);
                var dict = await JsonSerializer.DeserializeAsync<Dictionary<string, ImageCatalogEntry>>(stream);
                return dict != null 
                    ? new Dictionary<string, ImageCatalogEntry>(dict, StringComparer.OrdinalIgnoreCase) 
                    : new Dictionary<string, ImageCatalogEntry>(StringComparer.OrdinalIgnoreCase);
            }
            catch
            {
                return new Dictionary<string, ImageCatalogEntry>(StringComparer.OrdinalIgnoreCase);
            }
        }

        public static async Task SaveCatalogAsync(string directoryPath, Dictionary<string, ImageCatalogEntry> entries)
        {
            string path = Path.Combine(directoryPath, CatalogFileName);
            try
            {
                using var stream = File.Create(path);
                var options = new JsonSerializerOptions { WriteIndented = true };
                await JsonSerializer.SerializeAsync(stream, entries, options);
            }
            catch
            {
                // ignored
            }
        }
    }
}
