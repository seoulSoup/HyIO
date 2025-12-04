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
    }
}
