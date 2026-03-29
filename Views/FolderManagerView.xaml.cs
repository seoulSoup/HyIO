using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WinForms = System.Windows.Forms;

namespace HyIO.Views
{
    public partial class FolderManagerView : UserControl
    {
        private readonly AppConfig _config;
        private readonly Action _onFoldersChanged;
        private readonly ObservableCollection<FolderEntry> _folders;

        public FolderManagerView(AppConfig config, Action onFoldersChanged = null)
        {
            InitializeComponent();
            _config = config;
            _onFoldersChanged = onFoldersChanged;

            _folders = new ObservableCollection<FolderEntry>(_config.Folders);
            FolderGrid.ItemsSource = _folders;
        }

        private void SyncBack()
        {
            _config.Folders.Clear();
            foreach (var f in _folders)
                _config.Folders.Add(f);
            ConfigManager.Save(_config);
            _onFoldersChanged?.Invoke();
        }

        private void AddFolder_Click(object sender, RoutedEventArgs e)
        {
            using var dlg = new WinForms.FolderBrowserDialog();
            if (dlg.ShowDialog() == WinForms.DialogResult.OK)
            {
                if (_folders.Any(f => f.Path == dlg.SelectedPath))
                    return;

                _folders.Add(new FolderEntry { Path = dlg.SelectedPath, Enabled = true });
                SyncBack();
            }
        }

        private void RemoveFolder_Click(object sender, RoutedEventArgs e)
        {
            if (FolderGrid.SelectedItem is FolderEntry entry)
            {
                RemoveUsageEntriesForFolder(entry.Path);
                _folders.Remove(entry);
                SyncBack();
            }
        }

        private void RemoveUsageEntriesForFolder(string folderPath)
        {
            string normalizedFolderPath = NormalizeFolderPath(folderPath);

            var usageKeysToRemove = _config.ImageUsage
                .Where(kvp => IsInFolder(normalizedFolderPath, kvp.Value.FolderPath))
                .Select(kvp => kvp.Key)
                .ToList();

            foreach (var key in usageKeysToRemove)
            {
                _config.ImageUsage.Remove(key);
            }
        }

        private static bool IsInFolder(string baseFolderPath, string candidateFolderPath)
        {
            string normalizedCandidate = NormalizeFolderPath(candidateFolderPath);

            return normalizedCandidate.Equals(baseFolderPath, System.StringComparison.OrdinalIgnoreCase) ||
                   normalizedCandidate.StartsWith(baseFolderPath + Path.DirectorySeparatorChar, System.StringComparison.OrdinalIgnoreCase) ||
                   normalizedCandidate.StartsWith(baseFolderPath + Path.AltDirectorySeparatorChar, System.StringComparison.OrdinalIgnoreCase);
        }

        private static string NormalizeFolderPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                return Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }
    }
}
