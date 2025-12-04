using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using System.Collections.Generic;
using WF = System.Windows.Forms;

namespace HyIO.Views
{
    public partial class ImageOverlayView : UserControl
    {
        private static readonly string[] ImageExtensions =
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".gif"
            // svg, lnk는 여기 안 넣으니까 자동으로 skip 대상
        };
        public class ImageItem
        {
            public string FilePath { get; set; } = "";
            public string FileName => Path.GetFileName(FilePath);
            public BitmapImage Thumbnail { get; set; } = null!;
            public string TagsText { get; set; } = "";
        }

        private readonly AppConfig _config;
        private readonly ObservableCollection<ImageItem> _items = new();

        public ImageOverlayView(AppConfig config)
        {
            InitializeComponent();
            _config = config;
            ImageItemsControl.ItemsSource = _items;

            LoadImages();
        }

        public void LoadImages()
        {
            // 1) 이미 로딩된 파일 경로 캐시 (이미지 컬렉션 성능용)
            var existingPaths = new HashSet<string>(
                _items.Select(i => i.FilePath),    // ← 네 ImageItem에 맞는 프로퍼티 이름
                StringComparer.OrdinalIgnoreCase);

            // 2) 이번 스캔에서 실제로 발견된 파일들 (전체 경로 기준)
            var seenNow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var folder in _config.Folders.Where(f => f.Enabled && Directory.Exists(f.Path)))
            {
                foreach (var file in Directory.EnumerateFiles(folder.Path))
                {
                    var ext = Path.GetExtension(file);
                    if (string.IsNullOrEmpty(ext))
                        continue;

                    ext = ext.ToLowerInvariant();

                    // svg, lnk는 스킵
                    if (ext == ".svg" || ext == ".lnk")
                        continue;

                    // 우리가 지원하는 이미지 확장자만
                    if (!ImageExtensions.Contains(ext))
                        continue;

                    seenNow.Add(file);

                    // 이미 컬렉션에 있는 파일이면 썸네일 재생성 스킵
                    if (existingPaths.Contains(file))
                        continue;

                    // 새로운 파일만 썸네일 생성
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(file);
                    bmp.DecodePixelWidth = 128; // 원래 쓰던 썸네일 크기
                    bmp.EndInit();
                    bmp.Freeze();

                    var item = new ImageItem              // ← 네 프로젝트 타입 이름에 맞게
                    {
                        FilePath = file,
                        Thumbnail = bmp
                    };

                    _items.Add(item);
                }
            }

            // 3) 현재 활성 폴더들에 더 이상 존재하지 않는 파일은 _items에서 제거
            for (int i = _items.Count - 1; i >= 0; i--)
            {
                var path = _items[i].FilePath;
                if (!seenNow.Contains(path))
                {
                    _items.RemoveAt(i);
                }
            }

            // 4) 🔥 태그 정보 정리: 현재 존재하는 파일 이름만 남기기

            // seenNow는 전체 경로이므로, 파일 이름만 추려서 HashSet 생성
            var activeFileNames = new HashSet<string>(
                seenNow.Select(p => Path.GetFileName(p) ?? string.Empty)
                    .Where(name => !string.IsNullOrEmpty(name)),
                StringComparer.OrdinalIgnoreCase);

            // _config.Tags의 key는 "파일 이름" 기준이었으니까
            // 활성 파일 이름 목록에 없는 키들은 전부 삭제 대상
            var tagsToRemove = _config.Tags.Keys
                .Where(key => !activeFileNames.Contains(key))
                .ToList(); // Dictionary 순회 중에 Remove 못하니까 리스트로 복사

            foreach (var key in tagsToRemove)
            {
                _config.Tags.Remove(key);
            }

            if (tagsToRemove.Count > 0)
            {
                ConfigManager.Save(_config);
            }
        }


        private void ApplyFilter()
        {
            string keyword = SearchBox.Text?.Trim() ?? "";
            if (string.IsNullOrEmpty(keyword))
            {
                ImageItemsControl.ItemsSource = _items;
            }
            else
            {
                keyword = keyword.ToLowerInvariant();
                var filtered = _items.Where(i =>
                    i.FileName.ToLowerInvariant().Contains(keyword) ||
                    (i.TagsText ?? "").ToLowerInvariant().Contains(keyword)).ToList();

                ImageItemsControl.ItemsSource = filtered;
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void ImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ImageItem item)
            {
                try
                {
                    // 이미지 클립보드에 넣기
                    var bmp = new BitmapImage(new Uri(item.FilePath));
                    Clipboard.SetImage(bmp);
                    var win = Window.GetWindow(this);
                    

                    // 자동 붙여넣기
                    if (App.Config.AutoPasteEnabled)
                    {
                        if (win != null)
                        {
                            win.Hide();
                        }
                        WF.SendKeys.SendWait("^v");
                    }
                    
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"클립보드로 복사하는 중 오류가 발생했습니다.\n\n{ex.Message}",
                        "오류", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }
    }
}
