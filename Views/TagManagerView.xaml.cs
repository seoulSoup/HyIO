using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HyIO.Views
{
    public partial class TagManagerView : UserControl
    {
        // placeholder 텍스트
        private const string TagPlaceholderText = "여기에 태그 를 입력한 뒤 Enter";
        public double ScrollLogicalValue { get; private set; }

        // 각 카드(이미지 하나)에 대응하는 ViewModel
        public class TagRow : INotifyPropertyChanged
        {
            private string _fileName;
            private string _filePath;
            private ImageSource _thumbnail;
            private string _newTagText;

            public string FileName
            {
                get => _fileName;
                set { _fileName = value; OnPropertyChanged(); }
            }

            // 전체 파일 경로
            public string FilePath
            {
                get => _filePath;
                set { _filePath = value; OnPropertyChanged(); }
            }

            // 썸네일 이미지
            public ImageSource Thumbnail
            {
                get => _thumbnail;
                set { _thumbnail = value; OnPropertyChanged(); }
            }

            // 새 태그 입력 텍스트
            public string NewTagText
            {
                get => _newTagText;
                set { _newTagText = value; OnPropertyChanged(); }
            }

            // 이미지에 붙은 태그들
            public ObservableCollection<string> Tags { get; } = new();

            public event PropertyChangedEventHandler PropertyChanged;

            protected void OnPropertyChanged([CallerMemberName] string name = null)
                => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private readonly AppConfig _config;
        private readonly ObservableCollection<TagRow> _rows = new();

        public TagManagerView(AppConfig config)
        {
            InitializeComponent();
            _config = config;

            TagGrid.ItemsSource = _rows;
            LoadTags();
        }

        public void ReloadTags()
        {
            LoadTags();
        }

        // ==================== 태그 로딩 ====================
        private void LoadTags()
        {
            _rows.Clear();

            // 사용 가능한 폴더
            var enabledFolders = _config.Folders
                                        .Where(f => f.Enabled && Directory.Exists(f.Path))
                                        .Select(f => f.Path)
                                        .ToList();

            // ImageOverlayView와 동일한 이미지 확장자만 사용
            var imageExtensions = new HashSet<string>(
                new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif" },
                StringComparer.OrdinalIgnoreCase);

            // 폴더 안의 이미지 파일 수집 (파일명 기준 중복 제거)
            var allFiles = enabledFolders
                .SelectMany(p =>
                {
                    try
                    {
                        return Directory.EnumerateFiles(p);
                    }
                    catch
                    {
                        return Enumerable.Empty<string>();
                    }
                })
                .Where(path =>
                {
                    var ext = Path.GetExtension(path);
                    if (string.IsNullOrEmpty(ext))
                        return false;
                    return imageExtensions.Contains(ext);
                })
                .Select(path => new
                {
                    FileName = Path.GetFileName(path),
                    FullPath = path
                })
                .GroupBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())
                .OrderBy(x => x.FileName)
                .ToList();

            // === 1) 현재 존재하는 파일 기준으로 _config.Tags 정리 ===
            var activeFileNames = new HashSet<string>(
                allFiles.Select(f => f.FileName),
                StringComparer.OrdinalIgnoreCase);

            var keysToRemove = _config.Tags.Keys
                .Where(k => !activeFileNames.Contains(k))
                .ToList();

            foreach (var key in keysToRemove)
            {
                _config.Tags.Remove(key);
            }

            if (keysToRemove.Count > 0)
            {
                ConfigManager.Save(_config);
            }

            // === 2) 실제 존재하는 이미지들만 카드로 표시 ===
            foreach (var file in allFiles)
            {
                _config.Tags.TryGetValue(file.FileName, out var tagList);

                var row = new TagRow
                {
                    FileName = file.FileName,
                    FilePath = file.FullPath,
                    Thumbnail = LoadThumbnailSafe(file.FullPath),
                    NewTagText = TagPlaceholderText
                };

                if (tagList != null)
                {
                    foreach (var t in tagList)
                    {
                        // 기존에 저장된 태그는 그대로 보여주되,
                        // 너무 길면 UI 깨질 수 있으니 여기서도 잘라주고 싶으면 아래 주석 해제 가능
                        // var tt = t.Length > 12 ? t.Substring(0, 12) : t;
                        // row.Tags.Add(tt);

                        row.Tags.Add(t);
                    }
                }

                _rows.Add(row);
            }
        }

        private ImageSource LoadThumbnailSafe(string path)
        {
            try
            {
                if (!File.Exists(path))
                    return null;

                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(path);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.DecodePixelWidth = 144;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        // ==================== 태그 추가 (Enter) ====================
        private void TagTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            if (sender is not TextBox tb)
                return;

            if (tb.DataContext is not TagRow row)
                return;

            var text = tb.Text?.Trim();

            // placeholder거나 비어 있으면 무시
            if (string.IsNullOrEmpty(text) || text == TagPlaceholderText)
                return;

            // 항상 "#tag" 형식으로
            if (!text.StartsWith("#"))
                text = "#" + text;

            // ✅ 글자 수 12자로 제한 (# 포함)
            if (text.Length > 20)
                text = text.Substring(0, 20);

            // 중복 방지
            if (!row.Tags.Contains(text))
                row.Tags.Add(text);

            // ✅ 연속 입력을 위해, Enter 후에는 빈 칸으로 두고 placeholder는 사용하지 않음
            row.NewTagText = string.Empty;
            tb.Text = string.Empty;

            // 입력 상태 색상 (일반 텍스트 색)
            if (TryFindResource("AppTextPrimaryBrush") is Brush primary)
                tb.Foreground = primary;
            else
                tb.Foreground = Brushes.Black;

            e.Handled = true;

            UpdateConfigTags(row);
        }

        // ==================== 태그 삭제 (칩의 X 버튼) ====================
        private void RemoveTagButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe)
                return;

            // 칩의 DataContext는 태그 문자열
            if (fe.DataContext is not string tagText)
                return;

            // 상위 요소들 중에서 DataContext가 TagRow인 요소 찾기
            var row = FindAncestorTagRow(fe);
            if (row == null)
                return;

            if (row.Tags.Contains(tagText))
                row.Tags.Remove(tagText);

            UpdateConfigTags(row);
        }

        // VisualTree를 따라 올라가면서 DataContext가 TagRow인 요소 찾기
        private TagRow FindAncestorTagRow(DependencyObject current)
        {
            while (current != null)
            {
                if (current is FrameworkElement fe && fe.DataContext is TagRow row)
                    return row;

                current = VisualTreeHelper.GetParent(current);
            }

            return null;
        }

        // ==================== config 동기화 ====================
        private void UpdateConfigTags(TagRow row)
        {
            var tags = row.Tags
                .Select(t => t?.Trim())
                .Where(t => !string.IsNullOrEmpty(t))
                .ToList();

            if (tags.Count == 0)
            {
                _config.Tags.Remove(row.FileName);
            }
            else
            {
                _config.Tags[row.FileName] = tags;
            }

            ConfigManager.Save(_config);
        }

        // ==================== placeholder 처리 ====================
        private void TagTextBox_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is TagRow row)
            {
                // placeholder일 때만 지워줌
                if (tb.Text == TagPlaceholderText)
                {
                    tb.Text = string.Empty;
                    row.NewTagText = string.Empty;

                    if (TryFindResource("AppTextPrimaryBrush") is Brush primary)
                        tb.Foreground = primary;
                    else
                        tb.Foreground = Brushes.Black;
                }
            }
        }

        private void TagTextBox_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb && tb.DataContext is TagRow row)
            {
                var text = tb.Text?.Trim();
                if (string.IsNullOrEmpty(text))
                {
                    tb.Text = TagPlaceholderText;
                    row.NewTagText = TagPlaceholderText;

                    if (TryFindResource("AppTextSecondaryBrush") is Brush secondary)
                        tb.Foreground = secondary;
                    else
                        tb.Foreground = Brushes.Gray;
                }
            }
        }
        private void TagScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            if (sender is not ScrollViewer sv)
                return;

            // ScrollableHeight가 0이면(스크롤 불가능) 0으로 처리
            double scrollable = sv.ScrollableHeight;
            double offset = sv.VerticalOffset;

            double t = scrollable <= 0 ? 0.0 : offset / scrollable;

            double logicalValue = 100 + t * (600 - 100);
           if (!_scrollDebugShown)
            {
                _scrollDebugShown = true;
                MessageBox.Show($"scrollable={scrollable}, offset={offset}");
            }
            // ✅ 이 줄이 있어도 이제 정상
            ScrollLogicalValue = logicalValue;

            // TODO: logicalValue를 네가 원하는 곳에 사용
            // 예: 디버그 출력
            // System.Diagnostics.Debug.WriteLine($"Logical scroll = {logicalValue:F2}");

            // 만약 ViewModel에 프로퍼티가 있다면 거기에 넣어줘도 좋고,
            // UI의 다른 요소(라벨, 슬라이더 등)를 업데이트해도 됨.
        }
    }
}
