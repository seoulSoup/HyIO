﻿using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Resources;
using WF = System.Windows.Forms;
using HyIO.Views;

namespace HyIO
{
    public partial class MainWindow : Window
    {
        private WF.NotifyIcon _notifyIcon;
        private WF.ToolStripMenuItem _toggleAutoPasteMenuItem;

        private AppConfig _config;

        // 임베딩할 View들
        private ImageOverlayView _imageOverlayView;
        private FolderManagerView _folderManagerView;
        private TagManagerView _tagManagerView;
        private SettingsView _settingsView;
        private CommandPreviewWindow _commandPreviewWindow;
        private IntPtr _lastCommandTargetWindow;

        // 네비게이션 선택 상태
        private Button _currentNavButton;
        private const int NavTransitionMilliseconds = 170;
        private bool _navInitialized;

        // ====== 글로벌 핫키 관련 상수/WinAPI ======
        private const int HOTKEY_ID = 0x9876;
        private const int WM_HOTKEY = 0x0312;

        private const uint MOD_ALT = 0x0001;
        private const uint MOD_CTRL = 0x0002;
        private const uint MOD_SHIFT = 0x0004;
        private const uint MOD_WIN = 0x0008;

        [DllImport("user32.dll")]
        private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll")]
        private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        public MainWindow()
        {
            InitializeComponent();
        }

        private async void Window_Loaded(object sender, RoutedEventArgs e)
        {
            try
            {
                _config = ConfigManager.Load();
                App.Config = _config;

                if (_config.Folders.Count == 0)
                {
                    var pics = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
                    if (!string.IsNullOrEmpty(pics))
                        _config.Folders.Add(new FolderEntry { Path = pics, Enabled = true });
                }

                // 뷰 생성
                _imageOverlayView = new ImageOverlayView(_config);
                _folderManagerView = new FolderManagerView(_config, OnFoldersChanged);
                _tagManagerView = new TagManagerView(_config, OnTagsChanged);
                _settingsView = new SettingsView(_config, OnSettingsChanged);

                // 메인 창은 기존 매니저 화면으로 사용
                this.ShowInTaskbar = true;
                this.Show();
                NavTagManager_Click(null, new RoutedEventArgs());

                // 트레이 아이콘 생성
                CreateTrayIcon();

                // 글로벌 핫키 등록
                RegisterGlobalHotKey();

                var helper = new WindowInteropHelper(this);
                HwndSource source = HwndSource.FromHwnd(helper.Handle);
                source.AddHook(HwndHook);

                await _imageOverlayView.LoadImagesAsync();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.ToString(), "시작 중 오류", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
        }

        // =================== 창 드래그 (커스텀 타이틀바) ===================
        private void HeaderBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                try
                {
                    DragMove();
                }
                catch { /* 드래그 중 예외는 무시 */ }
            }
        }

        // =================== 트레이 아이콘 ===================
        private void CreateTrayIcon()
        {
            _notifyIcon = new WF.NotifyIcon();
            _notifyIcon.Icon = LoadTrayIcon();
            _notifyIcon.Visible = true;
            _notifyIcon.Text = "HyIO - ImageOverlay";

            var menu = new WF.ContextMenuStrip();

            _toggleAutoPasteMenuItem = new WF.ToolStripMenuItem();
            _toggleAutoPasteMenuItem.Click += ToggleAutoPasteMenuItem_Click;
            UpdateAutoPasteMenuItemText();

            menu.Items.Add("HyIO 열기", null, (s, e) => ShowDashboardAndOverlayTab());
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add(_toggleAutoPasteMenuItem);
            menu.Items.Add("종료", null, (s, e) => ExitApp());

            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.DoubleClick += (s, e) => ShowDashboardAndOverlayTab();
        }

        private Icon LoadTrayIcon()
        {
            StreamResourceInfo resourceStream = Application.GetResourceStream(
                new Uri("pack://application:,,,/Resources/haru.ico", UriKind.Absolute));

            if (resourceStream?.Stream == null)
                throw new FileNotFoundException("트레이 아이콘 리소스를 찾을 수 없습니다.", "Resources/haru.ico");

            using (resourceStream.Stream)
            using (var icon = new Icon(resourceStream.Stream))
            {
                return (Icon)icon.Clone();
            }
        }

        private void ShowDashboard()
        {
            this.ShowInTaskbar = true;
            if (!this.IsVisible)
                this.Show();

            if (this.WindowState == WindowState.Minimized)
                this.WindowState = WindowState.Normal;

            this.Activate();
        }

        private void ShowDashboardAndOverlayTab()
        {
            ShowDashboard();
            NavTagManager_Click(null, new RoutedEventArgs());
        }

        private void ExitApp()
        {
            _notifyIcon.Visible = false;
            _notifyIcon.Dispose();
            Application.Current.Shutdown();
        }

        // =================== 글로벌 핫키 처리 ===================
        private void RegisterGlobalHotKey()
        {
            var helper = new WindowInteropHelper(this);
            IntPtr hwnd = helper.Handle;

            ParseHotkey(_config.Hotkey, out var mods, out var key);

            if (!RegisterHotKey(hwnd, HOTKEY_ID, mods, key))
            {
                MessageBox.Show("글로벌 핫키 등록에 실패했습니다.", "오류",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void ParseHotkey(string hotkey, out uint mods, out uint key)
        {
            mods = 0;
            key = 0;

            if (string.IsNullOrWhiteSpace(hotkey))
            {
                mods = MOD_CTRL | MOD_ALT;
                key = (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(System.Windows.Input.Key.Space);
                return;
            }

            var parts = hotkey.Split('+', StringSplitOptions.RemoveEmptyEntries);
            foreach (var part in parts)
            {
                var p = part.ToLowerInvariant();
                if (p == "ctrl" || p == "control")
                    mods |= MOD_CTRL;
                else if (p == "alt")
                    mods |= MOD_ALT;
                else if (p == "shift")
                    mods |= MOD_SHIFT;
                else if (p == "win" || p == "windows")
                    mods |= MOD_WIN;
                else
                {
                    if (p == "space") key = 0x20;
                    else if (p == "enter") key = (uint)KeyInterop.VirtualKeyFromKey(Key.Enter);
                    else if (p == "pageup") key = (uint)KeyInterop.VirtualKeyFromKey(Key.PageUp);
                    else if (p == "pagedown") key = (uint)KeyInterop.VirtualKeyFromKey(Key.PageDown);
                    else if (p == "plus") key = (uint)KeyInterop.VirtualKeyFromKey(Key.OemPlus);
                    else if (p == "minus") key = (uint)KeyInterop.VirtualKeyFromKey(Key.OemMinus);
                    else if (p == "comma") key = (uint)KeyInterop.VirtualKeyFromKey(Key.OemComma);
                    else if (p == "period") key = (uint)KeyInterop.VirtualKeyFromKey(Key.OemPeriod);
                    else if (p == "slash") key = (uint)KeyInterop.VirtualKeyFromKey(Key.OemQuestion);
                    else if (p == "semicolon") key = (uint)KeyInterop.VirtualKeyFromKey(Key.OemSemicolon);
                    else if (p == "quote") key = (uint)KeyInterop.VirtualKeyFromKey(Key.OemQuotes);
                    else if (p == "openbracket") key = (uint)KeyInterop.VirtualKeyFromKey(Key.OemOpenBrackets);
                    else if (p == "closebracket") key = (uint)KeyInterop.VirtualKeyFromKey(Key.OemCloseBrackets);
                    else if (p == "backslash") key = (uint)KeyInterop.VirtualKeyFromKey(Key.OemPipe);
                    else if (p == "backtick") key = (uint)KeyInterop.VirtualKeyFromKey(Key.OemTilde);
                    else
                    {
                        var k = (System.Windows.Input.Key)Enum.Parse(typeof(System.Windows.Input.Key), part, true);
                        key = (uint)System.Windows.Input.KeyInterop.VirtualKeyFromKey(k);
                    }
                }
            }
        }

        private IntPtr HwndHook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
        {
            if (msg == WM_HOTKEY && wParam.ToInt32() == HOTKEY_ID)
            {
                if (!TryShowSlashCommandPreview())
                {
                    ShowDashboardAndOverlayTab();
                }

                handled = true;
            }
            return IntPtr.Zero;
        }

        private bool TryShowSlashCommandPreview()
        {
            if (_imageOverlayView == null)
                return false;

            if (!FocusedTextContextReader.TryReadSlashCommand(out var context))
            {
                _commandPreviewWindow?.HidePreview();
                return false;
            }

            var matches = _imageOverlayView.FindPreviewMatches(context.CommandText);

            EnsureCommandPreviewWindow();
            _lastCommandTargetWindow = context.TargetWindowHandle;
            _commandPreviewWindow.ShowPreview(
                context.CommandText,
                matches,
                context.AnchorScreenPoint);

            return true;
        }

        private void EnsureCommandPreviewWindow()
        {
            if (_commandPreviewWindow != null)
                return;

            _commandPreviewWindow = new CommandPreviewWindow();
            _commandPreviewWindow.CandidateChosen += CommandPreviewWindow_CandidateChosen;
        }

        private void CommandPreviewWindow_CandidateChosen(ImageOverlayView.PreviewImageMatch match)
        {
            try
            {
                _imageOverlayView.CopyMatchToClipboard(match, false);

                if (App.Config.AutoPasteEnabled && _lastCommandTargetWindow != IntPtr.Zero)
                {
                    SetForegroundWindow(_lastCommandTargetWindow);
                    WF.SendKeys.SendWait("^v");
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"이미지를 선택하는 중 오류가 발생했습니다.\n\n{ex.Message}",
                    "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool SetForegroundWindow(IntPtr hWnd);

        // =================== 자동 붙여넣기 토글 ===================
        private void ToggleAutoPasteMenuItem_Click(object sender, EventArgs e)
        {
            App.Config.AutoPasteEnabled = !App.Config.AutoPasteEnabled;
            ConfigManager.Save(App.Config);
            ApplyAutoPasteStateChanged();
        }

        private void UpdateAutoPasteMenuItemText()
        {
            if (_toggleAutoPasteMenuItem == null) return;

            _toggleAutoPasteMenuItem.Checked = App.Config.AutoPasteEnabled;
            _toggleAutoPasteMenuItem.Text =
                App.Config.AutoPasteEnabled ? "자동 붙여넣기: ON" : "자동 붙여넣기: OFF";
        }

        private void ApplyAutoPasteStateChanged()
        {
            UpdateAutoPasteMenuItemText();
            _settingsView?.SyncFromConfig();
        }

        // =================== 헤더 버튼 ===================
        private void MinimizeButton_Click(object sender, RoutedEventArgs e)
        {
            this.WindowState = WindowState.Minimized;
        }

        private void CloseButton_Click(object sender, RoutedEventArgs e)
        {
            this.Hide();
            this.ShowInTaskbar = false;
        }

        // =================== 네비게이션 선택 처리 ===================
        private void SelectNavButton(Button btn, double topMaskHeight)
        {
            AnimateNavigationSelection(btn, topMaskHeight);
            _currentNavButton = btn;
        }

        private void AnimateNavigationSelection(Button btn, double topMaskHeight)
        {
            if (btn == null)
                return;

            btn.UpdateLayout();
            NavButtonHost.UpdateLayout();

            var duration = TimeSpan.FromMilliseconds(NavTransitionMilliseconds);
            var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

            double targetY = btn.TranslatePoint(new System.Windows.Point(0, 0), NavButtonHost).Y - 44;

            if (!_navInitialized)
            {
                NavSelectionTransform.Y = targetY;
                RowUpper.Height = new GridLength(topMaskHeight);
                _navInitialized = true;
                return;
            }

            var highlightAnimation = new DoubleAnimation
            {
                To = targetY,
                Duration = duration,
                EasingFunction = easing
            };

            NavSelectionTransform.BeginAnimation(TranslateTransform.YProperty, highlightAnimation);

            var rowAnimation = new GridLengthAnimation
            {
                From = RowUpper.Height,
                To = new GridLength(topMaskHeight),
                Duration = duration,
                EasingFunction = easing
            };

            RowUpper.BeginAnimation(RowDefinition.HeightProperty, rowAnimation);
        }

        private void NavFolderManager_Click(object sender, RoutedEventArgs e)
        {
            SelectNavButton(NavFolderManager, 180);
            Dashboard.Text = "폴더 매니저";
            HeaderSubtitle.Text = "이미지 탐색에 사용할 폴더를 관리합니다.";
            MainContent.Content = _folderManagerView;
        }


        private void NavTagManager_Click(object sender, RoutedEventArgs e)
        {
            SelectNavButton(NavTagManager, 230);
            Dashboard.Text = "태그 매니저";
            HeaderSubtitle.Text = "이미지에 태그를 부여해서 검색에 활용할 수 있습니다.";
            // 폴더/파일 구성이 바뀌었을 수 있으므로, 탭을 열 때마다 태그 목록 새로고침
            _tagManagerView.ReloadTags();

            MainContent.Content = _tagManagerView;
        }

        private void NavSettings_Click(object sender, RoutedEventArgs e)
        {
            SelectNavButton(NavSettings, 280);
            Dashboard.Text = "설정";
            HeaderSubtitle.Text = "기타 옵션을 설정합니다.";
            MainContent.Content = _settingsView;
        }

        private void OnTagsChanged()
        {
            _imageOverlayView.RefreshTags();
        }

        private async void OnFoldersChanged()
        {
            await _imageOverlayView.LoadImagesAsync();
            _tagManagerView.ReloadTags();
        }

        private void OnSettingsChanged()
        {
            // 핫키가 변경되면 재등록
            var helper = new WindowInteropHelper(this);
            UnregisterHotKey(helper.Handle, HOTKEY_ID);
            RegisterGlobalHotKey();
            ApplyAutoPasteStateChanged();
        }
    }

    public class GridLengthAnimation : AnimationTimeline
    {
        public override Type TargetPropertyType => typeof(GridLength);

        public static readonly DependencyProperty FromProperty =
            DependencyProperty.Register(nameof(From), typeof(GridLength), typeof(GridLengthAnimation));

        public static readonly DependencyProperty ToProperty =
            DependencyProperty.Register(nameof(To), typeof(GridLength), typeof(GridLengthAnimation));

        public static readonly DependencyProperty EasingFunctionProperty =
            DependencyProperty.Register(nameof(EasingFunction), typeof(IEasingFunction), typeof(GridLengthAnimation));

        public GridLength From
        {
            get => (GridLength)GetValue(FromProperty);
            set => SetValue(FromProperty, value);
        }

        public GridLength To
        {
            get => (GridLength)GetValue(ToProperty);
            set => SetValue(ToProperty, value);
        }

        public IEasingFunction EasingFunction
        {
            get => (IEasingFunction)GetValue(EasingFunctionProperty);
            set => SetValue(EasingFunctionProperty, value);
        }

        public override object GetCurrentValue(object defaultOriginValue, object defaultDestinationValue, AnimationClock animationClock)
        {
            double fromValue = From.Value;
            double toValue = To.Value;
            double progress = animationClock.CurrentProgress ?? 0d;

            if (EasingFunction != null)
                progress = EasingFunction.Ease(progress);

            double current = fromValue + ((toValue - fromValue) * progress);
            return new GridLength(current, GridUnitType.Pixel);
        }

        protected override Freezable CreateInstanceCore()
        {
            return new GridLengthAnimation();
        }
    }
}
