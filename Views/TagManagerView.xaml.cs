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
        private const string TagPlaceholderText = "Tag 입력 후 Enter";
        public class TagRow : INotifyPropertyChanged
        {
            private string _fileName = "";
            private string _filePath = "";
            private ImageSource _thumbnail;
            private string _newTagText = "Tag 입력 후 Enter!";

            public string FileName
            {
                get => _fileName;
                set { _fileName = value; OnPropertyChanged(); }
            }

            // 2) 파일 경로 컬럼에 표시할 전체 경로
            public string FilePath
            {
                get => _filePath;
                set { _filePath = value; OnPropertyChanged(); }
            }

            // 2) 이미지 썸네일
            public ImageSource Thumbnail
            {
                get => _thumbnail;
                set { _thumbnail = value; OnPropertyChanged(); }
            }

            // 3) 현재 입력 중인 태그 텍스트
            public string NewTagText
            {
                get => _newTagText;
                set { _newTagText = value; OnPropertyChanged(); }
            }

            // 3,4) 이미 추가된 태그들 (칩으로 렌더링)
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

        // ==================== 태그 로딩 ====================
        private void LoadTags()
        {
            _rows.Clear();

            // 사용 가능한 폴더
            var enabledFolders = _config.Folders
                                        .Where(f => f.Enabled && Directory.Exists(f.Path))
                                        .Select(f => f.Path)
                                        .ToList();

            // 폴더 안의 파일 이름들 (중복 제거)
            var allFiles = enabledFolders
                .SelectMany(p => Directory.EnumerateFiles(p))
                .Select(path => new
                {
                    FileName = Path.GetFileName(path),
                    FullPath = path
                })
                .GroupBy(x => x.FileName, StringComparer.OrdinalIgnoreCase)
                .Select(g => g.First())  // 같은 이름이 여러 폴더에 있으면 첫 번째만
                .OrderBy(x => x.FileName);

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
                        row.Tags.Add(t);
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
                bmp.DecodePixelWidth = 96;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
        }

        // ==================== 태그 추가 (Enter 키) ====================
        private void TagTextBox_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Enter)
                return;

            if (sender is not TextBox tb)
                return;

            if (tb.DataContext is not TagRow row)
                return;

            var text = tb.Text?.Trim();
            // ✅ placeholder면 무시
            if (string.IsNullOrEmpty(text) || text == TagPlaceholderText)
                return;
            // "#Tag" 형식으로 통일
            if (!text.StartsWith("#"))
                text = "#" + text;

            // 중복 방지
            if (!row.Tags.Contains(text))
                row.Tags.Add(text);

            // 입력창 비우기 (LostFocus에서 placeholder 다시 복구)
            row.NewTagText = string.Empty;
            tb.Text = string.Empty;

            // 포커스를 유지해서 계속 입력할 수 있게 할지,
            // LostFocus 유도할지는 취향인데, 지금은 유지.
            e.Handled = true;
        }

        // ==================== 태그 삭제 (칩의 X 버튼) ====================
        private void RemoveTagButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is not FrameworkElement fe)
                return;

            // 칩의 DataContext는 태그 문자열 자체
            if (fe.DataContext is not string tagText)
                return;

            // 이 칩이 속한 행(TagRow) 찾기
            var row = FindAncestor<DataGridRow>(fe);
            if (row?.DataContext is not TagRow tagRow)
                return;

            if (tagRow.Tags.Contains(tagText))
                tagRow.Tags.Remove(tagText);

            UpdateConfigTags(tagRow);
        }

        // TagRow.Tags → _config.Tags 동기화
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

        private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
        {
            while (current != null)
            {
                if (current is T wanted)
                    return wanted;

                current = VisualTreeHelper.GetParent(current);
            }
            return null;
        }
        private void TagInput_GotFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                if (tb.Text == TagPlaceholderText)
                {
                    tb.Text = string.Empty;
                }

                // 실제 입력 시 폰트 색: 기본 텍스트 색
                if (TryFindResource("AppTextPrimaryBrush") is Brush primary)
                    tb.Foreground = primary;
                else
                    tb.Foreground = Brushes.Black;
            }
        }

        private void TagInput_LostFocus(object sender, RoutedEventArgs e)
        {
            if (sender is TextBox tb)
            {
                if (string.IsNullOrWhiteSpace(tb.Text))
                {
                    tb.Text = TagPlaceholderText;

                    // placeholder 색: 연한 텍스트 색
                    if (TryFindResource("AppTextSecondaryBrush") is Brush secondary)
                        tb.Foreground = secondary;
                    else
                        tb.Foreground = Brushes.Gray;
                }
            }
        }

    }
}
