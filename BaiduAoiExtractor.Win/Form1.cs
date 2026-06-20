using System.Globalization;
using System.Text;
using System.Text.Json;

namespace BaiduAoiExtractor.Win;

public partial class Form1 : Form
{
    private readonly TextBox _searchTextBox = new();
    private readonly ListBox _suggestionsListBox = new();
    private readonly ListView _excelPreviewList = new();
    private readonly ListView _placesList = new();
    private readonly TextBox _outputTextBox = new() { Text = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "BaiduAoiOutputs") };
    private readonly TextBox _logTextBox = new();
    private readonly Button _searchButton = new();
    private readonly Button _addButton = new();
    private readonly Button _removeButton = new();
    private readonly Button _importExcelButton = new();
    private readonly Button _addExcelSelectedButton = new();
    private readonly Button _addExcelAllButton = new();
    private readonly Button _clearExcelPreviewButton = new();
    private readonly Button _browseButton = new();
    private readonly Button _startButton = new();
    private readonly Button _cancelButton = new();
    private readonly Button _exportAoiButton = new();
    private readonly Button _exportPoiButton = new();
    private readonly Button _exportMainPoiButton = new();
    private readonly NumericUpDown _threadsBox = new() { Minimum = 1, Maximum = 5, Increment = 1, Value = 1 };
    private readonly NumericUpDown _minDelayBox = new() { Minimum = 0, Maximum = 60000, Increment = 500, Value = 1000 };
    private readonly NumericUpDown _maxDelayBox = new() { Minimum = 0, Maximum = 60000, Increment = 500, Value = 4000 };
    private readonly CheckBox _showBrowserBox = new();
    private readonly CheckBox _debugBox = new();
    private readonly CheckBox _excelBox = new() { Checked = true };
    private readonly CheckBox _geoJsonBox = new() { Checked = true };
    private readonly CheckBox _autoFirstCandidateBox = new() { Checked = true };
    private readonly ProgressBar _progressBar = new();
    private readonly ListView _resultList = new();
    private readonly WebBrowser _mapBrowser = new();
    private readonly Label _mapPlaceholder = new();
    private readonly List<PlaceInput> _places = [];
    private readonly List<PlaceInput> _excelPreviewPlaces = [];
    private readonly List<CrawlResult> _results = [];
    private readonly BaiduSuggestionService _suggestionService = new();

    private readonly SplitContainer _rootSplit = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
    private readonly SplitContainer _rightSplit = new() { Dock = DockStyle.Fill, Orientation = Orientation.Vertical };
    private readonly SplitContainer _topSplit = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };
    private readonly SplitContainer _resultsLogSplit = new() { Dock = DockStyle.Fill, Orientation = Orientation.Horizontal };

    private CancellationTokenSource? _cancellationTokenSource;
    private string _mapHtmlPath = string.Empty;

    public Form1()
    {
        InitializeComponent();
        SetInitialWindowSize();
        BuildUi();
        Load += (_, _) => SetLayoutDistances();
        Resize += (_, _) =>
        {
            SetLayoutDistances();
            AdjustListColumns();
        };
    }

    private void SetInitialWindowSize()
    {
        var area = Screen.PrimaryScreen?.WorkingArea ?? new Rectangle(0, 0, 1440, 900);
        ClientSize = new Size(
            Math.Max(1280, (int)(area.Width * 0.9)),
            Math.Max(800, (int)(area.Height * 0.9)));
        MinimumSize = new Size(1180, 760);
        StartPosition = FormStartPosition.CenterScreen;
        WindowState = FormWindowState.Maximized;
    }

    private void BuildUi()
    {
        Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
        BackColor = Color.FromArgb(245, 247, 250);

        _rootSplit.SplitterWidth = 6;
        _rootSplit.Panel1.Controls.Add(BuildLeftPanel());

        _rightSplit.SplitterWidth = 6;
        _rightSplit.Panel1.Controls.Add(BuildWorkPanel());
        _rightSplit.Panel2.Controls.Add(BuildMapPanel());

        _rootSplit.Panel2.Controls.Add(_rightSplit);
        Controls.Add(_rootSplit);
    }

    private void SetLayoutDistances()
    {
        SetSplitter(_rootSplit, 400);
        SetSplitter(_rightSplit, Math.Max(760, _rightSplit.Width - 320));
        SetSplitter(_topSplit, Math.Max(210, (int)(_topSplit.Height * 0.36)));
        SetSplitter(_resultsLogSplit, Math.Max(210, (int)(_resultsLogSplit.Height * 0.52)));
        AdjustListColumns();
    }

    private static void SetSplitter(SplitContainer split, int desired)
    {
        if (split.Width <= 0 || split.Height <= 0)
        {
            return;
        }

        var length = split.Orientation == Orientation.Vertical ? split.Width : split.Height;
        var max = length - split.SplitterWidth - split.Panel2MinSize;
        if (max < split.Panel1MinSize)
        {
            return;
        }

        split.SplitterDistance = Math.Clamp(desired, split.Panel1MinSize, max);
    }

    private Control BuildLeftPanel()
    {
        var outer = new Panel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            BackColor = Color.FromArgb(238, 242, 246),
            Padding = new Padding(10)
        };

        var content = new TableLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            Dock = DockStyle.Top
        };
        content.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        outer.Resize += (_, _) => content.Width = Math.Max(300, outer.ClientSize.Width - outer.Padding.Horizontal - 18);

        content.Controls.Add(BuildSearchGroup(), 0, 0);
        content.Controls.Add(BuildOutputGroup(), 0, 1);
        content.Controls.Add(BuildSettingsGroup(), 0, 2);
        content.Controls.Add(BuildExcelPreviewGroup(), 0, 3);
        content.Controls.Add(BuildActionGroup(), 0, 4);
        outer.Controls.Add(content);
        return outer;
    }

    private Control BuildSearchGroup()
    {
        var group = CreateGroup("搜索与添加");
        group.Height = 160;
        var layout = CreateGroupLayout(2);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 50));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        _searchTextBox.Multiline = false;
        _searchTextBox.ScrollBars = ScrollBars.None;
        _searchTextBox.PlaceholderText = "输入地点名或地址";
        _searchTextBox.Dock = DockStyle.Fill;
        layout.Controls.Add(_searchTextBox, 0, 0);

        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, Margin = new Padding(0, 6, 0, 0) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        ConfigureButton(_searchButton, "搜索候选", Color.FromArgb(72, 86, 102), 32);
        ConfigureButton(_addButton, "添加输入", Color.FromArgb(28, 92, 187), 32);
        _searchButton.Click += SearchButton_Click;
        _addButton.Click += AddButton_Click;
        buttons.Controls.Add(_searchButton, 0, 0);
        buttons.Controls.Add(_addButton, 1, 0);
        layout.Controls.Add(buttons, 0, 1);

        group.Controls.Add(layout);
        return group;
    }

    private Control BuildOutputGroup()
    {
        var group = CreateGroup("输出目录");
        group.Height = 125;
        var layout = CreateGroupLayout(2);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 32));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 36));

        _outputTextBox.Dock = DockStyle.Fill;
        _outputTextBox.Margin = new Padding(0, 0, 0, 4);
        layout.Controls.Add(_outputTextBox, 0, 0);

        ConfigureButton(_browseButton, "选择目录", Color.FromArgb(72, 86, 102), 32);
        _browseButton.Click += BrowseButton_Click;
        layout.Controls.Add(_browseButton, 0, 1);

        group.Controls.Add(layout);
        return group;
    }

    private Control BuildSettingsGroup()
    {
        var group = CreateGroup("爬取设置");
        group.Height = 350;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 7,
            Padding = new Padding(12, 16, 12, 8)
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 98));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        for (var i = 0; i < 7; i++)
        {
            layout.RowStyles.Add(new RowStyle(SizeType.Absolute, i < 2 ? 38 : 40));
        }

        layout.Controls.Add(MakeLabel("并发数量"), 0, 0);
        _threadsBox.Dock = DockStyle.Fill;
        layout.Controls.Add(_threadsBox, 1, 0);

        layout.Controls.Add(MakeLabel("延迟(ms)"), 0, 1);
        var delayRow = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0, 1, 0, 3) };
        delayRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        delayRow.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 24));
        delayRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
        _minDelayBox.Dock = DockStyle.Fill;
        _maxDelayBox.Dock = DockStyle.Fill;
        delayRow.Controls.Add(_minDelayBox, 0, 0);
        delayRow.Controls.Add(new Label { Text = "~", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleCenter, AutoSize = false }, 1, 0);
        delayRow.Controls.Add(_maxDelayBox, 2, 0);
        layout.Controls.Add(delayRow, 1, 1);

        ConfigureCheckBox(_showBrowserBox, "显示浏览器窗口");
        ConfigureCheckBox(_debugBox, "输出调试日志");
        ConfigureCheckBox(_excelBox, "输出 Excel 汇总", true);
        ConfigureCheckBox(_geoJsonBox, "输出 GeoJSON", true);
        ConfigureCheckBox(_autoFirstCandidateBox, "无UID时自动选第一个候选", true);

        layout.Controls.Add(_showBrowserBox, 0, 2);
        layout.SetColumnSpan(_showBrowserBox, 2);
        layout.Controls.Add(_debugBox, 0, 3);
        layout.SetColumnSpan(_debugBox, 2);
        layout.Controls.Add(_excelBox, 0, 4);
        layout.SetColumnSpan(_excelBox, 2);
        layout.Controls.Add(_geoJsonBox, 0, 5);
        layout.SetColumnSpan(_geoJsonBox, 2);
        layout.Controls.Add(_autoFirstCandidateBox, 0, 6);
        layout.SetColumnSpan(_autoFirstCandidateBox, 2);

        group.Controls.Add(layout);
        return group;
    }

    private Control BuildActionGroup()
    {
        var group = CreateGroup("运行控制");
        group.Height = 190;
        var layout = CreateGroupLayout(2);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 76));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 48));

        ConfigureButton(_startButton, "开始提取", Color.FromArgb(0, 120, 72), 68);
        _startButton.Font = new Font(Font.FontFamily, 14F, FontStyle.Bold);
        _startButton.Margin = new Padding(4, 4, 4, 6);
        _startButton.Click += StartButton_Click;
        layout.Controls.Add(_startButton, 0, 0);

        ConfigureButton(_cancelButton, "取消任务", Color.FromArgb(132, 59, 59), 36);
        _cancelButton.Enabled = false;
        _cancelButton.Click += CancelButton_Click;
        layout.Controls.Add(_cancelButton, 0, 1);

        group.Controls.Add(layout);
        return group;
    }

    private Control BuildExcelPreviewGroup()
    {
        var group = CreateGroup("Excel导入预览");
        group.Height = 260;
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(8, 18, 8, 8)
        };
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute, 38));

        ConfigureButton(_importExcelButton, "导入 Excel", Color.FromArgb(65, 122, 85), 32);
        _importExcelButton.Click += ImportExcelButton_Click;
        layout.Controls.Add(_importExcelButton, 0, 0);

        ConfigureList(_excelPreviewList);
        _excelPreviewList.MultiSelect = true;
        _excelPreviewList.Columns.Add("地点", 135);
        _excelPreviewList.Columns.Add("UID", 95);
        _excelPreviewList.Columns.Add("地址/来源", 160);
        layout.Controls.Add(_excelPreviewList, 0, 1);

        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, Margin = new Padding(0, 4, 0, 0) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 34));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33));
        ConfigureButton(_addExcelSelectedButton, "添加选中", Color.FromArgb(28, 92, 187), 32);
        ConfigureButton(_addExcelAllButton, "全部添加", Color.FromArgb(8, 104, 72), 32);
        ConfigureButton(_clearExcelPreviewButton, "清空预览", Color.FromArgb(129, 77, 45), 32);
        _addExcelSelectedButton.Enabled = false;
        _addExcelAllButton.Enabled = false;
        _clearExcelPreviewButton.Enabled = false;
        _addExcelSelectedButton.Click += AddExcelSelectedButton_Click;
        _addExcelAllButton.Click += AddExcelAllButton_Click;
        _clearExcelPreviewButton.Click += ClearExcelPreviewButton_Click;
        buttons.Controls.Add(_addExcelSelectedButton, 0, 0);
        buttons.Controls.Add(_addExcelAllButton, 1, 0);
        buttons.Controls.Add(_clearExcelPreviewButton, 2, 0);
        layout.Controls.Add(buttons, 0, 2);

        group.Controls.Add(layout);
        return group;
    }

    private Control BuildWorkPanel()
    {
        _topSplit.SplitterWidth = 5;

        var topArea = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Padding = new Padding(2, 0, 2, 0)
        };
        topArea.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        topArea.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        topArea.Controls.Add(BuildListsPanel(), 0, 0);
        topArea.Controls.Add(BuildProgressRow(), 0, 1);

        _resultsLogSplit.SplitterWidth = 5;
        _resultsLogSplit.Panel1.Controls.Add(BuildResultPanel());
        _resultsLogSplit.Panel2.Controls.Add(BuildLogPanel());

        _topSplit.Panel1.Controls.Add(topArea);
        _topSplit.Panel2.Controls.Add(_resultsLogSplit);
        return _topSplit;
    }

    private Control BuildListsPanel()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Padding = new Padding(0, 2, 0, 2) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 42));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 58));

        var suggestionsGroup = new GroupBox { Text = "搜索候选", Dock = DockStyle.Fill, Padding = new Padding(8, 20, 8, 8) };
        _suggestionsListBox.Dock = DockStyle.Fill;
        _suggestionsListBox.HorizontalScrollbar = true;
        _suggestionsListBox.IntegralHeight = false;
        _suggestionsListBox.DoubleClick += AddButton_Click;
        suggestionsGroup.Controls.Add(_suggestionsListBox);
        panel.Controls.Add(suggestionsGroup, 0, 0);

        var placesGroup = new GroupBox { Text = "待提取地点", Dock = DockStyle.Fill, Padding = new Padding(8, 20, 8, 8) };
        var placesLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        placesLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        placesLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 34));

        ConfigureList(_placesList);
        _placesList.Columns.Add("地点", 180);
        _placesList.Columns.Add("UID", 140);
        _placesList.Columns.Add("地址/查询词", 220);
        placesLayout.Controls.Add(_placesList, 0, 0);

        ConfigureButton(_removeButton, "移除选中地点", Color.FromArgb(129, 77, 45), 30);
        _removeButton.Click += RemoveButton_Click;
        placesLayout.Controls.Add(_removeButton, 0, 1);

        placesGroup.Controls.Add(placesLayout);
        panel.Controls.Add(placesGroup, 1, 0);
        return panel;
    }

    private Control BuildProgressRow()
    {
        var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 1, Margin = new Padding(0, 3, 0, 3) };
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 96));
        panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        panel.Controls.Add(new Label
        {
            Text = "进度：",
            Dock = DockStyle.Fill,
            AutoSize = false,
            TextAlign = ContentAlignment.MiddleLeft,
            Padding = new Padding(6, 0, 0, 0)
        }, 0, 0);
        _progressBar.Dock = DockStyle.Fill;
        _progressBar.Margin = new Padding(2, 7, 2, 7);
        panel.Controls.Add(_progressBar, 1, 0);
        return panel;
    }

    private Control BuildResultPanel()
    {
        var group = new GroupBox { Text = "运行结果", Dock = DockStyle.Fill, Padding = new Padding(8, 20, 8, 8) };
        var outer = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2 };
        outer.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        outer.RowStyles.Add(new RowStyle(SizeType.Absolute, 94));

        ConfigureList(_resultList);
        _resultList.MultiSelect = false;
        _resultList.Columns.Add("地点", 150);
        _resultList.Columns.Add("状态", 70);
        _resultList.Columns.Add("点数", 60);
        _resultList.Columns.Add("UID", 130);
        _resultList.Columns.Add("信息", 260);
        _resultList.SelectedIndexChanged += ResultList_SelectedIndexChanged;
        outer.Controls.Add(_resultList, 0, 0);

        var buttons = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 3, RowCount = 2, Margin = new Padding(0, 4, 0, 0) };
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33F));
        buttons.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34F));
        buttons.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));
        buttons.RowStyles.Add(new RowStyle(SizeType.Absolute, 42));

        ConfigureButton(_exportAoiButton, "导出AOI范围", Color.FromArgb(28, 92, 187), 42);
        _exportAoiButton.Enabled = false;
        _exportAoiButton.Click += ExportAoiButton_Click;
        buttons.Controls.Add(_exportAoiButton, 0, 0);

        ConfigureButton(_exportPoiButton, "导出周围POI点位", Color.FromArgb(8, 104, 72), 42);
        _exportPoiButton.Enabled = false;
        _exportPoiButton.Click += ExportPoiButton_Click;
        buttons.Controls.Add(_exportPoiButton, 1, 0);

        ConfigureButton(_exportMainPoiButton, "导出该点POI", Color.FromArgb(187, 112, 38), 42);
        _exportMainPoiButton.Enabled = false;
        _exportMainPoiButton.Click += ExportMainPoiButton_Click;
        buttons.Controls.Add(_exportMainPoiButton, 2, 0);

        var openGeoButton = new Button();
        ConfigureButton(openGeoButton, "打开 GeoJSON", Color.FromArgb(65, 122, 85), 42);
        openGeoButton.Click += OpenGeoJsonButton_Click;
        buttons.Controls.Add(openGeoButton, 0, 1);

        var clearMapButton = new Button();
        ConfigureButton(clearMapButton, "清空地图", Color.FromArgb(129, 77, 45), 42);
        clearMapButton.Click += (_, _) => ShowMapPlaceholder();
        buttons.Controls.Add(clearMapButton, 1, 1);

        outer.Controls.Add(buttons, 0, 1);
        group.Controls.Add(outer);
        return group;
    }

    private Control BuildLogPanel()
    {
        var group = new GroupBox { Text = "日志", Dock = DockStyle.Fill, Padding = new Padding(8, 20, 8, 8) };
        _logTextBox.Multiline = true;
        _logTextBox.ReadOnly = true;
        _logTextBox.ScrollBars = ScrollBars.Vertical;
        _logTextBox.Dock = DockStyle.Fill;
        _logTextBox.BackColor = Color.FromArgb(24, 28, 34);
        _logTextBox.ForeColor = Color.FromArgb(232, 238, 244);
        _logTextBox.Font = new Font("Consolas", 9F);
        group.Controls.Add(_logTextBox);
        return group;
    }

    private Control BuildMapPanel()
    {
        var group = new GroupBox { Text = "地图预览", Dock = DockStyle.Fill, Padding = new Padding(8, 20, 8, 8) };

        _mapPlaceholder.Text = "在运行结果中选中成功记录后，轮廓会显示在这里。\n也可以点击“打开 GeoJSON”加载已有文件。";
        _mapPlaceholder.Dock = DockStyle.Fill;
        _mapPlaceholder.TextAlign = ContentAlignment.MiddleCenter;
        _mapPlaceholder.ForeColor = Color.FromArgb(120, 132, 145);
        _mapPlaceholder.Font = new Font("Microsoft YaHei UI", 11F);
        _mapPlaceholder.BackColor = Color.FromArgb(240, 242, 245);

        _mapBrowser.Dock = DockStyle.Fill;
        _mapBrowser.AllowWebBrowserDrop = false;
        _mapBrowser.IsWebBrowserContextMenuEnabled = false;
        _mapBrowser.WebBrowserShortcutsEnabled = false;
        _mapBrowser.ScriptErrorsSuppressed = true;
        _mapBrowser.Visible = false;

        group.Controls.Add(_mapPlaceholder);
        group.Controls.Add(_mapBrowser);
        return group;
    }

    private static GroupBox CreateGroup(string title)
    {
        return new GroupBox
        {
            Text = title,
            Dock = DockStyle.Top,
            Height = 150,
            Margin = new Padding(0, 0, 0, 8),
            Padding = new Padding(0)
        };
    }

    private static TableLayoutPanel CreateGroupLayout(int rows)
    {
        return new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = rows,
            Padding = new Padding(12, 18, 12, 8)
        };
    }

    private static void ConfigureList(ListView list)
    {
        list.Dock = DockStyle.Fill;
        list.View = View.Details;
        list.FullRowSelect = true;
        list.GridLines = true;
        list.HideSelection = false;
        list.Margin = new Padding(0);
    }

    private static Label MakeLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        TextAlign = ContentAlignment.MiddleLeft,
        Margin = new Padding(0, 2, 8, 2)
    };

    private static Label MakeMutedLabel(string text) => new()
    {
        Text = text,
        Dock = DockStyle.Fill,
        AutoEllipsis = true,
        TextAlign = ContentAlignment.MiddleLeft,
        ForeColor = Color.FromArgb(102, 112, 124),
        Margin = new Padding(0, 2, 0, 0)
    };

    private static void ConfigureButton(Button button, string text, Color color, int height)
    {
        button.Text = text;
        button.Dock = DockStyle.Fill;
        button.Height = height;
        button.MinimumSize = new Size(0, height);
        button.BackColor = color;
        button.ForeColor = Color.White;
        button.FlatStyle = FlatStyle.Flat;
        button.FlatAppearance.BorderSize = 0;
        button.Margin = new Padding(4, 3, 4, 3);
        button.AutoEllipsis = true;
        button.UseVisualStyleBackColor = false;
    }

    private static void ConfigureCheckBox(CheckBox box, string text, bool isChecked = false)
    {
        box.Text = text;
        box.Checked = isChecked;
        box.Dock = DockStyle.Fill;
        box.AutoEllipsis = true;
        box.AutoSize = false;
        box.TextAlign = ContentAlignment.MiddleLeft;
        box.Margin = new Padding(0, 3, 0, 3);
    }

    private void AdjustListColumns()
    {
        AdjustLastColumn(_excelPreviewList, 2, 140);
        AdjustLastColumn(_placesList, 2, 180);
        AdjustLastColumn(_resultList, 4, 220);
    }

    private static void AdjustLastColumn(ListView list, int columnIndex, int minWidth)
    {
        if (list.Columns.Count <= columnIndex || list.ClientSize.Width <= 0)
        {
            return;
        }

        var used = 0;
        for (var i = 0; i < list.Columns.Count; i++)
        {
            if (i != columnIndex)
            {
                used += list.Columns[i].Width;
            }
        }

        list.Columns[columnIndex].Width = Math.Max(minWidth, list.ClientSize.Width - used - 24);
    }

    private async void SearchButton_Click(object? sender, EventArgs e)
    {
        var query = _searchTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(query))
        {
            MessageBox.Show(this, "请输入地点名或地址。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        _searchButton.Enabled = false;
        _searchButton.Text = "搜索中...";
        _suggestionsListBox.Items.Clear();

        try
        {
            AppendLog($"搜索候选：{query}");
            var suggestions = await _suggestionService.SearchAsync(query, CancellationToken.None);
            foreach (var suggestion in suggestions)
            {
                _suggestionsListBox.Items.Add(suggestion);
            }

            AppendLog($"找到 {suggestions.Count} 个候选。");
            if (suggestions.Count == 0)
            {
                MessageBox.Show(this, "没有找到候选。可以直接点击“添加输入/选中”使用当前输入。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            }
        }
        catch (Exception ex)
        {
            AppendLog($"搜索失败：{ex.Message}");
            MessageBox.Show(this, ex.Message, "搜索失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
        finally
        {
            _searchButton.Text = "搜索候选";
            _searchButton.Enabled = _cancellationTokenSource is null;
        }
    }

    private void AddButton_Click(object? sender, EventArgs e)
    {
        if (_suggestionsListBox.SelectedItem is PlaceSuggestion suggestion)
        {
            AddPlace(new PlaceInput(suggestion.Name, suggestion.Uid, suggestion.Name, suggestion.Address));
            return;
        }

        var query = _searchTextBox.Text.Trim();
        if (!string.IsNullOrWhiteSpace(query))
        {
            AddPlace(new PlaceInput(query));
        }
    }

    private void AddPlace(PlaceInput place)
    {
        if (_places.Any(x => string.Equals(x.Query, place.Query, StringComparison.OrdinalIgnoreCase) && x.Uid == place.Uid))
        {
            return;
        }

        _places.Add(place);
        RefreshPlacesList();
    }

    private void RefreshPlacesList()
    {
        _placesList.Items.Clear();
        foreach (var place in _places)
        {
            var item = new ListViewItem(place.Label);
            item.SubItems.Add(place.Uid ?? string.Empty);
            item.SubItems.Add(place.Address ?? place.Query);
            _placesList.Items.Add(item);
        }

        AdjustListColumns();
    }

    private void RemoveButton_Click(object? sender, EventArgs e)
    {
        foreach (var index in _placesList.SelectedIndices.Cast<int>().OrderDescending())
        {
            _places.RemoveAt(index);
        }

        RefreshPlacesList();
    }

    private void ImportExcelButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "选择包含地点或地址的 Excel",
            Filter = "Excel 文件|*.xlsx;*.xlsm;*.xltx;*.xltm|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var imported = Exporters.ReadPlacesFromExcel(dialog.FileName);
            _excelPreviewPlaces.Clear();
            _excelPreviewPlaces.AddRange(imported);
            RefreshExcelPreviewList();
            AppendLog($"从 Excel 读取 {imported.Count} 条，已放入预览。");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导入失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void AddExcelSelectedButton_Click(object? sender, EventArgs e)
    {
        var indices = _excelPreviewList.SelectedIndices.Cast<int>().OrderDescending().ToList();
        if (indices.Count == 0)
        {
            return;
        }

        foreach (var index in indices.Order())
        {
            AddPlace(_excelPreviewPlaces[index]);
        }

        foreach (var index in indices)
        {
            _excelPreviewPlaces.RemoveAt(index);
        }

        RefreshExcelPreviewList();
        AppendLog($"已添加 Excel 预览选中 {indices.Count} 条。");
    }

    private void AddExcelAllButton_Click(object? sender, EventArgs e)
    {
        var count = _excelPreviewPlaces.Count;
        foreach (var place in _excelPreviewPlaces)
        {
            AddPlace(place);
        }

        _excelPreviewPlaces.Clear();
        RefreshExcelPreviewList();
        AppendLog($"已添加 Excel 预览全部 {count} 条。");
    }

    private void ClearExcelPreviewButton_Click(object? sender, EventArgs e)
    {
        _excelPreviewPlaces.Clear();
        RefreshExcelPreviewList();
    }

    private void RefreshExcelPreviewList()
    {
        _excelPreviewList.Items.Clear();
        foreach (var place in _excelPreviewPlaces)
        {
            var item = new ListViewItem(place.Label);
            item.SubItems.Add(place.Uid ?? string.Empty);
            item.SubItems.Add(place.Address ?? place.Query);
            _excelPreviewList.Items.Add(item);
        }

        var hasPreview = _excelPreviewPlaces.Count > 0;
        var running = _cancellationTokenSource is not null;
        _addExcelSelectedButton.Enabled = hasPreview && !running;
        _addExcelAllButton.Enabled = hasPreview && !running;
        _clearExcelPreviewButton.Enabled = hasPreview && !running;
    }

    private void BrowseButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "选择 AOI 导出目录",
            SelectedPath = Directory.Exists(_outputTextBox.Text)
                ? _outputTextBox.Text
                : Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)
        };

        if (dialog.ShowDialog(this) == DialogResult.OK)
        {
            _outputTextBox.Text = dialog.SelectedPath;
        }
    }

    private async void StartButton_Click(object? sender, EventArgs e)
    {
        if (_places.Count == 0)
        {
            var query = _searchTextBox.Text.Trim();
            if (!string.IsNullOrWhiteSpace(query))
            {
                AddPlace(new PlaceInput(query));
            }
        }

        if (_places.Count == 0)
        {
            MessageBox.Show(this, "请先添加至少一个地点。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        if (_minDelayBox.Value > _maxDelayBox.Value)
        {
            MessageBox.Show(this, "最小延迟不能大于最大延迟。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        var outputDir = _outputTextBox.Text.Trim();
        if (string.IsNullOrWhiteSpace(outputDir))
        {
            MessageBox.Show(this, "请选择输出目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        SetRunning(true);
        _results.Clear();
        _resultList.Items.Clear();
        _logTextBox.Clear();
        _progressBar.Minimum = 0;
        _progressBar.Maximum = _places.Count;
        _progressBar.Value = 0;
        _cancellationTokenSource = new CancellationTokenSource();

        var settings = new CrawlSettings(
            Headless: !_showBrowserBox.Checked,
            Debug: _debugBox.Checked,
            MinDelayMs: (int)_minDelayBox.Value,
            MaxDelayMs: (int)_maxDelayBox.Value);

        try
        {
            Directory.CreateDirectory(outputDir);
            await RunBatchAsync(_places.ToList(), outputDir, settings, (int)_threadsBox.Value, _cancellationTokenSource.Token);

            if (_excelBox.Checked && _results.Count > 0)
            {
                Exporters.SaveBatchExcel(_results, outputDir);
                AppendLog("已输出 Excel 汇总。");
            }

            AppendLog("全部任务结束。");
        }
        catch (OperationCanceledException)
        {
            AppendLog("任务已取消。");
        }
        finally
        {
            _cancellationTokenSource.Dispose();
            _cancellationTokenSource = null;
            SetRunning(false);
        }
    }

    private async Task RunBatchAsync(IReadOnlyList<PlaceInput> places, string outputDir, CrawlSettings settings, int degreeOfParallelism, CancellationToken cancellationToken)
    {
        using var semaphore = new SemaphoreSlim(degreeOfParallelism);
        var resultLock = new object();

        await Task.WhenAll(places.Select(async place =>
        {
            await semaphore.WaitAsync(cancellationToken);
            try
            {
                var resolvedPlace = await ResolvePlaceForCrawlAsync(place, cancellationToken);
                var result = await new BaiduAoiCrawler(AppendLog).CrawlAsync(resolvedPlace, settings, cancellationToken);
                lock (resultLock)
                {
                    _results.Add(result);
                }

                if (result.Success)
                {
                    try
                    {
                        Exporters.SaveAll(result, outputDir, _excelBox.Checked, _geoJsonBox.Checked);
                        AddResult(result, "成功", result.Message);
                        AppendLog($"[{result.PlaceName}] 已导出。");
                    }
                    catch (Exception ex)
                    {
                        AddResult(CrawlResult.Fail(result.PlaceName, result.Uid, ex.Message), "导出失败", ex.Message);
                    }
                }
                else
                {
                    AddResult(result, "失败", result.Message);
                }
            }
            catch (OperationCanceledException)
            {
                AddResult(CrawlResult.Fail(place.Label, place.Uid, "已取消。"), "已取消", string.Empty);
            }
            catch (Exception ex)
            {
                AddResult(CrawlResult.Fail(place.Label, place.Uid, ex.Message), "失败", ex.Message);
            }
            finally
            {
                semaphore.Release();
                IncrementProgress();
            }
        }));
    }

    private async Task<PlaceInput> ResolvePlaceForCrawlAsync(PlaceInput place, CancellationToken cancellationToken)
    {
        if (!_autoFirstCandidateBox.Checked || !string.IsNullOrWhiteSpace(place.Uid))
        {
            return place;
        }

        try
        {
            AppendLog($"[{place.Label}] 正在搜索候选，自动选择第一条...");
            var suggestions = await _suggestionService.SearchAsync(place.Query, cancellationToken);
            var first = suggestions.FirstOrDefault(s => !string.IsNullOrWhiteSpace(s.Uid)) ?? suggestions.FirstOrDefault();
            if (first is null)
            {
                AppendLog($"[{place.Label}] 未找到候选，使用原始名称提取。");
                return place;
            }

            AppendLog($"[{place.Label}] 已自动选择候选: {first.Name}");
            return new PlaceInput(first.Name, first.Uid, first.Name, first.Address);
        }
        catch (Exception ex)
        {
            AppendLog($"[{place.Label}] 自动候选失败，使用原始名称: {ex.Message}");
            return place;
        }
    }

    private void CancelButton_Click(object? sender, EventArgs e)
    {
        _cancelButton.Enabled = false;
        _cancellationTokenSource?.Cancel();
        AppendLog("正在取消任务...");
    }

    private void SetRunning(bool running)
    {
        _startButton.Enabled = !running;
        _cancelButton.Enabled = running;
        _searchButton.Enabled = !running;
        _addButton.Enabled = !running;
        _removeButton.Enabled = !running;
        _importExcelButton.Enabled = !running;
        _browseButton.Enabled = !running;
        _addExcelSelectedButton.Enabled = !running && _excelPreviewPlaces.Count > 0;
        _addExcelAllButton.Enabled = !running && _excelPreviewPlaces.Count > 0;
        _clearExcelPreviewButton.Enabled = !running && _excelPreviewPlaces.Count > 0;
        _searchTextBox.ReadOnly = running;
        _outputTextBox.ReadOnly = running;
        _threadsBox.Enabled = !running;
        _minDelayBox.Enabled = !running;
        _maxDelayBox.Enabled = !running;
        _showBrowserBox.Enabled = !running;
        _debugBox.Enabled = !running;
        _excelBox.Enabled = !running;
        _geoJsonBox.Enabled = !running;
        _autoFirstCandidateBox.Enabled = !running;
        UpdateExportButtons();
    }

    private void AddResult(CrawlResult result, string status, string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AddResult(result, status, message));
            return;
        }

        var item = new ListViewItem(result.PlaceName) { Tag = result };
        item.SubItems.Add(status);
        item.SubItems.Add(result.Points.Count.ToString(CultureInfo.InvariantCulture));
        item.SubItems.Add(result.Uid ?? string.Empty);
        var poiText = result.PoiPoints.Count > 0 ? $" | POI {result.PoiPoints.Count}" : string.Empty;
        item.SubItems.Add(message + poiText);
        item.ForeColor = status == "成功" ? Color.FromArgb(18, 117, 58) : Color.FromArgb(176, 50, 50);
        _resultList.Items.Add(item);
        AdjustListColumns();
    }

    private void AppendLog(string message)
    {
        if (InvokeRequired)
        {
            BeginInvoke(() => AppendLog(message));
            return;
        }

        _logTextBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
    }

    private void IncrementProgress()
    {
        if (InvokeRequired)
        {
            BeginInvoke(IncrementProgress);
            return;
        }

        if (_progressBar.Value < _progressBar.Maximum)
        {
            _progressBar.Value++;
        }
    }

    private void ResultList_SelectedIndexChanged(object? sender, EventArgs e)
    {
        if (_resultList.SelectedItems.Count == 0)
        {
            ShowMapPlaceholder();
            UpdateExportButtons();
            return;
        }

        if (_resultList.SelectedItems[0].Tag is not CrawlResult result || !result.Success || result.Points.Count < 3)
        {
            ShowMapPlaceholder();
            UpdateExportButtons();
            return;
        }

        UpdateMapDisplay(result.PlaceName, result.Points);
        UpdateExportButtons();
    }

    private void ExportAoiButton_Click(object? sender, EventArgs e)
    {
        ExportSelectedResult(exportAoi: true, exportPoi: false);
    }

    private void ExportPoiButton_Click(object? sender, EventArgs e)
    {
        ExportSelectedResult(exportAoi: false, exportPoi: true);
    }

    private void ExportMainPoiButton_Click(object? sender, EventArgs e)
    {
        if (SelectedResult() is not { } result)
        {
            return;
        }

        try
        {
            var outputDir = _outputTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(outputDir))
            {
                MessageBox.Show(this, "请选择输出目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var path = Exporters.SaveMainPoiCsv(result, outputDir);
            AppendLog($"[{result.PlaceName}] 已导出该点POI: {path}");
            MessageBox.Show(this, $"已导出：{Environment.NewLine}{path}", "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private void ExportSelectedResult(bool exportAoi, bool exportPoi)
    {
        if (SelectedResult() is not { } result)
        {
            return;
        }

        try
        {
            var outputDir = _outputTextBox.Text.Trim();
            if (string.IsNullOrWhiteSpace(outputDir))
            {
                MessageBox.Show(this, "请选择输出目录。", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var folder = Exporters.SaveSelectedFiles(result, outputDir, exportAoi, exportPoi);
            AppendLog($"[{result.PlaceName}] 已导出到 {folder}");
            MessageBox.Show(this, $"已导出到：{Environment.NewLine}{folder}", "导出完成", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "导出失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private CrawlResult? SelectedResult()
    {
        return _resultList.SelectedItems.Count == 0 ? null : _resultList.SelectedItems[0].Tag as CrawlResult;
    }

    private void UpdateExportButtons()
    {
        var running = _cancellationTokenSource is not null;
        var result = SelectedResult();
        _exportAoiButton.Enabled = !running && result is { Success: true } && result.Points.Count >= 3;
        _exportPoiButton.Enabled = !running && result is { Success: true } && result.PoiPoints.Count > 0;
        _exportMainPoiButton.Enabled = !running && result is { Success: true } && result.Points.Count > 0;
    }

    private void OpenGeoJsonButton_Click(object? sender, EventArgs e)
    {
        using var dialog = new OpenFileDialog
        {
            Title = "打开 GeoJSON 轮廓文件",
            Filter = "GeoJSON 文件|*.geojson;*.json|所有文件|*.*"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        try
        {
            var text = File.ReadAllText(dialog.FileName, Encoding.UTF8);
            using var document = JsonDocument.Parse(text);
            var root = document.RootElement;
            double[][]? coords = null;

            if (root.TryGetProperty("type", out var type))
            {
                var typeName = type.GetString();
                if (typeName == "FeatureCollection")
                {
                    var features = root.GetProperty("features");
                    if (features.GetArrayLength() > 0)
                    {
                        coords = ExtractCoords(features[0].GetProperty("geometry"));
                    }
                }
                else if (typeName == "Feature")
                {
                    coords = ExtractCoords(root.GetProperty("geometry"));
                }
                else if (typeName == "Polygon")
                {
                    coords = ExtractCoords(root);
                }
            }

            if (coords is null || coords.Length < 3)
            {
                MessageBox.Show(this, "文件中没有有效的多边形轮廓数据。", "解析失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var name = Path.GetFileNameWithoutExtension(dialog.FileName).Replace("_轮廓", string.Empty, StringComparison.Ordinal);
            var points = coords.Select(c => new AoiPoint(c[0], c[1])).ToList();
            UpdateMapDisplay(name, points);
            AppendLog($"已加载 GeoJSON: {dialog.FileName} ({points.Count} 个点)");
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, $"解析失败：{Environment.NewLine}{ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
    }

    private static double[][]? ExtractCoords(JsonElement geometry)
    {
        if (geometry.TryGetProperty("type", out var type) &&
            type.GetString() == "Polygon" &&
            geometry.TryGetProperty("coordinates", out var coordinates) &&
            coordinates.GetArrayLength() > 0)
        {
            var ring = coordinates[0];
            var result = new double[ring.GetArrayLength()][];
            for (var i = 0; i < ring.GetArrayLength(); i++)
            {
                result[i] = [ring[i][0].GetDouble(), ring[i][1].GetDouble()];
            }

            return result;
        }

        return null;
    }

    private void ShowMapPlaceholder()
    {
        _mapBrowser.Visible = false;
        _mapPlaceholder.Visible = true;
    }

    private void UpdateMapDisplay(string placeName, IReadOnlyList<AoiPoint> points)
    {
        if (points.Count < 3)
        {
            ShowMapPlaceholder();
            return;
        }

        var html = BuildMapHtml(placeName, points);
        if (!string.IsNullOrEmpty(_mapHtmlPath))
        {
            try
            {
                File.Delete(_mapHtmlPath);
            }
            catch
            {
                // Best effort cleanup for the previous preview file.
            }
        }

        _mapHtmlPath = Path.Combine(Path.GetTempPath(), $"{Guid.NewGuid():N}.html");
        File.WriteAllText(_mapHtmlPath, html, Encoding.UTF8);
        _mapPlaceholder.Visible = false;
        _mapBrowser.Visible = true;
        _mapBrowser.Navigate(_mapHtmlPath);
    }

    private static string BuildMapHtml(string name, IReadOnlyList<AoiPoint> points)
    {
        var centerLng = points.Average(p => p.X);
        var centerLat = points.Average(p => p.Y);
        var coordinates = new StringBuilder();
        foreach (var point in points)
        {
            coordinates.Append(CultureInfo.InvariantCulture, $"[{point.Y:F6},{point.X:F6}],");
        }

        coordinates.Append(CultureInfo.InvariantCulture, $"[{points[0].Y:F6},{points[0].X:F6}]");
        var encodedName = JsonSerializer.Serialize(name);

        var centerLatText = centerLat.ToString("F6", CultureInfo.InvariantCulture);
        var centerLngText = centerLng.ToString("F6", CultureInfo.InvariantCulture);
        return string.Format(
            CultureInfo.InvariantCulture,
            """
<!DOCTYPE html>
<html>
<head>
  <meta charset="utf-8" />
  <meta http-equiv="X-UA-Compatible" content="IE=edge" />
  <link rel="stylesheet" href="https://unpkg.com/leaflet@1.9.4/dist/leaflet.css" />
  <script src="https://unpkg.com/leaflet@1.9.4/dist/leaflet.js"></script>
  <style>
    html, body, #map {{ width: 100%; height: 100%; margin: 0; padding: 0; }}
    .info {{ padding: 6px 10px; background: white; border-radius: 4px; box-shadow: 0 0 8px rgba(0,0,0,.18); font: 13px/1.5 "Microsoft YaHei", sans-serif; }}
  </style>
</head>
<body>
  <div id="map"></div>
  <script>
    const placeName = {0};
    const map = L.map('map', {{ center: [{1}, {2}], zoom: 15, zoomControl: true }});
    L.tileLayer('https://{{s}}.tile.openstreetmap.org/{{z}}/{{x}}/{{y}}.png', {{ attribution: '&copy; OSM', maxZoom: 19 }}).addTo(map);
    const polygon = L.polygon([{3}], {{ color: '#e64a19', weight: 3, opacity: .9, fillColor: '#ff7043', fillOpacity: .35 }}).addTo(map);
    map.fitBounds(polygon.getBounds(), {{ padding: [20, 20] }});
    L.control({{ position: 'topright' }}).onAdd = function() {{
      const div = L.DomUtil.create('div', 'info');
      div.innerHTML = '<b>' + placeName + '</b><br />{4} 个坐标点';
      return div;
    }}.addTo(map);
  </script>
</body>
</html>
""",
            encodedName,
            centerLatText,
            centerLngText,
            coordinates,
            points.Count);
    }
}
