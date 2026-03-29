using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Imaging;
using WF = System.Windows.Forms;

namespace HyIO.Views
{
    public partial class ImageOverlayView : UserControl
    {
        private static readonly string[] ImageExtensions =
        {
            ".png", ".jpg", ".jpeg", ".bmp", ".gif"
        };

        public class ImageItem : INotifyPropertyChanged
        {
            private string _tagsText = string.Empty;
            private int _usageCount;

            public string FilePath { get; set; } = string.Empty;
            public string FolderPath => Path.GetDirectoryName(FilePath) ?? string.Empty;
            public string FileName => Path.GetFileName(FilePath);
            public BitmapImage Thumbnail { get; set; } = null!;
            public ObservableCollection<string> Tags { get; } = new();

            public int UsageCount
            {
                get => _usageCount;
                set
                {
                    if (_usageCount == value)
                        return;

                    _usageCount = value;
                    OnPropertyChanged();
                }
            }

            public string TagsText
            {
                get => _tagsText;
                set
                {
                    if (_tagsText == value)
                        return;

                    _tagsText = value;
                    OnPropertyChanged();
                }
            }

            public event PropertyChangedEventHandler PropertyChanged;

            private void OnPropertyChanged([CallerMemberName] string propertyName = null)
            {
                PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
            }
        }

        private readonly AppConfig _config;
        private readonly ObservableCollection<ImageItem> _items = new();
        private bool _isThumbDragging;
        private Point _thumbDragStartPoint;
        private double _thumbDragStartOffset;

        public ImageOverlayView(AppConfig config)
        {
            InitializeComponent();
            _config = config;
            ImageItemsControl.ItemsSource = _items;

            LoadImages();
        }

        public void LoadImages()
        {
            var seenNow = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var rebuiltItems = new List<ImageItem>();

            foreach (var folder in _config.Folders.Where(f => f.Enabled && Directory.Exists(f.Path)))
            {
                foreach (var file in Directory.EnumerateFiles(folder.Path))
                {
                    var ext = Path.GetExtension(file);
                    if (string.IsNullOrEmpty(ext))
                        continue;

                    ext = ext.ToLowerInvariant();
                    if (ext == ".svg" || ext == ".lnk")
                        continue;

                    if (!ImageExtensions.Contains(ext))
                        continue;

                    seenNow.Add(file);

                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.UriSource = new Uri(file);
                    bmp.DecodePixelWidth = 128;
                    bmp.EndInit();
                    bmp.Freeze();

                    rebuiltItems.Add(new ImageItem
                    {
                        FilePath = file,
                        Thumbnail = bmp
                    });
                }
            }

            var activeImageKeys = new HashSet<string>(
                seenNow.Select(TagKeyHelper.GetImageKey),
                StringComparer.OrdinalIgnoreCase);

            var activeFileNames = new HashSet<string>(
                seenNow.Select(p => Path.GetFileName(p) ?? string.Empty)
                    .Where(name => !string.IsNullOrEmpty(name)),
                StringComparer.OrdinalIgnoreCase);

            var tagsToRemove = _config.Tags.Keys
                .Where(key =>
                {
                    if (TagKeyHelper.IsPathBasedKey(key))
                        return !activeImageKeys.Contains(TagKeyHelper.GetImageKey(key));

                    return !activeFileNames.Contains(key);
                })
                .ToList();

            foreach (var key in tagsToRemove)
            {
                _config.Tags.Remove(key);
            }

            if (tagsToRemove.Count > 0)
            {
                ConfigManager.Save(_config);
            }

            _items.Clear();
            foreach (var item in rebuiltItems
                .OrderBy(i => i.FileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.FilePath, StringComparer.OrdinalIgnoreCase))
            {
                _items.Add(item);
            }

            CleanupUsageEntries(seenNow);
            RefreshTags();
        }

        public void RefreshTags()
        {
            foreach (var item in _items)
            {
                var tagKey = TagKeyHelper.GetImageKey(item.FilePath);
                item.UsageCount = _config.ImageUsage.TryGetValue(tagKey, out var usage) ? usage.Count : 0;

                if (_config.Tags.TryGetValue(tagKey, out var tags))
                {
                    UpdateItemTags(item, tags);
                }
                else if (_config.Tags.TryGetValue(item.FileName, out var legacyTags))
                {
                    UpdateItemTags(item, legacyTags);
                }
                else
                {
                    item.Tags.Clear();
                    item.TagsText = string.Empty;
                }
            }

            SortItemsByUsage();
            ApplyFilter();
        }

        private void CleanupUsageEntries(HashSet<string> activePaths)
        {
            var activeImageKeys = new HashSet<string>(
                activePaths.Select(TagKeyHelper.GetImageKey),
                StringComparer.OrdinalIgnoreCase);

            var activeFolderPaths = new HashSet<string>(
                _config.Folders.Select(f => NormalizeFolderPath(f.Path)),
                StringComparer.OrdinalIgnoreCase);

            var usageKeysToRemove = _config.ImageUsage
                .Where(kvp =>
                    !activeImageKeys.Contains(TagKeyHelper.GetImageKey(kvp.Key)) ||
                    !activeFolderPaths.Contains(NormalizeFolderPath(kvp.Value.FolderPath)))
                .Select(kvp => kvp.Key)
                .ToList();

            if (usageKeysToRemove.Count == 0)
                return;

            foreach (var key in usageKeysToRemove)
            {
                _config.ImageUsage.Remove(key);
            }

            ConfigManager.Save(_config);
        }

        private void SortItemsByUsage()
        {
            var ordered = _items
                .OrderByDescending(i => i.UsageCount)
                .ThenBy(i => i.FileName, StringComparer.OrdinalIgnoreCase)
                .ThenBy(i => i.FilePath, StringComparer.OrdinalIgnoreCase)
                .ToList();

            _items.Clear();
            foreach (var item in ordered)
            {
                _items.Add(item);
            }
        }

        private void UpdateUsageCount(ImageItem item)
        {
            var imageKey = TagKeyHelper.GetImageKey(item.FilePath);
            if (!_config.ImageUsage.TryGetValue(imageKey, out var usageEntry))
            {
                usageEntry = new ImageUsageEntry
                {
                    FolderPath = item.FolderPath,
                    Count = 0
                };
                _config.ImageUsage[imageKey] = usageEntry;
            }

            usageEntry.FolderPath = item.FolderPath;
            usageEntry.Count++;
            item.UsageCount = usageEntry.Count;

            ConfigManager.Save(_config);
            SortItemsByUsage();
            ApplyFilter();
        }

        private static string NormalizeFolderPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            try
            {
                return Path.GetFullPath(path)
                    .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
            catch
            {
                return path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            }
        }

        private static void UpdateItemTags(ImageItem item, IEnumerable<string> tags)
        {
            var normalizedTags = tags
                .Where(t => !string.IsNullOrWhiteSpace(t))
                .Select(t => t.Trim())
                .ToList();

            item.Tags.Clear();
            foreach (var tag in normalizedTags)
            {
                item.Tags.Add(tag);
            }

            item.TagsText = string.Join(" ", normalizedTags);
        }

        private void ApplyFilter()
        {
            string keyword = SearchBox.Text?.Trim() ?? string.Empty;
            if (string.IsNullOrEmpty(keyword))
            {
                ImageItemsControl.ItemsSource = _items;
            }
            else
            {
                keyword = keyword.ToLowerInvariant();
                var filtered = _items.Where(i =>
                    i.FileName.ToLowerInvariant().Contains(keyword) ||
                    (i.TagsText ?? string.Empty).ToLowerInvariant().Contains(keyword)).ToList();

                ImageItemsControl.ItemsSource = filtered;
            }
        }

        private void SearchBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            ApplyFilter();
        }

        private void ImageScrollViewer_ScrollChanged(object sender, ScrollChangedEventArgs e)
        {
            UpdateCustomScrollbar();
        }

        private void ImageCustomScrollTrack_SizeChanged(object sender, SizeChangedEventArgs e)
        {
            UpdateCustomScrollbar();
        }

        private void UpdateCustomScrollbar()
        {
            if (ImageCustomScrollTrack == null || ImageCustomScrollThumb == null || ImageScrollViewer == null)
                return;

            double trackHeight = ImageCustomScrollTrack.ActualHeight;
            if (trackHeight <= 0)
                return;

            double extent = ImageScrollViewer.ExtentHeight;
            double viewport = ImageScrollViewer.ViewportHeight;
            double scrollable = ImageScrollViewer.ScrollableHeight;

            if (extent <= 0 || scrollable <= 0)
            {
                double fullHeight = Math.Max(0, trackHeight - 8);
                ImageCustomScrollThumb.Height = fullHeight;
                ImageCustomScrollThumb.Margin = new Thickness(4);
                return;
            }

            double innerTrackHeight = trackHeight - 8;
            double thumbHeight = Math.Max(32, innerTrackHeight * (viewport / extent));
            if (thumbHeight > innerTrackHeight)
                thumbHeight = innerTrackHeight;

            ImageCustomScrollThumb.Height = thumbHeight;

            double travel = innerTrackHeight - thumbHeight;
            if (travel < 0)
                travel = 0;

            double t = scrollable <= 0 ? 0.0 : ImageScrollViewer.VerticalOffset / scrollable;
            double topMargin = 4 + travel * t;
            ImageCustomScrollThumb.Margin = new Thickness(4, topMargin, 4, 4);
        }

        private void ImageCustomScrollThumb_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (ImageScrollViewer == null || ImageCustomScrollTrack == null)
                return;

            _isThumbDragging = true;
            _thumbDragStartPoint = e.GetPosition(ImageCustomScrollTrack);
            _thumbDragStartOffset = ImageScrollViewer.VerticalOffset;

            ImageCustomScrollThumb.CaptureMouse();
            e.Handled = true;
        }

        private void ImageCustomScrollThumb_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (!_isThumbDragging)
                return;

            _isThumbDragging = false;
            ImageCustomScrollThumb.ReleaseMouseCapture();
            e.Handled = true;
        }

        private void ImageCustomScrollThumb_MouseMove(object sender, MouseEventArgs e)
        {
            if (!_isThumbDragging)
                return;

            if (ImageScrollViewer == null || ImageCustomScrollTrack == null)
                return;

            double scrollable = ImageScrollViewer.ScrollableHeight;
            if (scrollable <= 0)
                return;

            double trackHeight = ImageCustomScrollTrack.ActualHeight;
            double innerTrackHeight = trackHeight - 4;
            double thumbHeight = ImageCustomScrollThumb.ActualHeight;
            double travel = innerTrackHeight - thumbHeight;
            if (travel <= 0)
                return;

            double currentY = e.GetPosition(ImageCustomScrollTrack).Y;
            double deltaY = currentY - _thumbDragStartPoint.Y;
            double proportion = deltaY / travel;
            double newOffset = _thumbDragStartOffset + proportion * scrollable;

            if (newOffset < 0) newOffset = 0;
            if (newOffset > scrollable) newOffset = scrollable;

            ImageScrollViewer.ScrollToVerticalOffset(newOffset);
        }

        private void ImageButton_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.DataContext is ImageItem item)
            {
                try
                {
                    UpdateUsageCount(item);

                    var fileDropList = new StringCollection();
                    fileDropList.Add(item.FilePath);
                    Clipboard.SetFileDropList(fileDropList);
                    var win = Window.GetWindow(this);

                    if (win != null)
                    {
                        win.Hide();
                        win.ShowInTaskbar = false;
                    }

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
