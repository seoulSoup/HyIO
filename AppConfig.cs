// AppConfig.cs
using System.Collections.Generic;

namespace HyIO
{
    public class FolderEntry
    {
        public string Path { get; set; } = "";
        public bool Enabled { get; set; } = true;
    }

    public class AppConfig
    {
        public string Hotkey { get; set; } = "Ctrl+Space";
        public List<FolderEntry> Folders { get; set; } = new();
        public Dictionary<string, List<string>> Tags { get; set; } = new();
        public string IconPath { get; set; } = "";
        public bool AutoPasteEnabled { get; set; } = false;
    }
}
