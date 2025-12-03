using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Windows.Controls;

namespace HyIO.Views
{
    public partial class TagManagerView : UserControl
    {
        public class TagRow
        {
            public string FileName { get; set; } = "";
            public string TagsText { get; set; } = "";
        }

        private readonly AppConfig _config;
        private readonly ObservableCollection<TagRow> _rows = new();

        public TagManagerView(AppConfig config)
        {
            InitializeComponent();
            _config = config;

            TagGrid.ItemsSource = _rows;
            LoadTags();

            TagGrid.CellEditEnding += TagGrid_CellEditEnding;
        }

        private void LoadTags()
        {
            _rows.Clear();

            // 현재 폴더에서 찾을 수 있는 파일들만 대상으로
            var enabledFolders = _config.Folders
                                        .Where(f => f.Enabled && Directory.Exists(f.Path))
                                        .Select(f => f.Path)
                                        .ToList();

            var allFiles = enabledFolders
                .SelectMany(p => Directory.EnumerateFiles(p))
                .Select(Path.GetFileName)
                .Distinct()
                .OrderBy(fn => fn);

            foreach (var fileName in allFiles)
            {
                _config.Tags.TryGetValue(fileName, out var tags);

                _rows.Add(new TagRow
                {
                    FileName = fileName!,
                    TagsText = tags != null ? string.Join(", ", tags) : ""
                });
            }
        }

        private void TagGrid_CellEditEnding(object sender, DataGridCellEditEndingEventArgs e)
        {
            if (e.EditAction != DataGridEditAction.Commit) return;

            // 편집이 완료된 뒤에 TextBox 값 읽기
            if (e.Row.Item is TagRow row)
            {
                var textBox = e.EditingElement as TextBox;
                if (textBox != null)
                    row.TagsText = textBox.Text;

                var tags = row.TagsText.Split(',')
                                       .Select(t => t.Trim())
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
        }
    }
}
