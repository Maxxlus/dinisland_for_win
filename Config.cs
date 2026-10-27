using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace DynamicIsland
{
    public class Theme
    {
        public string Name { get; set; }
        public string Bg1 { get; set; }
        public string Bg2 { get; set; }
        public string Fg { get; set; }
        public string Accent { get; set; }
    }

    public class Config
    {
        public string ApiKey { get; set; } = "";
        public string Model { get; set; } = "openai/gpt-4o-mini";
        public string Active { get; set; } = "Тёмная";
        public bool Autostart { get; set; } = true;
        public List<string> Likes { get; set; } = new();
        public List<string> Shelf { get; set; } = new();
        public string City { get; set; } = "";
        public bool Peeks { get; set; } = true;
        public double OffsetX { get; set; } = 0;
        public bool Static { get; set; } = false;
        public bool DirectNet { get; set; } = false;
        public bool Caffeine { get; set; } = false;
        public bool HourChime { get; set; } = false;
        public bool EyeBreak { get; set; } = false;
        public string VpnProvider { get; set; } = "happ";
        public string VpnConnectCmd { get; set; } = "";
        public string VpnDisconnectCmd { get; set; } = "";
        public List<Theme> Themes { get; set; } = new();

        public static readonly Theme[] Builtin =
        {
            new Theme { Name = "Тёмная", Bg1 = "#000000", Bg2 = "#000000", Fg = "#FFFFFF", Accent = "#0A84FF" },
            new Theme { Name = "Светлая", Bg1 = "#F2F2F7", Bg2 = "#FFFFFF", Fg = "#000000", Accent = "#007AFF" },
        };

        static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "DynamicIsland");
        static string FilePath => Path.Combine(Dir, "config.json");

        public static Config Load()
        {
            Config c = null;
            try { c = JsonSerializer.Deserialize<Config>(File.ReadAllText(FilePath)); } catch { }
            c ??= new Config();
            foreach (var b in Builtin.Reverse())
                if (!c.Themes.Any(t => t.Name == b.Name)) c.Themes.Insert(0, b);
            return c;
        }

        public void Save()
        {
            try
            {
                Directory.CreateDirectory(Dir);
                File.WriteAllText(FilePath, JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true }));
            }
            catch { }
        }
    }
}
