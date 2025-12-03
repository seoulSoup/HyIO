using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media.Imaging;
using WF = System.Windows.Forms;

namespace HyIO.Views
{
    public partial class ImageOverlayView : UserControl
    {
        public class OverlayImageItem
        {
            public string FilePath { get; set; } = "";
            public string FileName => Path.GetFileName(FilePath);
            public BitmapImage Thumbnail { get; set; } = null!;
            public string TagsText { get; set; } = "";
        }

        private readonly AppConfig _config;
        private readonly ObservableCollection<OverlayImageItem> _items = new();

        public ImageOverlayView(AppConfig config)
        {
            InitializeComponent();
            _config = config;
            ImageItemsControl.ItemsSource = _items;

            LoadImages();
        }

        private void LoadImages()
        {
            _items.Clear();

            var enabledFolders = _config.Folders
                                        .Where(f => f.Enabled && Directory.Exists(f.Path))
                                        .Select(f => f.Path)
                                        .ToList();

            string[] exts = new[] { ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp" };

            foreach (var folder in enabledFolders)
            {
                foreach (var file in Directory.EnumerateFiles(folder, "*.*", SearchOption.TopDirectoryOnly)
                                              .Where(f => exts.Contains(Path.GetExtension(f), StringComparer.OrdinalIgnoreCase)))
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(file);
                    bmp.DecodePixelWidth = 256;
                    bmp.EndInit();
                    bmp.Freeze();

                    var key = Path.GetFileName(file);
                    _config.Tags.TryGetValue(key, out var tags);

                    _items.Add(new OverlayImageItem
                    {
                        FilePath = file,
                        Thumbnail = bmp,
                        TagsText = tags != null ? string.Join(", ", tags) : ""
                    });
                }
            }

            ApplyFilter();
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
            if (sender is Button btn && btn.DataContext is OverlayImageItem item)
            {
                try
                {
                    // 이미지 클립보드에 넣기
                    var bmp = new BitmapImage(new Uri(item.FilePath));
                    Clipboard.SetImage(bmp);

                    // 자동 붙여넣기
                    if (App.Config.AutoPasteEnabled)
                    {
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
