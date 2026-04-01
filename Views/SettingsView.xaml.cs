using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;

namespace HyIO.Views
{
    public partial class SettingsView : UserControl
    {
        private readonly AppConfig _config;
        private readonly Action<bool> _onSettingsChanged;

        public SettingsView(AppConfig config, Action<bool> onSettingsChanged)
        {
            InitializeComponent();
            _config = config;
            _onSettingsChanged = onSettingsChanged;

            SyncFromConfig();
        }

        public void SyncFromConfig()
        {
            HotkeyBox.Text = _config.Hotkey;
            AutoPasteToggle.IsChecked = _config.AutoPasteEnabled;
            UpdateAutoPasteDescription();
            UpdateToggleVisual(false);
        }

        private void AutoPasteToggle_Click(object sender, RoutedEventArgs e)
        {
            _config.AutoPasteEnabled = AutoPasteToggle.IsChecked == true;
            UpdateAutoPasteDescription();
            ConfigManager.Save(_config);
            _onSettingsChanged?.Invoke(false);
        }

        private void AutoPasteToggle_Loaded(object sender, RoutedEventArgs e)
        {
            UpdateToggleVisual(false);
        }

        private void AutoPasteToggle_Checked(object sender, RoutedEventArgs e)
        {
            UpdateToggleVisual(true);
        }

        private void AutoPasteToggle_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateToggleVisual(true);
        }

        private void UpdateToggleVisual(bool animate)
        {
            if (AutoPasteToggle.Template == null)
                return;

            if (AutoPasteToggle.Template.FindName("Thumb", AutoPasteToggle) is not FrameworkElement thumb)
                return;

            if (thumb.RenderTransform is not TranslateTransform currentTransform || currentTransform.IsFrozen)
            {
                var replacementTransform = new TranslateTransform();
                if (thumb.RenderTransform is TranslateTransform existingTransform)
                    replacementTransform.X = existingTransform.X;

                thumb.RenderTransform = replacementTransform;
            }

            if (thumb.RenderTransform is not TranslateTransform transform)
                return;

            double targetX = AutoPasteToggle.IsChecked == true ? 34 : 0;

            if (!animate)
            {
                transform.BeginAnimation(TranslateTransform.XProperty, null);
                transform.X = targetX;
                return;
            }

            var animation = new DoubleAnimation
            {
                To = targetX,
                Duration = TimeSpan.FromMilliseconds(180),
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut }
            };

            transform.BeginAnimation(TranslateTransform.XProperty, animation);
        }

        private void UpdateAutoPasteDescription()
        {
            if (AutoPasteDescription == null)
                return;

            AutoPasteDescription.Text = _config.AutoPasteEnabled
                ? "이미지를 선택하면 창을 닫고 자동으로 붙여넣습니다."
                : "이미지 선택 시 창을 닫고 클립보드에만 복사합니다.";
        }

        private void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            e.Handled = true;

            var key = e.Key == Key.System ? e.SystemKey : e.Key;

            if (key == Key.Tab)
            {
                e.Handled = false;
                return;
            }

            if (key == Key.Escape)
            {
                HotkeyBox.Text = string.Empty;
                _config.Hotkey = string.Empty;
                ConfigManager.Save(_config);
                _onSettingsChanged?.Invoke(true);
                return;
            }

            if (IsModifierKey(key))
                return;

            var hotkeyText = BuildHotkeyText(Keyboard.Modifiers, key);
            if (!string.IsNullOrWhiteSpace(hotkeyText))
            {
                HotkeyBox.Text = hotkeyText;
                HotkeyBox.CaretIndex = HotkeyBox.Text.Length;
                _config.Hotkey = hotkeyText;
                ConfigManager.Save(_config);
                _onSettingsChanged?.Invoke(true);
            }
        }

        private static bool IsModifierKey(Key key)
        {
            return key == Key.LeftCtrl || key == Key.RightCtrl ||
                   key == Key.LeftAlt || key == Key.RightAlt ||
                   key == Key.LeftShift || key == Key.RightShift ||
                   key == Key.LWin || key == Key.RWin;
        }

        private static string BuildHotkeyText(ModifierKeys modifiers, Key key)
        {
            string result = string.Empty;

            if (modifiers.HasFlag(ModifierKeys.Control))
                result += "Ctrl+";
            if (modifiers.HasFlag(ModifierKeys.Alt))
                result += "Alt+";
            if (modifiers.HasFlag(ModifierKeys.Shift))
                result += "Shift+";
            if (modifiers.HasFlag(ModifierKeys.Windows))
                result += "Win+";

            result += NormalizeKeyName(key);
            return result;
        }

        private static string NormalizeKeyName(Key key)
        {
            if (key >= Key.D0 && key <= Key.D9)
                return key.ToString().Substring(1);

            if (key >= Key.NumPad0 && key <= Key.NumPad9)
                return key.ToString().Replace("NumPad", "Num");

            return key switch
            {
                Key.Space => "Space",
                Key.Return => "Enter",
                Key.Prior => "PageUp",
                Key.Next => "PageDown",
                Key.OemPlus => "Plus",
                Key.OemMinus => "Minus",
                Key.OemComma => "Comma",
                Key.OemPeriod => "Period",
                Key.OemQuestion => "Slash",
                Key.OemSemicolon => "Semicolon",
                Key.OemQuotes => "Quote",
                Key.OemOpenBrackets => "OpenBracket",
                Key.OemCloseBrackets => "CloseBracket",
                Key.OemPipe => "Backslash",
                Key.OemTilde => "Backtick",
                _ => key.ToString()
            };
        }
    }
}
