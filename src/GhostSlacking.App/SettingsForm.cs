using GhostSlacking.Core;
using AntButton = AntdUI.Button;
using AntInput = AntdUI.Input;
using AntInputNumber = AntdUI.InputNumber;
using AntPanel = AntdUI.Panel;
using AntSelect = AntdUI.Select;
using AntSwitch = AntdUI.Switch;

namespace GhostSlacking.App;

internal sealed class SettingsForm : Form
{
    private const int ExpandedSidebarWidth = 168;
    private const int CollapsedSidebarWidth = 64;
    private static readonly Color Sidebar = Color.White;
    private static readonly Color SidebarSelected = Color.FromArgb(235, 248, 246);
    private static readonly Color Accent = Color.FromArgb(28, 178, 165);
    private static readonly Color Canvas = Color.White;
    private static readonly Color Card = Color.White;
    private static readonly Color Surface = Color.FromArgb(247, 249, 252);
    private static readonly Color Border = Color.FromArgb(225, 230, 237);
    private static readonly Color PrimaryText = Color.FromArgb(28, 36, 49);
    private static readonly Color SecondaryText = Color.FromArgb(103, 113, 128);

    private readonly UiLanguage _initialLanguage;
    private readonly AntInputNumber _diameter;
    private readonly AntSelect _shape;
    private readonly AntInput _peekKeyDisplay;
    private readonly AntButton _capturePeekKey;
    private readonly HotkeyEditor _pickHotkey;
    private readonly HotkeyEditor _windowToggleHotkey;
    private readonly HotkeyEditor _restoreHotkey;
    private readonly HotkeyEditor _restoreAllHotkey;
    private readonly HotkeyEditor _settingsHotkey;
    private readonly HotkeyEditor _exitHotkey;
    private readonly AntSelect _peekMode;
    private readonly AntSwitch _restoreOnExit;
    private readonly AntSwitch _startWithWindows;
    private readonly AntSelect _logLevel;
    private readonly AntSelect _language;
    private readonly Label _pageTitle;
    private readonly Label _pageDescription;
    private readonly Panel _pageHost;
    private readonly List<NavButton> _navigation = [];
    private TableLayoutPanel? _rootLayout;
    private TableLayoutPanel? _sidebarPanel;
    private Panel? _brandPanel;
    private Label? _brandName;
    private PictureBox? _brandIcon;
    private AntButton? _collapseButton;
    private ToolTip? _toolTip;
    private bool _sidebarCollapsed;
    private int _peekVirtualKey;
    private bool _capturingPeekKey;
    private HotkeyEditor? _activeHotkeyEditor;

    public SettingsForm(AppSettings settings)
    {
        _initialLanguage = settings.Language;
        AntdUI.Config.TextRenderingHighQuality = true;
        Text = UiText.Text(settings.Language, "title");
        Font = new Font("Microsoft YaHei UI", 9.5F);
        Icon = AppIcon.Instance;
        BackColor = Canvas;
        ForeColor = PrimaryText;
        FormBorderStyle = FormBorderStyle.Sizable;
        MaximizeBox = false;
        MinimizeBox = false;
        KeyPreview = true;
        StartPosition = FormStartPosition.CenterScreen;
        AutoScaleMode = AutoScaleMode.Dpi;
        ClientSize = new Size(940, 680);
        MinimumSize = new Size(800, 620);

        _diameter = CreateNumericInput(settings.RevealDiameterPx);
        _shape = CreateComboBox();
        _shape.Items.AddRange([
            new ShapeChoice(RevealShape.Circle, UiText.Text(settings.Language, "circle")),
            new ShapeChoice(RevealShape.Rectangle, UiText.Text(settings.Language, "rectangle")),
            new ShapeChoice(RevealShape.RoundedRectangle, UiText.Text(settings.Language, "roundedRectangle"))]);
        FitSelectWidth(_shape);
        SelectShape(settings.RevealShape);
        _peekMode = CreateComboBox();
        _peekMode.Items.AddRange([
            new TriggerChoice(PeekTrigger.Hold, UiText.Text(settings.Language, "holdPeek")),
            new TriggerChoice(PeekTrigger.Toggle, UiText.Text(settings.Language, "togglePeek"))]);
        FitSelectWidth(_peekMode);
        SelectPeekMode(settings.PeekTrigger);

        _peekVirtualKey = settings.PeekVirtualKey;
        _peekKeyDisplay = CreateKeyDisplay(UiText.PeekKeyName(settings.Language, settings.PeekVirtualKey));
        _capturePeekKey = CreateSecondaryButton(UiText.Text(settings.Language, "changeKey"));
        _capturePeekKey.Click += (_, _) => BeginPeekKeyCapture(CurrentLanguage);
        _pickHotkey = CreateHotkeyEditor(settings.PickHotkey, settings.Language);
        _windowToggleHotkey = CreateHotkeyEditor(settings.WindowToggleHotkey, settings.Language);
        _restoreHotkey = CreateHotkeyEditor(settings.RestoreHotkey, settings.Language);
        _restoreAllHotkey = CreateHotkeyEditor(settings.RestoreAllHotkey, settings.Language);
        _settingsHotkey = CreateHotkeyEditor(settings.SettingsHotkey, settings.Language);
        _exitHotkey = CreateHotkeyEditor(settings.ExitHotkey, settings.Language);

        _restoreOnExit = CreateCheckBox(settings.RestoreOnExit);
        _startWithWindows = CreateCheckBox(settings.StartWithWindows);
        _logLevel = CreateComboBox();
        _logLevel.Items.AddRange(Enum.GetValues<LogLevel>().Cast<object>().ToArray());
        FitSelectWidth(_logLevel);
        SelectValue(_logLevel, settings.MinimumLogLevel);
        _language = CreateComboBox();
        _language.Items.AddRange([new LanguageChoice(UiLanguage.Chinese, "中文"), new LanguageChoice(UiLanguage.English, "English")]);
        FitSelectWidth(_language);
        _language.SelectedIndex = settings.Language == UiLanguage.Chinese ? 0 : 1;

        _pageTitle = new Label { AutoSize = true, Font = new Font(Font.FontFamily, 17F, FontStyle.Bold), ForeColor = PrimaryText };
        _pageDescription = new Label { AutoSize = true, ForeColor = SecondaryText, Margin = new Padding(0, 7, 0, 0) };
        _pageHost = new Panel { Dock = DockStyle.Fill, BackColor = Canvas };

        BuildLayout(settings.Language);
        KeyDown += OnKeyDown;
        FormClosed += (_, _) =>
        {
            _toolTip?.Dispose();
            _brandIcon?.Image?.Dispose();
        };
    }

    public AppSettings GetSettings(AppSettings current)
    {
        var shape = _shape.SelectedValue is ShapeChoice selectedShape ? selectedShape.Shape : current.RevealShape;
        var peekMode = _peekMode.SelectedValue is TriggerChoice selectedTrigger ? selectedTrigger.Trigger : current.PeekTrigger;
        var logLevel = _logLevel.SelectedValue is LogLevel selectedLevel ? selectedLevel : current.MinimumLogLevel;
        var language = _language.SelectedValue is LanguageChoice selectedLanguage ? selectedLanguage.Language : current.Language;
        return current with
        {
            RevealDiameterPx = (int)_diameter.Value,
            RevealShape = shape,
            PeekTrigger = peekMode,
            Language = language,
            PeekVirtualKey = _peekVirtualKey,
            PickHotkey = _pickHotkey.Binding,
            WindowToggleVirtualKey = _windowToggleHotkey.Binding.VirtualKey,
            WindowToggleHotkey = _windowToggleHotkey.Binding,
            RestoreHotkey = _restoreHotkey.Binding,
            RestoreAllHotkey = _restoreAllHotkey.Binding,
            SettingsHotkey = _settingsHotkey.Binding,
            ExitHotkey = _exitHotkey.Binding,
            RestoreOnExit = _restoreOnExit.Checked,
            StartWithWindows = _startWithWindows.Checked,
            MinimumLogLevel = logLevel
        };
    }

    private void BuildLayout(UiLanguage language)
    {
        _rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2,
            Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Canvas
        };
        _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168F));
        _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        Controls.Add(_rootLayout);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2,
            Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Canvas
        };
        content.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        content.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        content.Controls.Add(CreatePageHeader(), 0, 0);
        content.Controls.Add(_pageHost, 0, 1);

        var sidebar = CreateSidebar(language);
        _rootLayout.Controls.Add(sidebar, 0, 0);
        _rootLayout.SetRowSpan(sidebar, 2);
        _rootLayout.Controls.Add(content, 1, 0);
        _rootLayout.Controls.Add(CreateFooter(language), 1, 1);

        var revealPage = CreateRevealPage(language);
        var hotkeysPage = CreateHotkeysPage(language);
        var generalPage = CreateGeneralPage(language);
        _pageHost.Controls.AddRange([revealPage, hotkeysPage, generalPage]);
        ConfigureNavigation(_navigation[0], revealPage, language, "revealSettings", "revealSettingsDescription");
        ConfigureNavigation(_navigation[1], hotkeysPage, language, "hotkeySettings", "hotkeySettingsDescription");
        ConfigureNavigation(_navigation[2], generalPage, language, "generalSettings", "generalSettingsDescription");
        ShowPage(_navigation[0], revealPage, UiText.Text(language, "revealSettings"), UiText.Text(language, "revealSettingsDescription"));
        SetSidebarCollapsed(false, language);
    }

    private Control CreateSidebar(UiLanguage language)
    {
        _sidebarPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 3,
            Margin = Padding.Empty, Padding = new Padding(12, 18, 12, 12), BackColor = Sidebar
        };
        _sidebarPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _sidebarPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 84F));
        _sidebarPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _sidebarPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));

        _brandPanel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Sidebar };
        _brandIcon = new PictureBox
        {
            Image = AppIcon.Instance.ToBitmap(), SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent, Location = Point.Empty, Size = new Size(36, 36), TabStop = false
        };
        _brandName = new Label
        {
            Text = UiText.Text(language, "settings"), AutoSize = false,
            Font = new Font(Font.FontFamily, 11F, FontStyle.Bold), ForeColor = PrimaryText,
            TextAlign = ContentAlignment.MiddleLeft, Location = new Point(46, 0), Size = new Size(90, 36)
        };
        _brandPanel.Controls.AddRange([_brandIcon, _brandName]);

        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Sidebar
        };
        AddNavigationButton(nav, UiText.Text(language, "revealSettings"), NavGlyph.Reveal);
        AddNavigationButton(nav, UiText.Text(language, "hotkeySettings"), NavGlyph.Keyboard);
        AddNavigationButton(nav, UiText.Text(language, "generalSettings"), NavGlyph.General);

        _collapseButton = new AntButton
        {
            Dock = DockStyle.Right, Width = 40, Height = 36, Radius = 6, BorderWidth = 0,
            Text = "‹", Font = new Font(Font.FontFamily, 18F),
            ForeColor = SecondaryText, BackColor = Color.Transparent, BackHover = SidebarSelected,
            Cursor = Cursors.Hand, Margin = Padding.Empty
        };
        _collapseButton.Click += (_, _) => SetSidebarCollapsed(!_sidebarCollapsed, language);
        _toolTip = new ToolTip();
        _toolTip.SetToolTip(_collapseButton, UiText.Text(language, "collapseSidebar"));
        var collapseHost = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Sidebar };
        collapseHost.Controls.Add(_collapseButton);

        _sidebarPanel.Controls.Add(_brandPanel, 0, 0);
        _sidebarPanel.Controls.Add(nav, 0, 1);
        _sidebarPanel.Controls.Add(collapseHost, 0, 2);
        return _sidebarPanel;
    }

    private Control CreatePageHeader()
    {
        var header = new TableLayoutPanel
        {
            Dock = DockStyle.Fill, AutoSize = true, AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1, RowCount = 2, MinimumSize = new Size(0, 112),
            Margin = Padding.Empty, Padding = new Padding(38, 22, 34, 18), BackColor = Canvas
        };
        header.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        header.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        _pageTitle.Margin = Padding.Empty;
        _pageDescription.Margin = new Padding(0, 7, 0, 0);
        header.Controls.Add(_pageTitle, 0, 0);
        header.Controls.Add(_pageDescription, 0, 1);
        header.Resize += (_, _) =>
        {
            var textWidth = Math.Max(240, header.ClientSize.Width - header.Padding.Horizontal);
            _pageTitle.MaximumSize = new Size(textWidth, 0);
            _pageDescription.MaximumSize = new Size(textWidth, 0);
        };
        return header;
    }

    private Control CreateFooter(UiLanguage language)
    {
        var footer = new Panel { Dock = DockStyle.Fill, Padding = new Padding(34, 14, 34, 14), BackColor = Card };
        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Right, AutoSize = true, FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false, Margin = Padding.Empty, Padding = Padding.Empty
        };
        var save = CreatePrimaryButton(UiText.Text(language, "save"));
        save.DialogResult = DialogResult.OK;
        save.Click += (_, _) => ValidateUniqueHotkeys();
        var cancel = CreateSecondaryButton(UiText.Text(language, "cancel"));
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        footer.Controls.Add(buttons);
        AcceptButton = save;
        CancelButton = cancel;
        return footer;
    }

    private void ValidateUniqueHotkeys()
    {
        HotkeyEditor[] editors =
        [
            _pickHotkey,
            _windowToggleHotkey,
            _restoreHotkey,
            _restoreAllHotkey,
            _settingsHotkey,
            _exitHotkey
        ];
        var duplicate = editors
            .Where(editor => !editor.Binding.IsDisabled)
            .GroupBy(editor => editor.Binding)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is null)
        {
            return;
        }

        DialogResult = DialogResult.None;
        MessageBox.Show(
            this,
            string.Format(UiText.Text(CurrentLanguage, "duplicateHotkey"), UiText.ShortcutName(CurrentLanguage, duplicate.Key)),
            UiText.Text(CurrentLanguage, "title"),
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
    }

    private Control CreateRevealPage(UiLanguage language)
    {
        return CreatePage([
            CreateSection(language, "revealAppearance", [
                CreateSettingRow(UiText.Text(language, "diameter"), UiText.Text(language, "diameterDescription"), _diameter),
                CreateSettingRow(UiText.Text(language, "shape"), UiText.Text(language, "shapeDescription"), _shape)]),
            CreateSection(language, "peekBehavior", [
                CreateSettingRow(UiText.Text(language, "peekMode"), UiText.Text(language, "peekModeDescription"), _peekMode),
                CreateSettingRow(UiText.Text(language, "peekKey"), UiText.Text(language, "peekKeyDescription"), CreateKeyEditorPanel(_peekKeyDisplay, _capturePeekKey))])]);
    }

    private Control CreateHotkeysPage(UiLanguage language) => CreatePage([
        CreateSection(language, "windowActions", [
            ShortcutRow(language, "pickHotkey", "pickHotkeyDescription", _pickHotkey),
            ShortcutRow(language, "windowToggleHotkey", "windowToggleHotkeyDescription", _windowToggleHotkey),
            ShortcutRow(language, "restoreHotkey", "restoreHotkeyDescription", _restoreHotkey),
            ShortcutRow(language, "restoreAllHotkey", "restoreAllHotkeyDescription", _restoreAllHotkey)]),
        CreateSection(language, "applicationActions", [
            ShortcutRow(language, "settingsHotkey", "settingsHotkeyDescription", _settingsHotkey),
            ShortcutRow(language, "exitHotkey", "exitHotkeyDescription", _exitHotkey)])]);

    private Control ShortcutRow(UiLanguage language, string titleKey, string descriptionKey, HotkeyEditor editor) =>
        CreateSettingRow(UiText.Text(language, titleKey), UiText.Text(language, descriptionKey), CreateKeyEditorPanel(editor.Display, editor.CaptureButton, editor.ClearButton));

    private Control CreateGeneralPage(UiLanguage language) => CreatePage([
        CreateSection(language, "startupAndSafety", [
            CreateSettingRow(UiText.Text(language, "startWindows"), UiText.Text(language, "startWindowsDescription"), _startWithWindows),
            CreateSettingRow(UiText.Text(language, "restoreOnExit"), UiText.Text(language, "restoreOnExitDescription"), _restoreOnExit)]),
        CreateSection(language, "languageAndDiagnostics", [
            CreateSettingRow(UiText.Text(language, "interfaceLanguage"), UiText.Text(language, "languageDescription"), _language),
            CreateSettingRow(UiText.Text(language, "logLevel"), UiText.Text(language, "logLevelDescription"), _logLevel)])]);

    private static Control CreatePage(Control[] sections)
    {
        var scroll = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill, AutoScroll = true,
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Margin = Padding.Empty, Padding = new Padding(34, 0, 25, 24),
            BackColor = Canvas, Visible = false
        };
        scroll.Controls.AddRange(sections);
        scroll.Resize += (_, _) =>
        {
            var width = Math.Max(478, scroll.ClientSize.Width - scroll.Padding.Horizontal - 2);
            foreach (Control section in scroll.Controls) section.Width = width;
        };
        return scroll;
    }

    private Control CreateSection(UiLanguage language, string titleKey, Control[] rows)
    {
        var section = new TableLayoutPanel
        {
            Width = 560, Height = 29 + (rows.Length * 90),
            ColumnCount = 1, RowCount = 2,
            Margin = new Padding(0, 0, 0, 20), Padding = Padding.Empty, BackColor = Canvas
        };
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 29F));
        section.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        var heading = new Label
        {
            Text = UiText.Text(language, titleKey), AutoSize = true,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold), ForeColor = SecondaryText,
            Margin = new Padding(2, 0, 0, 0), Anchor = AnchorStyles.Left
        };
        var card = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown, WrapContents = false,
            Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Canvas
        };
        foreach (var row in rows) card.Controls.Add(row);
        card.Resize += (_, _) =>
        {
            foreach (Control row in card.Controls) row.Width = Math.Max(478, card.ClientSize.Width);
        };
        section.Controls.Add(heading, 0, 0);
        section.Controls.Add(card, 0, 1);
        return section;
    }

    private Control CreateSettingRow(string title, string description, Control editor)
    {
        var surface = new AntPanel
        {
            Height = 82, Width = 560, Radius = 8, BorderWidth = 0,
            Margin = new Padding(0, 0, 0, 8), Padding = Padding.Empty,
            Back = Surface, BackColor = Surface
        };
        var layout = new TableLayoutPanel
        {
            ColumnCount = 2, RowCount = 1, Dock = DockStyle.Fill,
            Margin = Padding.Empty, Padding = new Padding(18, 10, 18, 10), BackColor = Color.Transparent
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380F));
        var copy = new TableLayoutPanel
        {
            ColumnCount = 1, RowCount = 2, Dock = DockStyle.Fill,
            Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Color.Transparent
        };
        copy.RowStyles.Add(new RowStyle(SizeType.Absolute, 25F));
        copy.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        copy.Controls.Add(new Label
        {
            Text = title, AutoSize = false, Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold), ForeColor = PrimaryText,
            TextAlign = ContentAlignment.MiddleLeft, AutoEllipsis = true, Margin = Padding.Empty
        }, 0, 0);
        copy.Controls.Add(new Label
        {
            Text = description, AutoSize = false, Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 8.5F), ForeColor = SecondaryText,
            TextAlign = ContentAlignment.TopLeft, AutoEllipsis = true, Margin = new Padding(0, 3, 12, 0)
        }, 0, 1);
        editor.Anchor = AnchorStyles.Right;
        layout.Controls.Add(copy, 0, 0);
        layout.Controls.Add(editor, 1, 0);
        surface.Controls.Add(layout);
        return surface;
    }

    private void AddNavigationButton(Control parent, string text, NavGlyph glyph)
    {
        var button = new NavButton
        {
            LabelText = text, Glyph = glyph, AccessibleName = text,
            Width = 144, Height = 44, Margin = new Padding(0, 0, 0, 6),
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold)
        };
        _navigation.Add(button);
        parent.Controls.Add(button);
    }

    private void ConfigureNavigation(NavButton button, Control page, UiLanguage language, string titleKey, string descriptionKey) =>
        button.Click += (_, _) => ShowPage(button, page, UiText.Text(language, titleKey), UiText.Text(language, descriptionKey));

    private void ShowPage(NavButton selected, Control page, string title, string description)
    {
        CancelKeyCapture();
        foreach (Control candidate in _pageHost.Controls) candidate.Visible = ReferenceEquals(candidate, page);
        foreach (var button in _navigation) button.Selected = ReferenceEquals(button, selected);
        page.BringToFront();
        _pageTitle.Text = title;
        _pageDescription.Text = description;
    }

    private void SetSidebarCollapsed(bool collapsed, UiLanguage language)
    {
        if (_rootLayout is null || _sidebarPanel is null || _brandPanel is null ||
            _brandIcon is null || _brandName is null || _collapseButton is null)
        {
            return;
        }

        _sidebarCollapsed = collapsed;
        _rootLayout.ColumnStyles[0].Width = collapsed ? CollapsedSidebarWidth : ExpandedSidebarWidth;
        _sidebarPanel.Padding = collapsed ? new Padding(9, 18, 9, 12) : new Padding(12, 18, 12, 12);
        _brandName.Visible = !collapsed;
        _brandIcon.Location = collapsed ? new Point(5, 0) : Point.Empty;
        foreach (var button in _navigation)
        {
            button.Compact = collapsed;
            button.Width = collapsed ? 46 : 144;
            _toolTip?.SetToolTip(button, collapsed ? button.LabelText : string.Empty);
        }
        _collapseButton.Text = collapsed ? "›" : "‹";
        _toolTip?.SetToolTip(_collapseButton, UiText.Text(language, collapsed ? "expandSidebar" : "collapseSidebar"));
        _sidebarPanel.PerformLayout();
    }

    private void CancelKeyCapture()
    {
        if (_activeHotkeyEditor is not null)
        {
            _activeHotkeyEditor.CancelCapture(CurrentLanguage);
            _activeHotkeyEditor = null;
        }
        if (_capturingPeekKey)
        {
            SetButtonText(_capturePeekKey, UiText.Text(CurrentLanguage, "changeKey"));
            _capturingPeekKey = false;
        }
    }

    private void BeginPeekKeyCapture(UiLanguage language)
    {
        CancelKeyCapture();
        _capturingPeekKey = true;
        SetButtonText(_capturePeekKey, UiText.Text(language, "pressKey"));
        _capturePeekKey.Focus();
    }

    private HotkeyEditor CreateHotkeyEditor(HotkeyBinding binding, UiLanguage language)
    {
        var editor = new HotkeyEditor(binding, language);
        editor.CaptureButton.Click += (_, _) =>
        {
            CancelKeyCapture();
            _activeHotkeyEditor = editor;
            editor.BeginCapture(CurrentLanguage);
        };
        editor.ClearButton.Click += (_, _) =>
        {
            CancelKeyCapture();
            editor.Clear(CurrentLanguage);
        };
        return editor;
    }

    private static Control CreateKeyEditorPanel(params Control[] controls)
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true, FlowDirection = FlowDirection.LeftToRight, WrapContents = false,
            Margin = Padding.Empty, Padding = Padding.Empty, BackColor = Card
        };
        panel.Controls.AddRange(controls);
        return panel;
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (_activeHotkeyEditor is not null)
        {
            if (_activeHotkeyEditor.TryCapture(e, CurrentLanguage))
            {
                _activeHotkeyEditor = null;
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            return;
        }
        if (!_capturingPeekKey || e.KeyValue is < 1 or > 255) return;
        _peekVirtualKey = e.KeyValue;
        SetKeyDisplayText(_peekKeyDisplay, UiText.PeekKeyName(CurrentLanguage, _peekVirtualKey));
        SetButtonText(_capturePeekKey, UiText.Text(CurrentLanguage, "changeKey"));
        _capturingPeekKey = false;
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private UiLanguage CurrentLanguage => _language.SelectedValue is LanguageChoice choice ? choice.Language : _initialLanguage;

    private void SelectPeekMode(PeekTrigger trigger)
    {
        for (var i = 0; i < _peekMode.Items.Count; i++)
        {
            if (_peekMode.Items[i] is TriggerChoice choice && choice.Trigger == trigger)
            {
                _peekMode.SelectedIndex = i;
                return;
            }
        }
        _peekMode.SelectedIndex = 0;
    }

    private void SelectShape(RevealShape shape)
    {
        for (var i = 0; i < _shape.Items.Count; i++)
        {
            if (_shape.Items[i] is ShapeChoice choice && choice.Shape == shape)
            {
                _shape.SelectedIndex = i;
                return;
            }
        }
        _shape.SelectedIndex = 0;
    }

    private static void SelectValue(AntSelect select, object value)
    {
        for (var i = 0; i < select.Items.Count; i++)
        {
            if (Equals(select.Items[i], value))
            {
                select.SelectedIndex = i;
                return;
            }
        }
        select.SelectedIndex = select.Items.Count > 0 ? 0 : -1;
    }

    private static AntSelect CreateComboBox() => new()
    {
        Width = 112, Height = 38, Radius = 6, BorderWidth = 1F,
        BorderColor = Border, BorderHover = Accent, BorderActive = Accent,
        BackColor = Color.White, ForeColor = PrimaryText, Margin = Padding.Empty,
        Font = new Font("Microsoft YaHei UI", 9F), TextAlign = HorizontalAlignment.Center,
        DropDownTextAlign = AntdUI.TAlign.None, ListAutoWidth = true
    };

    private static AntInputNumber CreateNumericInput(int value) => new()
    {
        Minimum = 64, Maximum = 800, Increment = 8, Value = value,
        Width = 112, Height = 38, TextAlign = HorizontalAlignment.Center,
        SuffixText = "px", SuffixFore = SecondaryText, ShowControl = true,
        Radius = 6, BorderWidth = 1F, BorderColor = Border, BorderHover = Accent, BorderActive = Accent,
        BackColor = Color.White, ForeColor = PrimaryText, Margin = Padding.Empty
    };

    private static AntInput CreateKeyDisplay(string text)
    {
        var font = new Font("Microsoft YaHei UI", 9F);
        return new AntInput
        {
            ReadOnly = true, Text = text, Width = MeasureOptionWidth(text, font, 72, 170), Height = 38, TabStop = false,
            TextAlign = HorizontalAlignment.Center, Radius = 6, BorderWidth = 1F,
            BorderColor = Border, BackColor = Color.FromArgb(248, 250, 252),
            ForeColor = PrimaryText, Font = font, Margin = Padding.Empty
        };
    }

    private static AntSwitch CreateCheckBox(bool value) => new()
    {
        Checked = value, AutoSize = false, Size = new Size(44, 24),
        Fill = Accent, FillHover = Color.FromArgb(19, 151, 139),
        WaveSize = 0, Margin = new Padding(0, 6, 4, 0)
    };

    private static AntButton CreatePrimaryButton(string text)
    {
        var font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        return new AntButton
        {
            Text = text, Size = new Size(MeasureButtonWidth(text, font), 40), Type = AntdUI.TTypeMini.Primary,
            Radius = 6, BorderWidth = 0, BackColor = Accent, ForeColor = Color.White,
            BackHover = Color.FromArgb(20, 154, 143), BackActive = Color.FromArgb(15, 132, 122),
            ForeHover = Color.White, ForeActive = Color.White,
            Font = font, TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand,
            Margin = new Padding(10, 0, 0, 0)
        };
    }

    private static AntButton CreateSecondaryButton(string text)
    {
        var font = new Font("Microsoft YaHei UI", 9F);
        return new AntButton
        {
            Text = text, Size = new Size(MeasureButtonWidth(text, font), 38), Radius = 6, BorderWidth = 1.2F,
            DefaultBorderColor = Accent, DefaultBack = Color.FromArgb(221, 243, 239), BackColor = Color.FromArgb(221, 243, 239),
            BackHover = Accent, BackActive = Color.FromArgb(15, 132, 122),
            ForeColor = Color.FromArgb(18, 126, 116), ForeHover = Color.White, ForeActive = Color.White,
            Font = font, TextAlign = ContentAlignment.MiddleCenter, Cursor = Cursors.Hand,
            Margin = new Padding(10, 0, 0, 0)
        };
    }

    private static int MeasureButtonWidth(string text, Font font) =>
        Math.Clamp(TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding).Width + 32, 72, 148);

    private static int MeasureOptionWidth(string text, Font font, int minimum, int maximum) =>
        Math.Clamp(TextRenderer.MeasureText(text, font, Size.Empty, TextFormatFlags.NoPadding).Width + 38, minimum, maximum);

    private static void FitSelectWidth(AntSelect select)
    {
        var width = 0;
        for (var i = 0; i < select.Items.Count; i++)
        {
            var text = select.Items[i]?.ToString() ?? string.Empty;
            var textWidth = TextRenderer.MeasureText(text, select.Font, Size.Empty, TextFormatFlags.NoPadding).Width;
            width = Math.Max(width, Math.Clamp(textWidth + 64, 112, 210));
        }
        select.Width = Math.Max(112, width);
    }

    private static void SetKeyDisplayText(AntInput input, string text)
    {
        input.Text = text;
        input.Width = MeasureOptionWidth(text, input.Font, 72, 170);
    }

    private static void SetHotkeyDisplayText(AntInput input, string text)
    {
        input.Text = text;
        input.Width = MeasureOptionWidth(text, input.Font, 72, 140);
    }

    private static void SetButtonText(AntButton button, string text)
    {
        button.Text = text;
        button.Width = MeasureButtonWidth(text, button.Font);
    }

    private sealed record ShapeChoice(RevealShape Shape, string Name) { public override string ToString() => Name; }
    private sealed record TriggerChoice(PeekTrigger Trigger, string Name) { public override string ToString() => Name; }
    private sealed record LanguageChoice(UiLanguage Language, string Name) { public override string ToString() => Name; }

    private sealed class HotkeyEditor
    {
        private bool _capturing;
        public HotkeyBinding Binding { get; private set; }
        public AntInput Display { get; }
        public AntButton CaptureButton { get; }
        public AntButton ClearButton { get; }

        public HotkeyEditor(HotkeyBinding binding, UiLanguage language)
        {
            Binding = binding;
            Display = CreateKeyDisplay(UiText.ShortcutName(language, binding));
            Display.Width = Math.Min(Display.Width, 140);
            CaptureButton = CreateSecondaryButton(UiText.Text(language, "changeKey"));
            ClearButton = CreateSecondaryButton(UiText.Text(language, "clearHotkey"));
            ClearButton.Width = 64;
            ClearButton.Enabled = !binding.IsDisabled;
        }

        public void BeginCapture(UiLanguage language)
        {
            _capturing = true;
            SetButtonText(CaptureButton, UiText.Text(language, "pressShortcut"));
            CaptureButton.Focus();
        }

        public void CancelCapture(UiLanguage language)
        {
            _capturing = false;
            SetButtonText(CaptureButton, UiText.Text(language, "changeKey"));
        }

        public void Clear(UiLanguage language)
        {
            _capturing = false;
            Binding = HotkeyBinding.Disabled;
            SetHotkeyDisplayText(Display, UiText.ShortcutName(language, Binding));
            SetButtonText(CaptureButton, UiText.Text(language, "changeKey"));
            ClearButton.Enabled = false;
        }

        public bool TryCapture(KeyEventArgs e, UiLanguage language)
        {
            if (!_capturing || e.KeyValue is < 1 or > 255) return false;
            if (HotkeyBinding.IsModifierVirtualKey(e.KeyValue)) return false;
            var modifiers = ShortcutModifiers.None;
            if (e.Modifiers.HasFlag(Keys.Control)) modifiers |= ShortcutModifiers.Control;
            if (e.Modifiers.HasFlag(Keys.Alt)) modifiers |= ShortcutModifiers.Alt;
            if (e.Modifiers.HasFlag(Keys.Shift)) modifiers |= ShortcutModifiers.Shift;
            Binding = new HotkeyBinding(e.KeyValue, modifiers);
            SetHotkeyDisplayText(Display, UiText.ShortcutName(language, Binding));
            SetButtonText(CaptureButton, UiText.Text(language, "changeKey"));
            ClearButton.Enabled = true;
            _capturing = false;
            return true;
        }
    }

    private enum NavGlyph
    {
        Reveal,
        Keyboard,
        General
    }

    private sealed class NavButton : AntButton
    {
        private bool _selected;
        private bool _compact;
        public string LabelText { get; init; } = string.Empty;
        public NavGlyph Glyph { get; init; }
        public bool Compact
        {
            get => _compact;
            set
            {
                _compact = value;
                Invalidate();
            }
        }
        public bool Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                BackColor = value ? SidebarSelected : Sidebar;
                DefaultBack = value ? SidebarSelected : Color.Transparent;
                ForeColor = value ? Color.FromArgb(18, 126, 116) : Color.FromArgb(79, 91, 107);
                Invalidate();
            }
        }

        public NavButton()
        {
            Radius = 6;
            BorderWidth = 0;
            BackColor = Sidebar;
            DefaultBack = Color.Transparent;
            BackHover = Color.FromArgb(244, 248, 249);
            BackActive = SidebarSelected;
            ForeColor = Color.FromArgb(79, 91, 107);
            Text = string.Empty;
            Cursor = Cursors.Hand;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var color = Selected ? Color.FromArgb(18, 126, 116) : Color.FromArgb(79, 91, 107);
            var iconX = Compact ? (Width - 18) / 2 : 12;
            var iconY = (Height - 18) / 2;
            using var pen = new Pen(color, 2F)
            {
                StartCap = System.Drawing.Drawing2D.LineCap.Round,
                EndCap = System.Drawing.Drawing2D.LineCap.Round
            };
            using var brush = new SolidBrush(color);
            switch (Glyph)
            {
                case NavGlyph.Reveal:
                    e.Graphics.DrawEllipse(pen, iconX, iconY + 4, 18, 10);
                    e.Graphics.FillEllipse(brush, iconX + 7, iconY + 7, 4, 4);
                    break;
                case NavGlyph.Keyboard:
                    e.Graphics.DrawRectangle(pen, iconX + 1, iconY + 3, 16, 12);
                    e.Graphics.DrawLine(pen, iconX + 5, iconY + 11, iconX + 13, iconY + 11);
                    break;
                case NavGlyph.General:
                    e.Graphics.DrawEllipse(pen, iconX + 2, iconY + 2, 14, 14);
                    e.Graphics.FillEllipse(brush, iconX + 7, iconY + 7, 4, 4);
                    break;
            }

            if (!Compact)
            {
                TextRenderer.DrawText(
                    e.Graphics,
                    LabelText,
                    Font,
                    new Rectangle(40, 0, Math.Max(0, Width - 48), Height),
                    color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
        }
    }
}
