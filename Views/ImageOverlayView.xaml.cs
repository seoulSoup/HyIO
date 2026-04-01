using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using WF = System.Windows.Forms;

namespace HyIO.Views
{
    public partial class ImageOverlayView : UserControl
    {
        public sealed class PreviewImageMatch
        {
            public string CommandText { get; init; } = string.Empty;
            public string FilePath { get; init; } = string.Empty;
            public string FileName { get; init; } = string.Empty;
            public BitmapImage Thumbnail { get; init; } = null!;
            public string MatchSummary { get; init; } = string.Empty;
        }

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

        private sealed class ImageLoadProgress
        {
            public string Message { get; init; } = string.Empty;
            public double Value { get; init; }
        }

        private sealed class ImageLoadResult
        {
            public HashSet<string> SeenPaths { get; init; } = new(StringComparer.OrdinalIgnoreCase);
            public List<ImageItem> Items { get; init; } = new();
        }

        private readonly AppConfig _config;
        private readonly ObservableCollection<ImageItem> _items = new();
        private bool _isThumbDragging;
        private Point _thumbDragStartPoint;
        private double _thumbDragStartOffset;
        private CancellationTokenSource _loadImagesCts;

        public ImageOverlayView(AppConfig config)
        {
            InitializeComponent();
            _config = config;
            ImageItemsControl.ItemsSource = _items;
        }

        public async Task LoadImagesAsync()
        {
            _loadImagesCts?.Cancel();
            _loadImagesCts?.Dispose();

            var cts = new CancellationTokenSource();
            _loadImagesCts = cts;

            SetLoadingState(true, "이미지 목록을 불러오는 중...", 0);

            try
            {
                var progress = new Progress<ImageLoadProgress>(p => SetLoadingState(true, p.Message, p.Value));
                var result = await Task.Run(() => BuildImageLoadResult(progress, cts.Token), cts.Token);

                if (cts.IsCancellationRequested)
                    return;

                ApplyLoadResult(result);
            }
            catch (OperationCanceledException)
            {
            }
            catch (Exception ex)
            {
                SetLoadingState(false, string.Empty, 0);
                MessageBox.Show($"이미지 목록을 불러오는 중 오류가 발생했습니다.\n\n{ex.Message}",
                    "오류", MessageBoxButton.OK, MessageBoxImage.Error);
            }
            finally
            {
                if (ReferenceEquals(_loadImagesCts, cts))
                {
                    SetLoadingState(false, string.Empty, 0);
                    _loadImagesCts.Dispose();
                    _loadImagesCts = null;
                }
            }
        }

        public IReadOnlyList<PreviewImageMatch> FindPreviewMatches(string commandText, int maxResults = 50)
        {
            var normalizedCommand = NormalizeLookupToken(commandText);
            if (string.IsNullOrWhiteSpace(normalizedCommand))
                return Array.Empty<PreviewImageMatch>();

            var candidates = _config.Folders
                .Where(f => f.Enabled && Directory.Exists(f.Path))
                .SelectMany(f =>
                {
                    try
                    {
                        return Directory.EnumerateFiles(f.Path);
                    }
                    catch
                    {
                        return Enumerable.Empty<string>();
                    }
                })
                .Where(IsSupportedImagePath)
                .Select(path => CreatePreviewCandidate(path, normalizedCommand))
                .Where(candidate => candidate != null)
                .OrderByDescending(candidate => candidate.IsTagMatch)
                .ThenByDescending(candidate => candidate.IsExactMatch)
                .ThenByDescending(candidate => candidate.UsageCount)
                .ThenBy(candidate => candidate.FileName, StringComparer.OrdinalIgnoreCase)
                .Take(Math.Max(1, maxResults))
                .ToList();

            var matches = new List<PreviewImageMatch>(candidates.Count);
            foreach (var candidate in candidates)
            {
                var thumbnail = LoadPreviewBitmap(candidate.FilePath);
                if (thumbnail == null)
                    continue;

                matches.Add(new PreviewImageMatch
                {
                    CommandText = commandText.Trim(),
                    FilePath = candidate.FilePath,
                    FileName = candidate.FileName,
                    Thumbnail = thumbnail,
                    MatchSummary = candidate.MatchSummary
                });
            }

            return matches;
        }

        private ImageLoadResult BuildImageLoadResult(IProgress<ImageLoadProgress> progress, CancellationToken cancellationToken)
        {
            var enabledFolders = _config.Folders
                .Where(f => f.Enabled && Directory.Exists(f.Path))
                .ToList();

            var candidateFiles = new List<string>();
            int folderCount = enabledFolders.Count;

            if (folderCount == 0)
            {
                progress.Report(new ImageLoadProgress
                {
                    Message = "등록된 폴더가 없습니다.",
                    Value = 100
                });

                return new ImageLoadResult();
            }

            for (int folderIndex = 0; folderIndex < folderCount; folderIndex++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var folder = enabledFolders[folderIndex];
                progress.Report(new ImageLoadProgress
                {
                    Message = $"{Path.GetFileName(folder.Path)} 폴더를 스캔하는 중...",
                    Value = folderIndex * 40d / folderCount
                });

                foreach (var file in Directory.EnumerateFiles(folder.Path))
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var ext = Path.GetExtension(file);
                    if (string.IsNullOrEmpty(ext))
                        continue;

                    ext = ext.ToLowerInvariant();
                    if (ext == ".svg" || ext == ".lnk")
                        continue;

                    if (!ImageExtensions.Contains(ext))
                        continue;

                    candidateFiles.Add(file);
                }

                progress.Report(new ImageLoadProgress
                {
                    Message = $"{Path.GetFileName(folder.Path)} 폴더 스캔 완료",
                    Value = (folderIndex + 1) * 40d / folderCount
                });
            }

            var seenNow = new HashSet<string>(candidateFiles, StringComparer.OrdinalIgnoreCase);
            var rebuiltItems = new List<ImageItem>(candidateFiles.Count);

            if (candidateFiles.Count == 0)
            {
                progress.Report(new ImageLoadProgress
                {
                    Message = "표시할 이미지가 없습니다.",
                    Value = 100
                });

                return new ImageLoadResult
                {
                    SeenPaths = seenNow,
                    Items = rebuiltItems
                };
            }

            for (int i = 0; i < candidateFiles.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var file = candidateFiles[i];
                try
                {
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
                catch
                {
                    continue;
                }

                progress.Report(new ImageLoadProgress
                {
                    Message = $"이미지 미리보기를 만드는 중... ({i + 1}/{candidateFiles.Count})",
                    Value = 40d + ((i + 1) * 60d / Math.Max(1, candidateFiles.Count))
                });
            }

            return new ImageLoadResult
            {
                SeenPaths = seenNow,
                Items = rebuiltItems
            };
        }

        private void ApplyLoadResult(ImageLoadResult result)
        {
            var seenNow = result.SeenPaths;
            var rebuiltItems = result.Items;

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

        private void SetLoadingState(bool isLoading, string message, double progressValue)
        {
            LoadingOverlay.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            LoadingProgressBar.Value = Math.Max(0, Math.Min(100, progressValue));
            LoadingStatusText.Text = string.IsNullOrWhiteSpace(message) ? "이미지 목록을 불러오는 중..." : message;
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

        public void CopyMatchToClipboard(PreviewImageMatch match, bool autoPaste)
        {
            if (match == null || string.IsNullOrWhiteSpace(match.FilePath))
                return;

            var imageItem = _items.FirstOrDefault(item =>
                string.Equals(item.FilePath, match.FilePath, StringComparison.OrdinalIgnoreCase));

            if (imageItem != null)
            {
                UpdateUsageCount(imageItem);
            }

            CopyImageToClipboard(match.FilePath);

            if (autoPaste)
            {
                WF.SendKeys.SendWait("^v");
            }
        }

        private PreviewCandidate CreatePreviewCandidate(string filePath, string normalizedCommand)
        {
            var fileName = Path.GetFileName(filePath);
            if (string.IsNullOrWhiteSpace(fileName))
                return null;

            var fileNameWithoutExtension = Path.GetFileNameWithoutExtension(filePath) ?? string.Empty;
            var normalizedFileName = NormalizeLookupToken(fileNameWithoutExtension);
            bool fileNameExactMatch = normalizedFileName == normalizedCommand;
            bool fileNameContainsMatch = normalizedFileName.Contains(normalizedCommand, StringComparison.Ordinal);

            var tagKey = TagKeyHelper.GetImageKey(filePath);
            bool tagExactMatch = false;
            bool tagContainsMatch = false;
            string matchedTag = string.Empty;

            if (_config.Tags.TryGetValue(tagKey, out var pathTags))
            {
                foreach (var tag in pathTags)
                {
                    var normalizedTag = NormalizeLookupToken(tag);
                    if (normalizedTag == normalizedCommand)
                    {
                        tagExactMatch = true;
                        matchedTag = tag;
                        break;
                    }

                    if (!tagContainsMatch && normalizedTag.Contains(normalizedCommand, StringComparison.Ordinal))
                    {
                        tagContainsMatch = true;
                        matchedTag = tag;
                    }
                }
            }

            if (!tagExactMatch && !tagContainsMatch && _config.Tags.TryGetValue(fileName, out var legacyTags))
            {
                foreach (var tag in legacyTags)
                {
                    var normalizedTag = NormalizeLookupToken(tag);
                    if (normalizedTag == normalizedCommand)
                    {
                        tagExactMatch = true;
                        matchedTag = tag;
                        break;
                    }

                    if (!tagContainsMatch && normalizedTag.Contains(normalizedCommand, StringComparison.Ordinal))
                    {
                        tagContainsMatch = true;
                        matchedTag = tag;
                    }
                }
            }

            bool isTagMatch = tagExactMatch || tagContainsMatch;
            bool isFileNameMatch = fileNameExactMatch || fileNameContainsMatch;

            if (!isFileNameMatch && !isTagMatch)
                return null;

            int usageCount = _config.ImageUsage.TryGetValue(tagKey, out var usage) ? usage.Count : 0;
            bool isExactMatch = fileNameExactMatch || tagExactMatch;

            string matchSummary = isTagMatch
                ? $"태그 매치: {matchedTag}"
                : $"파일명 매치: {fileNameWithoutExtension}";

            return new PreviewCandidate
            {
                FilePath = filePath,
                FileName = fileName,
                UsageCount = usageCount,
                IsTagMatch = isTagMatch,
                IsExactMatch = isExactMatch,
                MatchSummary = matchSummary
            };
        }

        private static bool IsSupportedImagePath(string path)
        {
            var ext = Path.GetExtension(path);
            if (string.IsNullOrWhiteSpace(ext))
                return false;

            ext = ext.ToLowerInvariant();
            if (ext == ".svg" || ext == ".lnk")
                return false;

            return ImageExtensions.Contains(ext);
        }

        private static string NormalizeLookupToken(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return string.Empty;

            return text.Trim().TrimStart('#', '/').ToLowerInvariant();
        }

        private static BitmapImage LoadPreviewBitmap(string filePath)
        {
            try
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.UriSource = new Uri(filePath);
                bmp.DecodePixelWidth = 256;
                bmp.EndInit();
                bmp.Freeze();
                return bmp;
            }
            catch
            {
                return null;
            }
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
                    CopyImageToClipboard(item.FilePath);
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

        private static void CopyImageToClipboard(string filePath)
        {
            var extension = Path.GetExtension(filePath)?.ToLowerInvariant();

            if (extension == ".gif")
            {
                var fileDropList = new StringCollection();
                fileDropList.Add(filePath);
                Clipboard.SetFileDropList(fileDropList);
                return;
            }

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.UriSource = new Uri(filePath);
            image.EndInit();
            image.Freeze();

            BitmapSource clipboardImage = extension == ".png"
                ? FlattenImageOnWhiteBackground(image)
                : image;

            var dataObject = new DataObject();
            dataObject.SetImage(clipboardImage);
            Clipboard.SetDataObject(dataObject, true);
        }

        private static BitmapSource FlattenImageOnWhiteBackground(BitmapSource source)
        {
            int pixelWidth = Math.Max(1, source.PixelWidth);
            int pixelHeight = Math.Max(1, source.PixelHeight);
            double dpiX = source.DpiX > 0 ? source.DpiX : 96;
            double dpiY = source.DpiY > 0 ? source.DpiY : 96;

            var visual = new DrawingVisual();
            using (var context = visual.RenderOpen())
            {
                context.DrawRectangle(Brushes.White, null, new Rect(0, 0, pixelWidth, pixelHeight));
                context.DrawImage(source, new Rect(0, 0, pixelWidth, pixelHeight));
            }

            var flattened = new RenderTargetBitmap(pixelWidth, pixelHeight, dpiX, dpiY, PixelFormats.Pbgra32);
            flattened.Render(visual);
            flattened.Freeze();
            return flattened;
        }

        private sealed class PreviewCandidate
        {
            public string FilePath { get; init; } = string.Empty;
            public string FileName { get; init; } = string.Empty;
            public int UsageCount { get; init; }
            public bool IsTagMatch { get; init; }
            public bool IsExactMatch { get; init; }
            public string MatchSummary { get; init; } = string.Empty;
        }
    }
}
