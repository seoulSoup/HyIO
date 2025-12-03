﻿using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Input;
using System.Windows.Media;
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

        // 네비게이션 선택 상태
        private Button _currentNavButton;
        private System.Windows.Media.Brush _navDefaultBackground;
        private System.Windows.Media.Brush _navSelectedBrush;

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

        private void Window_Loaded(object sender, RoutedEventArgs e)
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
                _folderManagerView = new FolderManagerView(_config);
                _tagManagerView = new TagManagerView(_config);
                _settingsView = new SettingsView(_config, OnSettingsChanged);

                // 네비게이션 색상 초기값
                _navDefaultBackground = NavImageOverlay.Background;
                _navSelectedBrush = new SolidColorBrush(System.Windows.Media.Color.FromRgb(0x50, 0x56, 0xA5));

                // 처음 화면: Image Overlay 선택
                SelectNavButton(NavImageOverlay);
                MainContent.Content = _imageOverlayView;

                // 메인 창은 대시보드로 사용 → 보여주기
                this.ShowInTaskbar = true;
                this.Show();

                // 트레이 아이콘 생성
                CreateTrayIcon();

                // 글로벌 핫키 등록
                RegisterGlobalHotKey();

                var helper = new WindowInteropHelper(this);
                HwndSource source = HwndSource.FromHwnd(helper.Handle);
                source.AddHook(HwndHook);
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
            _notifyIcon.Icon = new Icon("haru.ico");
            _notifyIcon.Visible = true;
            _notifyIcon.Text = "HyIO - ImageOverlay";

            var menu = new WF.ContextMenuStrip();

            _toggleAutoPasteMenuItem = new WF.ToolStripMenuItem();
            _toggleAutoPasteMenuItem.Click += ToggleAutoPasteMenuItem_Click;
            UpdateAutoPasteMenuItemText();

            menu.Items.Add("HyIO 대시보드 열기", null, (s, e) => ShowDashboard());
            menu.Items.Add("이미지 오버레이 열기", null, (s, e) => ShowDashboardAndOverlayTab());
            menu.Items.Add(new WF.ToolStripSeparator());
            menu.Items.Add(_toggleAutoPasteMenuItem);
            menu.Items.Add("종료", null, (s, e) => ExitApp());

            _notifyIcon.ContextMenuStrip = menu;
            _notifyIcon.DoubleClick += (s, e) => ShowDashboardAndOverlayTab();
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
            NavImageOverlay_Click(null, new RoutedEventArgs());
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
                // 핫키 → 대시보드 + ImageOverlay 탭
                ShowDashboardAndOverlayTab();
                handled = true;
            }
            return IntPtr.Zero;
        }

        // =================== 자동 붙여넣기 토글 ===================
        private void ToggleAutoPasteMenuItem_Click(object sender, EventArgs e)
        {
            App.Config.AutoPasteEnabled = !App.Config.AutoPasteEnabled;
            ConfigManager.Save(App.Config);
            UpdateAutoPasteMenuItemText();
        }

        private void UpdateAutoPasteMenuItemText()
        {
            if (_toggleAutoPasteMenuItem == null) return;

            _toggleAutoPasteMenuItem.Checked = App.Config.AutoPasteEnabled;
            _toggleAutoPasteMenuItem.Text =
                App.Config.AutoPasteEnabled ? "자동 붙여넣기: ON" : "자동 붙여넣기: OFF";
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
        private void SelectNavButton(Button btn)
        {
            if (_currentNavButton != null)
            {
                _currentNavButton.Background = _navDefaultBackground;
            }

            btn.Background = _navSelectedBrush;
            _currentNavButton = btn;
        }

        private void NavImageOverlay_Click(object sender, RoutedEventArgs e)
        {
            SelectNavButton(NavImageOverlay);
            Dashboard.Text = "이미지 선택";
            HeaderSubtitle.Text = "즐겨 쓰는 이미지를 선택해서 복사/붙여넣기 할 수 있습니다.";
            MainContent.Content = _imageOverlayView;
        }

        private void NavFolderManager_Click(object sender, RoutedEventArgs e)
        {
            SelectNavButton(NavFolderManager);
            Dashboard.Text = "폴더 매니저";
            HeaderSubtitle.Text = "이미지 탐색에 사용할 폴더를 관리합니다.";
            MainContent.Content = _folderManagerView;
        }

        private void NavTagManager_Click(object sender, RoutedEventArgs e)
        {
            SelectNavButton(NavTagManager);
            Dashboard.Text = "태그 매니저";
            HeaderSubtitle.Text = "이미지에 태그를 부여하고 검색에 활용할 수 있습니다.";
            MainContent.Content = _tagManagerView;
        }

        private void NavSettings_Click(object sender, RoutedEventArgs e)
        {
            SelectNavButton(NavSettings);
            Dashboard.Text = "설정";
            HeaderSubtitle.Text = "글로벌 핫키 및 각종 옵션을 설정합니다.";
            MainContent.Content = _settingsView;
        }

        private void OnSettingsChanged()
        {
            // 핫키가 변경되면 재등록
            var helper = new WindowInteropHelper(this);
            UnregisterHotKey(helper.Handle, HOTKEY_ID);
            RegisterGlobalHotKey();
        }
    }
}
