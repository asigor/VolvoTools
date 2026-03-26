using System;
using System.IO;
using System.Text.Json;

namespace VolvoToolsGui
{
    internal sealed class GuiConfig
    {
        public string? DefaultPlatform { get; set; }
        public string? DefaultBaudrate { get; set; }
        public string? DefaultModule { get; set; }
        public string? DefaultEcuId { get; set; }
        public string? DefaultPin { get; set; }
        public bool? DefaultPinScanDown { get; set; }

        public string? CemDefaultEcuId { get; set; }
        public string? CemDefaultPin { get; set; }
        public bool? CemDefaultPinScanDown { get; set; }

        public string? LoggerDefaultEcuId { get; set; }
        public decimal? LoggerDefaultPrintCount { get; set; }

        public static GuiConfig Load(string configPath)
        {
            if (!File.Exists(configPath))
            {
                return new GuiConfig();
            }

            try
            {
                var json = File.ReadAllText(configPath);
                return JsonSerializer.Deserialize<GuiConfig>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true,
                    ReadCommentHandling = JsonCommentHandling.Skip,
                    AllowTrailingCommas = true
                }) ?? new GuiConfig();
            }
            catch
            {
                return new GuiConfig();
            }
        }
    }
}
