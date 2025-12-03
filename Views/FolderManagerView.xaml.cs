using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using WinForms = System.Windows.Forms;

namespace HyIO.Views
{
    public partial class FolderManagerView : UserControl
    {
        private readonly AppConfig _config;
        private readonly ObservableCollection<FolderEntry> _folders;

        public FolderManagerView(AppConfig config)
        {
            InitializeComponent();
            _config = config;

            _folders = new ObservableCollection<FolderEntry>(_config.Folders);
            FolderGrid.ItemsSource = _folders;
        }

        private void SyncBack()
        {
            _config.Folders.Clear();
            foreach (var f in _folders)
                _config.Folders.Add(f);
            ConfigManager.Save(_config);
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
                _folders.Remove(entry);
                SyncBack();
            }
        }

        private void MoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (FolderGrid.SelectedItem is FolderEntry entry)
            {
                int index = _folders.IndexOf(entry);
                if (index > 0)
                {
                    _folders.Move(index, index - 1);
                    FolderGrid.SelectedItem = entry;
                    SyncBack();
                }
            }
        }

        private void MoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (FolderGrid.SelectedItem is FolderEntry entry)
            {
                int index = _folders.IndexOf(entry);
                if (index < _folders.Count - 1)
                {
                    _folders.Move(index, index + 1);
                    FolderGrid.SelectedItem = entry;
                    SyncBack();
                }
            }
        }
    }
}
