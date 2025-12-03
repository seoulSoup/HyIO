using System;
using System.Windows;
using System.Windows.Controls;

namespace HyIO.Views
{
    public partial class SettingsView : UserControl
    {
        private readonly AppConfig _config;
        private readonly Action _onSettingsChanged;

        public SettingsView(AppConfig config, Action onSettingsChanged)
        {
            InitializeComponent();
            _config = config;
            _onSettingsChanged = onSettingsChanged;

            HotkeyBox.Text = _config.Hotkey;
            AutoPasteCheck.IsChecked = _config.AutoPasteEnabled;
        }

        private void SaveButton_Click(object sender, RoutedEventArgs e)
        {
            _config.Hotkey = HotkeyBox.Text.Trim();
            _config.AutoPasteEnabled = AutoPasteCheck.IsChecked == true;

            ConfigManager.Save(_config);
            _onSettingsChanged?.Invoke();

            MessageBox.Show("설정이 저장되었습니다.", "HyIO",
                MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }
}
