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
    private static readonly Color Conflict = Color.FromArgb(214, 58, 72);
    private static readonly Color ConflictSurface = Color.FromArgb(255, 246, 247);
    private static readonly AppSettings DefaultSettings = new();

    private readonly UiLanguage _initialLanguage;
    private readonly AntInputNumber _diameter;
    private readonly AntInputNumber _diameterStep;
    private readonly AntInputNumber _softEdgeWidth;
    private readonly AntSelect _blurLevel;
    private readonly AntSelect _shape;
    private readonly KeyDisplay _peekKeyDisplay;
    private readonly HotkeyEditor _pickHotkey;
    private readonly HotkeyEditor _windowToggleHotkey;
    private readonly HotkeyEditor _restoreHotkey;
    private readonly HotkeyEditor _restoreAllHotkey;
    private readonly HotkeyEditor _settingsHotkey;
    private readonly HotkeyEditor _exitHotkey;
    private readonly HotkeyEditor _diameterIncreaseHotkey;
    private readonly HotkeyEditor _diameterDecreaseHotkey;
    private readonly AntSelect _peekMode;
    private readonly AntSwitch _restoreOnExit;
    private readonly AntSwitch _startWithWindows;
    private readonly AntSelect _logLevel;
    private readonly AntSelect _language;
    private readonly Label _pageTitle;
    private readonly Label _pageDescription;
    private readonly Panel _pageHost;
    private readonly List<NavButton> _navigation = [];
    private readonly Dictionary<Control, string> _localizedTextKeys = [];
    private readonly Dictionary<Control, string> _localizedToolTipKeys = [];
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

    public event Func<bool>? SaveRequested;

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

        _diameter = CreateNumericInput(settings.RevealDiameterPx, 64, 800, 8);
        _diameterStep = CreateNumericInput(settings.RevealDiameterStepPx, 8, 256, 8);
        _softEdgeWidth = CreateNumericInput(settings.RevealSoftEdgeWidthPx, 0, 128, 4);
        _blurLevel = CreateComboBox();
        _blurLevel.Items.AddRange([
            new BlurLevelChoice(RevealBlurLevel.Low, UiText.Text(settings.Language, "blurLow")),
            new BlurLevelChoice(RevealBlurLevel.Medium, UiText.Text(settings.Language, "blurMedium")),
            new BlurLevelChoice(RevealBlurLevel.High, UiText.Text(settings.Language, "blurHigh"))]);
        FitSelectWidth(_blurLevel);
        SelectBlurLevel(settings.RevealBlurLevel);
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
        _peekKeyDisplay.EditRequested += (_, _) => BeginPeekKeyCapture(CurrentLanguage);
        _pickHotkey = CreateHotkeyEditor(settings.PickHotkey, DefaultSettings.PickHotkey, settings.Language);
        _windowToggleHotkey = CreateHotkeyEditor(settings.WindowToggleHotkey, DefaultSettings.WindowToggleHotkey, settings.Language);
        _restoreHotkey = CreateHotkeyEditor(settings.RestoreHotkey, DefaultSettings.RestoreHotkey, settings.Language);
        _restoreAllHotkey = CreateHotkeyEditor(settings.RestoreAllHotkey, DefaultSettings.RestoreAllHotkey, settings.Language);
        _settingsHotkey = CreateHotkeyEditor(settings.SettingsHotkey, DefaultSettings.SettingsHotkey, settings.Language);
        _exitHotkey = CreateHotkeyEditor(settings.ExitHotkey, DefaultSettings.ExitHotkey, settings.Language);
        _diameterIncreaseHotkey = CreateHotkeyEditor(
            settings.RevealDiameterIncreaseHotkey,
            DefaultSettings.RevealDiameterIncreaseHotkey,
            settings.Language);
        _diameterDecreaseHotkey = CreateHotkeyEditor(
            settings.RevealDiameterDecreaseHotkey,
            DefaultSettings.RevealDiameterDecreaseHotkey,
            settings.Language);
        UpdateHotkeyConflicts();

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
        _language.SelectedIndexChanged += (_, _) => ApplyLanguage(CurrentLanguage);
        KeyDown += OnKeyDown;
        FormClosed += (_, _) =>
        {
            _toolTip?.Dispose();
            _brandIcon?.Image?.Dispose();
        };
    }

    public AppSettings GetSettings(AppSettings current)
    {
        var blurLevel = _blurLevel.SelectedValue is BlurLevelChoice selectedBlurLevel
            ? selectedBlurLevel.Level
            : current.RevealBlurLevel;
        var shape = _shape.SelectedValue is ShapeChoice selectedShape ? selectedShape.Shape : current.RevealShape;
        var peekMode = _peekMode.SelectedValue is TriggerChoice selectedTrigger ? selectedTrigger.Trigger : current.PeekTrigger;
        var logLevel = _logLevel.SelectedValue is LogLevel selectedLevel ? selectedLevel : current.MinimumLogLevel;
        var language = _language.SelectedValue is LanguageChoice selectedLanguage ? selectedLanguage.Language : current.Language;
        return current with
        {
            RevealDiameterPx = (int)_diameter.Value,
            RevealDiameterStepPx = (int)_diameterStep.Value,
            RevealSoftEdgeWidthPx = (int)_softEdgeWidth.Value,
            RevealBlurLevel = blurLevel,
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
            RevealDiameterIncreaseHotkey = _diameterIncreaseHotkey.Binding,
            RevealDiameterDecreaseHotkey = _diameterDecreaseHotkey.Binding,
            RestoreOnExit = _restoreOnExit.Checked,
            StartWithWindows = _startWithWindows.Checked,
            MinimumLogLevel = logLevel
        };
    }

    private void BuildLayout(UiLanguage language)
    {
        _rootLayout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Canvas
        };
        _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 168F));
        _rootLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _rootLayout.RowStyles.Add(new RowStyle(SizeType.Absolute, 72F));
        Controls.Add(_rootLayout);

        var content = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 2,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Canvas
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
        ConfigureNavigation(_navigation[0], revealPage, "revealSettings", "revealSettingsDescription");
        ConfigureNavigation(_navigation[1], hotkeysPage, "hotkeySettings", "hotkeySettingsDescription");
        ConfigureNavigation(_navigation[2], generalPage, "generalSettings", "generalSettingsDescription");
        ShowPage(_navigation[0], revealPage, language);
        SetSidebarCollapsed(false, language);
    }

    private Control CreateSidebar(UiLanguage language)
    {
        _sidebarPanel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Margin = Padding.Empty,
            Padding = new Padding(12, 18, 12, 12),
            BackColor = Sidebar
        };
        _sidebarPanel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        _sidebarPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 84F));
        _sidebarPanel.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        _sidebarPanel.RowStyles.Add(new RowStyle(SizeType.Absolute, 48F));

        _brandPanel = new Panel { Dock = DockStyle.Fill, Margin = Padding.Empty, BackColor = Sidebar };
        _brandIcon = new PictureBox
        {
            Image = AppIcon.Instance.ToBitmap(),
            SizeMode = PictureBoxSizeMode.Zoom,
            BackColor = Color.Transparent,
            Location = Point.Empty,
            Size = new Size(36, 36),
            TabStop = false
        };
        _brandName = new Label
        {
            Text = UiText.Text(language, "settings"),
            AutoSize = false,
            Font = new Font(Font.FontFamily, 11F, FontStyle.Bold),
            ForeColor = PrimaryText,
            TextAlign = ContentAlignment.MiddleLeft,
            Location = new Point(46, 0),
            Size = new Size(90, 36)
        };
        _localizedTextKeys[_brandName] = "settings";
        _brandPanel.Controls.AddRange([_brandIcon, _brandName]);

        var nav = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Sidebar
        };
        AddNavigationButton(nav, language, "revealSettings", NavGlyph.Reveal);
        AddNavigationButton(nav, language, "hotkeySettings", NavGlyph.Keyboard);
        AddNavigationButton(nav, language, "generalSettings", NavGlyph.General);

        _collapseButton = new AntButton
        {
            Dock = DockStyle.Right,
            Width = 40,
            Height = 36,
            Radius = 6,
            BorderWidth = 0,
            Text = "‹",
            Font = new Font(Font.FontFamily, 18F),
            ForeColor = SecondaryText,
            BackColor = Color.Transparent,
            BackHover = SidebarSelected,
            Cursor = Cursors.Hand,
            Margin = Padding.Empty
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
            Dock = DockStyle.Fill,
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            ColumnCount = 1,
            RowCount = 2,
            MinimumSize = new Size(0, 112),
            Margin = Padding.Empty,
            Padding = new Padding(38, 22, 34, 18),
            BackColor = Canvas
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
            Dock = DockStyle.Right,
            AutoSize = true,
            FlowDirection = FlowDirection.RightToLeft,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty
        };
        var save = CreatePrimaryButton(UiText.Text(language, "save"));
        _localizedTextKeys[save] = "save";
        save.Click += (_, _) => RequestSave();
        var cancel = CreateSecondaryButton(UiText.Text(language, "cancel"));
        _localizedTextKeys[cancel] = "cancel";
        cancel.DialogResult = DialogResult.Cancel;
        buttons.Controls.Add(save);
        buttons.Controls.Add(cancel);
        footer.Controls.Add(buttons);
        AcceptButton = save;
        return footer;
    }

    private void RequestSave()
    {
        if (ValidateUniqueHotkeys() && SaveRequested?.Invoke() == true)
        {
            AntdUI.Message.success(this, UiText.Text(CurrentLanguage, "saveSucceeded"), autoClose: 1);
        }
    }

    public void ShowSaveError()
    {
        AntdUI.Message.error(this, UiText.Text(CurrentLanguage, "settingsSaveFailed"), autoClose: 2);
    }

    private bool ValidateUniqueHotkeys()
    {
        var editors = GetHotkeyEditors();
        UpdateHotkeyConflicts();
        var duplicate = editors
            .Where(editor => !editor.Binding.IsDisabled)
            .GroupBy(editor => editor.Binding)
            .FirstOrDefault(group => group.Count() > 1);
        if (duplicate is null)
        {
            return true;
        }

        MessageBox.Show(
            this,
            string.Format(UiText.Text(CurrentLanguage, "duplicateHotkey"), UiText.ShortcutName(CurrentLanguage, duplicate.Key)),
            UiText.Text(CurrentLanguage, "title"),
            MessageBoxButtons.OK,
            MessageBoxIcon.Warning);
        return false;
    }

    private Control CreateRevealPage(UiLanguage language)
    {
        return CreatePage([
            CreateSection(language, "revealAppearance", [
                CreateSettingRow(language, "diameter", "diameterDescription", CreateResettableEditor(
                    _diameter,
                    () => _diameter.Value = DefaultSettings.RevealDiameterPx)),
                CreateSettingRow(language, "diameterStep", "diameterStepDescription", CreateResettableEditor(
                    _diameterStep,
                    () => _diameterStep.Value = DefaultSettings.RevealDiameterStepPx)),
                CreateSettingRow(language, "softEdgeWidth", "softEdgeWidthDescription", CreateResettableEditor(
                    _softEdgeWidth,
                    () => _softEdgeWidth.Value = DefaultSettings.RevealSoftEdgeWidthPx)),
                CreateSettingRow(language, "blurLevel", "blurLevelDescription", CreateResettableEditor(
                    _blurLevel,
                    () => SelectBlurLevel(DefaultSettings.RevealBlurLevel))),
                CreateSettingRow(language, "shape", "shapeDescription", CreateResettableEditor(
                    _shape,
                    () => SelectShape(DefaultSettings.RevealShape)))]),
            CreateSection(language, "peekBehavior", [
                CreateSettingRow(language, "peekMode", "peekModeDescription", CreateResettableEditor(
                    _peekMode,
                    () => SelectPeekMode(DefaultSettings.PeekTrigger))),
                CreateSettingRow(language, "peekKey", "peekKeyDescription", CreateKeyEditorPanel(
                    _peekKeyDisplay,
                    () => ResetPeekKey(CurrentLanguage)))])]);
    }

    private Control CreateHotkeysPage(UiLanguage language) => CreatePage([
        CreateSection(language, "windowActions", [
            ShortcutRow(language, "pickHotkey", "pickHotkeyDescription", _pickHotkey),
            ShortcutRow(language, "windowToggleHotkey", "windowToggleHotkeyDescription", _windowToggleHotkey),
            ShortcutRow(language, "diameterIncreaseHotkey", "diameterIncreaseHotkeyDescription", _diameterIncreaseHotkey),
            ShortcutRow(language, "diameterDecreaseHotkey", "diameterDecreaseHotkeyDescription", _diameterDecreaseHotkey),
            ShortcutRow(language, "restoreHotkey", "restoreHotkeyDescription", _restoreHotkey),
            ShortcutRow(language, "restoreAllHotkey", "restoreAllHotkeyDescription", _restoreAllHotkey)]),
        CreateSection(language, "applicationActions", [
            ShortcutRow(language, "settingsHotkey", "settingsHotkeyDescription", _settingsHotkey),
            ShortcutRow(language, "exitHotkey", "exitHotkeyDescription", _exitHotkey)])]);

    private Control ShortcutRow(UiLanguage language, string titleKey, string descriptionKey, HotkeyEditor editor) =>
        CreateSettingRow(language, titleKey, descriptionKey, CreateKeyEditorPanel(
            editor.Display,
            () => ResetHotkey(editor, CurrentLanguage)));

    private Control CreateKeyEditorPanel(KeyDisplay display, Action reset) =>
        CreateResettableEditor(display, reset, "resetHotkey");

    private Control CreateResettableEditor(Control editor, Action reset, string tooltipKey = "resetSetting")
    {
        var panel = new FlowLayoutPanel
        {
            AutoSize = true,
            AutoSizeMode = AutoSizeMode.GrowAndShrink,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Surface
        };
        var resetButton = new AntButton
        {
            Text = "↺",
            Size = new Size(30, 30),
            Radius = 6,
            BorderWidth = 0,
            BackColor = Color.Transparent,
            DefaultBack = Color.Transparent,
            BackHover = Color.FromArgb(229, 237, 240),
            BackActive = Color.FromArgb(216, 229, 231),
            ForeColor = SecondaryText,
            ForeHover = Color.FromArgb(18, 126, 116),
            ForeActive = Color.FromArgb(15, 105, 97),
            Font = new Font("Segoe UI Symbol", 12F, FontStyle.Regular),
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
            Margin = new Padding(8, 4, 0, 4),
            TabStop = true
        };
        resetButton.Click += (_, _) => reset();
        _localizedToolTipKeys[resetButton] = tooltipKey;
        var resetText = UiText.Text(CurrentLanguage, tooltipKey);
        resetButton.AccessibleName = resetText;
        _toolTip?.SetToolTip(resetButton, resetText);
        panel.Controls.Add(editor);
        panel.Controls.Add(resetButton);
        return panel;
    }

    private Control CreateGeneralPage(UiLanguage language) => CreatePage([
        CreateSection(language, "startupAndSafety", [
            CreateSettingRow(language, "startWindows", "startWindowsDescription", CreateResettableEditor(
                _startWithWindows,
                () => _startWithWindows.Checked = DefaultSettings.StartWithWindows)),
            CreateSettingRow(language, "restoreOnExit", "restoreOnExitDescription", CreateResettableEditor(
                _restoreOnExit,
                () => _restoreOnExit.Checked = DefaultSettings.RestoreOnExit))]),
        CreateSection(language, "languageAndDiagnostics", [
            CreateSettingRow(language, "interfaceLanguage", "languageDescription", CreateResettableEditor(
                _language,
                () => SelectLanguage(DefaultSettings.Language))),
            CreateSettingRow(language, "logLevel", "logLevelDescription", CreateResettableEditor(
                _logLevel,
                () => SelectValue(_logLevel, DefaultSettings.MinimumLogLevel)))])]);

    private static Control CreatePage(Control[] sections)
    {
        var scroll = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoScroll = true,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = new Padding(34, 0, 25, 24),
            BackColor = Canvas,
            Visible = false
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
            Width = 560,
            Height = 29 + (rows.Length * 84),
            ColumnCount = 1,
            RowCount = 2,
            Margin = new Padding(0, 0, 0, 20),
            Padding = Padding.Empty,
            BackColor = Canvas
        };
        section.RowStyles.Add(new RowStyle(SizeType.Absolute, 29F));
        section.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        var heading = new Label
        {
            Text = UiText.Text(language, titleKey),
            AutoSize = true,
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold),
            ForeColor = SecondaryText,
            Margin = new Padding(2, 0, 0, 0),
            Anchor = AnchorStyles.Left
        };
        _localizedTextKeys[heading] = titleKey;
        var card = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.TopDown,
            WrapContents = false,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Canvas
        };
        card.Controls.AddRange(rows);
        card.Resize += (_, _) =>
        {
            foreach (Control row in card.Controls)
            {
                row.Width = Math.Max(478, card.ClientSize.Width);
            }
        };
        section.Controls.Add(heading, 0, 0);
        section.Controls.Add(card, 0, 1);
        return section;
    }

    private Control CreateSettingRow(UiLanguage language, string titleKey, string descriptionKey, Control editor)
    {
        var surface = new AntPanel
        {
            Height = 76,
            Width = 560,
            Radius = 8,
            BorderWidth = 0,
            Margin = new Padding(0, 0, 0, 8),
            Padding = Padding.Empty,
            Back = Surface,
            BackColor = Surface
        };
        var layout = new TableLayoutPanel
        {
            ColumnCount = 2,
            RowCount = 1,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = new Padding(20, 8, 12, 8),
            BackColor = Color.Transparent
        };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100F));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 380F));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
        var copy = new TableLayoutPanel
        {
            ColumnCount = 1,
            RowCount = 2,
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Color.Transparent
        };
        copy.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        copy.RowStyles.Add(new RowStyle(SizeType.Percent, 50F));
        var title = new Label
        {
            Text = UiText.Text(language, titleKey),
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 9.5F, FontStyle.Bold),
            ForeColor = PrimaryText,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = Padding.Empty
        };
        var description = new Label
        {
            Text = UiText.Text(language, descriptionKey),
            AutoSize = false,
            Dock = DockStyle.Fill,
            Font = new Font(Font.FontFamily, 8.5F),
            ForeColor = SecondaryText,
            TextAlign = ContentAlignment.MiddleLeft,
            AutoEllipsis = true,
            Margin = new Padding(0, 0, 12, 0)
        };
        _localizedTextKeys[title] = titleKey;
        _localizedTextKeys[description] = descriptionKey;
        copy.Controls.Add(title, 0, 0);
        copy.Controls.Add(description, 0, 1);

        var editorHost = new Panel
        {
            Dock = DockStyle.Fill,
            Margin = Padding.Empty,
            Padding = Padding.Empty,
            BackColor = Surface,
            TabStop = false
        };
        editor.Anchor = AnchorStyles.None;
        editorHost.Controls.Add(editor);
        editorHost.Layout += (_, _) => CenterEditor(editorHost, editor);
        editor.SizeChanged += (_, _) => editorHost.PerformLayout();
        layout.Controls.Add(copy, 0, 0);
        layout.Controls.Add(editorHost, 1, 0);
        surface.Controls.Add(layout);
        return surface;
    }

    private static void CenterEditor(Control host, Control editor)
    {
        var location = new Point(
            Math.Max(0, (host.ClientSize.Width - editor.Width) / 2),
            Math.Max(0, (host.ClientSize.Height - editor.Height) / 2));
        if (editor.Location != location)
        {
            editor.Location = location;
        }
    }

    private void AddNavigationButton(Control parent, UiLanguage language, string textKey, NavGlyph glyph)
    {
        var text = UiText.Text(language, textKey);
        var button = new NavButton
        {
            TextKey = textKey,
            LabelText = text,
            Glyph = glyph,
            AccessibleName = text,
            Width = 144,
            Height = 44,
            Margin = new Padding(0, 0, 0, 6),
            Font = new Font(Font.FontFamily, 9F, FontStyle.Bold)
        };
        _navigation.Add(button);
        parent.Controls.Add(button);
    }

    private void ConfigureNavigation(NavButton button, Control page, string titleKey, string descriptionKey)
    {
        button.PageTitleKey = titleKey;
        button.PageDescriptionKey = descriptionKey;
        button.Click += (_, _) => ShowPage(button, page, CurrentLanguage);
    }

    private void ShowPage(NavButton selected, Control page, UiLanguage language)
    {
        CancelKeyCapture();
        foreach (Control candidate in _pageHost.Controls) candidate.Visible = ReferenceEquals(candidate, page);
        foreach (var button in _navigation) button.Selected = ReferenceEquals(button, selected);
        page.BringToFront();
        _pageTitle.Text = UiText.Text(language, selected.PageTitleKey);
        _pageDescription.Text = UiText.Text(language, selected.PageDescriptionKey);
    }

    private void ApplyLanguage(UiLanguage language)
    {
        CancelKeyCapture();
        Text = UiText.Text(language, "title");

        foreach (var (control, key) in _localizedTextKeys)
        {
            var text = UiText.Text(language, key);
            if (control is AntButton button)
            {
                SetButtonText(button, text);
            }
            else
            {
                control.Text = text;
            }
        }

        foreach (var button in _navigation)
        {
            button.LabelText = UiText.Text(language, button.TextKey);
            button.AccessibleName = button.LabelText;
            _toolTip?.SetToolTip(button, _sidebarCollapsed ? button.LabelText : string.Empty);
            button.Invalidate();
        }

        foreach (var (control, key) in _localizedToolTipKeys)
        {
            var text = UiText.Text(language, key);
            control.AccessibleName = text;
            _toolTip?.SetToolTip(control, text);
        }

        var selected = _navigation.FirstOrDefault(button => button.Selected);
        if (selected is not null)
        {
            _pageTitle.Text = UiText.Text(language, selected.PageTitleKey);
            _pageDescription.Text = UiText.Text(language, selected.PageDescriptionKey);
        }

        RefreshChoiceText(language);
        SetKeyDisplayText(_peekKeyDisplay, UiText.PeekKeyName(language, _peekVirtualKey));
        foreach (var editor in GetHotkeyEditors())
        {
            editor.ApplyLanguage(language);
        }

        if (_collapseButton is not null)
        {
            _toolTip?.SetToolTip(_collapseButton, UiText.Text(language, _sidebarCollapsed ? "expandSidebar" : "collapseSidebar"));
        }

        PerformLayout();
    }

    private void RefreshChoiceText(UiLanguage language)
    {
        var blurLevel = _blurLevel.SelectedValue is BlurLevelChoice blurLevelChoice
            ? blurLevelChoice.Level
            : DefaultSettings.RevealBlurLevel;
        _blurLevel.Items.Clear();
        _blurLevel.Items.AddRange([
            new BlurLevelChoice(RevealBlurLevel.Low, UiText.Text(language, "blurLow")),
            new BlurLevelChoice(RevealBlurLevel.Medium, UiText.Text(language, "blurMedium")),
            new BlurLevelChoice(RevealBlurLevel.High, UiText.Text(language, "blurHigh"))]);
        SelectBlurLevel(blurLevel);
        FitSelectWidth(_blurLevel);

        var shape = _shape.SelectedValue is ShapeChoice shapeChoice ? shapeChoice.Shape : DefaultSettings.RevealShape;
        _shape.Items.Clear();
        _shape.Items.AddRange([
            new ShapeChoice(RevealShape.Circle, UiText.Text(language, "circle")),
            new ShapeChoice(RevealShape.Rectangle, UiText.Text(language, "rectangle")),
            new ShapeChoice(RevealShape.RoundedRectangle, UiText.Text(language, "roundedRectangle"))]);
        SelectShape(shape);
        FitSelectWidth(_shape);

        var trigger = _peekMode.SelectedValue is TriggerChoice triggerChoice ? triggerChoice.Trigger : DefaultSettings.PeekTrigger;
        _peekMode.Items.Clear();
        _peekMode.Items.AddRange([
            new TriggerChoice(PeekTrigger.Hold, UiText.Text(language, "holdPeek")),
            new TriggerChoice(PeekTrigger.Toggle, UiText.Text(language, "togglePeek"))]);
        SelectPeekMode(trigger);
        FitSelectWidth(_peekMode);
    }

    private HotkeyEditor[] GetHotkeyEditors() =>
        [
            _pickHotkey,
            _windowToggleHotkey,
            _diameterIncreaseHotkey,
            _diameterDecreaseHotkey,
            _restoreHotkey,
            _restoreAllHotkey,
            _settingsHotkey,
            _exitHotkey
        ];

    private void UpdateHotkeyConflicts()
    {
        var editors = GetHotkeyEditors();
        var conflicts = editors
            .Where(editor => !editor.Binding.IsDisabled)
            .GroupBy(editor => editor.Binding)
            .Where(group => group.Count() > 1)
            .SelectMany(group => group)
            .ToHashSet();

        foreach (var editor in editors)
        {
            editor.Display.SetConflict(conflicts.Contains(editor));
        }
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
            SetKeyDisplayText(_peekKeyDisplay, UiText.PeekKeyName(CurrentLanguage, _peekVirtualKey));
            _capturingPeekKey = false;
        }
    }

    private void BeginPeekKeyCapture(UiLanguage language)
    {
        CancelKeyCapture();
        _capturingPeekKey = true;
        SetKeyDisplayText(_peekKeyDisplay, UiText.Text(language, "pressKey"));
        _peekKeyDisplay.Focus();
    }

    private void ResetPeekKey(UiLanguage language)
    {
        CancelKeyCapture();
        _peekVirtualKey = DefaultSettings.PeekVirtualKey;
        SetKeyDisplayText(_peekKeyDisplay, UiText.PeekKeyName(language, _peekVirtualKey));
    }

    private void ResetHotkey(HotkeyEditor editor, UiLanguage language)
    {
        CancelKeyCapture();
        editor.Reset(language);
        UpdateHotkeyConflicts();
    }

    private HotkeyEditor CreateHotkeyEditor(
        HotkeyBinding binding,
        HotkeyBinding defaultBinding,
        UiLanguage language)
    {
        var editor = new HotkeyEditor(binding, defaultBinding, language);
        editor.Display.EditRequested += (_, _) =>
        {
            CancelKeyCapture();
            _activeHotkeyEditor = editor;
            editor.BeginCapture(CurrentLanguage);
        };
        editor.Display.ClearRequested += (_, _) =>
        {
            CancelKeyCapture();
            editor.Clear(CurrentLanguage);
            UpdateHotkeyConflicts();
        };
        return editor;
    }

    protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
    {
        if ((keyData & Keys.KeyCode) == Keys.Escape &&
            (_activeHotkeyEditor is not null || _capturingPeekKey))
        {
            CancelKeyCapture();
            return true;
        }

        return base.ProcessCmdKey(ref msg, keyData);
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.KeyCode == Keys.Escape &&
            (_activeHotkeyEditor is not null || _capturingPeekKey))
        {
            CancelKeyCapture();
            e.Handled = true;
            e.SuppressKeyPress = true;
            return;
        }

        if (_activeHotkeyEditor is not null)
        {
            if (_activeHotkeyEditor.TryCapture(e, CurrentLanguage))
            {
                _activeHotkeyEditor = null;
                UpdateHotkeyConflicts();
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
            return;
        }
        if (!_capturingPeekKey || e.KeyValue is < 1 or > 255) return;
        _peekVirtualKey = e.KeyValue;
        SetKeyDisplayText(_peekKeyDisplay, UiText.PeekKeyName(CurrentLanguage, _peekVirtualKey));
        _capturingPeekKey = false;
        e.Handled = true;
        e.SuppressKeyPress = true;
    }

    private UiLanguage CurrentLanguage => _language.SelectedValue is LanguageChoice choice ? choice.Language : _initialLanguage;

    private void SelectLanguage(UiLanguage language)
    {
        for (var i = 0; i < _language.Items.Count; i++)
        {
            if (_language.Items[i] is LanguageChoice choice && choice.Language == language)
            {
                _language.SelectedIndex = i;
                return;
            }
        }
        _language.SelectedIndex = 0;
    }

    private void SelectBlurLevel(RevealBlurLevel level)
    {
        for (var i = 0; i < _blurLevel.Items.Count; i++)
        {
            if (_blurLevel.Items[i] is BlurLevelChoice choice && choice.Level == level)
            {
                _blurLevel.SelectedIndex = i;
                return;
            }
        }
        _blurLevel.SelectedIndex = 1;
    }

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
        Width = 112,
        Height = 38,
        Radius = 6,
        BorderWidth = 1F,
        BorderColor = Border,
        BorderHover = Accent,
        BorderActive = Accent,
        BackColor = Color.White,
        ForeColor = PrimaryText,
        Margin = Padding.Empty,
        Font = new Font("Microsoft YaHei UI", 9F),
        TextAlign = HorizontalAlignment.Center,
        DropDownTextAlign = AntdUI.TAlign.None,
        ListAutoWidth = true,
        CaretVisible = false
    };

    private static AntInputNumber CreateNumericInput(int value, int minimum, int maximum, int increment) => new()
    {
        Minimum = minimum,
        Maximum = maximum,
        Increment = increment,
        Value = value,
        Width = 112,
        Height = 38,
        TextAlign = HorizontalAlignment.Center,
        SuffixText = "px",
        SuffixFore = SecondaryText,
        ShowControl = true,
        Radius = 6,
        BorderWidth = 1F,
        BorderColor = Border,
        BorderHover = Accent,
        BorderActive = Accent,
        BackColor = Color.White,
        ForeColor = PrimaryText,
        Margin = Padding.Empty
    };

    private static KeyDisplay CreateKeyDisplay(string text)
    {
        var font = new Font("Microsoft YaHei UI", 9F);
        return new KeyDisplay
        {
            ReadOnly = false,
            CaretVisible = false,
            AllowClear = false,
            UseContextMenu = false,
            ImeMode = ImeMode.Disable,
            Text = text,
            Width = MeasureOptionWidth(text, font, 72, 170),
            Height = 38,
            TabStop = false,
            TextAlign = HorizontalAlignment.Center,
            Radius = 6,
            BorderWidth = 1F,
            BorderColor = Border,
            BorderHover = Accent,
            BorderActive = Accent,
            BackColor = Color.FromArgb(248, 250, 252),
            ForeColor = PrimaryText,
            Font = font,
            Margin = Padding.Empty,
            Cursor = Cursors.Hand
        };
    }

    private static AntSwitch CreateCheckBox(bool value) => new()
    {
        Checked = value,
        AutoSize = false,
        Size = new Size(44, 24),
        Fill = Accent,
        FillHover = Color.FromArgb(19, 151, 139),
        WaveSize = 0,
        Margin = new Padding(0, 6, 4, 0)
    };

    private static AntButton CreatePrimaryButton(string text)
    {
        var font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
        return new AntButton
        {
            Text = text,
            Size = new Size(MeasureButtonWidth(text, font), 40),
            Type = AntdUI.TTypeMini.Primary,
            Radius = 6,
            BorderWidth = 0,
            BackColor = Accent,
            ForeColor = Color.White,
            BackHover = Color.FromArgb(20, 154, 143),
            BackActive = Color.FromArgb(15, 132, 122),
            ForeHover = Color.White,
            ForeActive = Color.White,
            Font = font,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
            Margin = new Padding(10, 0, 0, 0)
        };
    }

    private static AntButton CreateSecondaryButton(string text)
    {
        var font = new Font("Microsoft YaHei UI", 9F);
        return new AntButton
        {
            Text = text,
            Size = new Size(MeasureButtonWidth(text, font), 38),
            Radius = 6,
            BorderWidth = 1.2F,
            DefaultBorderColor = Accent,
            DefaultBack = Color.FromArgb(221, 243, 239),
            BackColor = Color.FromArgb(221, 243, 239),
            BackHover = Accent,
            BackActive = Color.FromArgb(15, 132, 122),
            ForeColor = Color.FromArgb(18, 126, 116),
            ForeHover = Color.White,
            ForeActive = Color.White,
            Font = font,
            TextAlign = ContentAlignment.MiddleCenter,
            Cursor = Cursors.Hand,
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
        input.Width = MeasureOptionWidth(text, input.Font, 112, 190);
    }

    private static void SetButtonText(AntButton button, string text)
    {
        button.Text = text;
        button.Width = MeasureButtonWidth(text, button.Font);
    }

    private sealed record BlurLevelChoice(RevealBlurLevel Level, string Name) { public override string ToString() => Name; }
    private sealed record ShapeChoice(RevealShape Shape, string Name) { public override string ToString() => Name; }
    private sealed record TriggerChoice(PeekTrigger Trigger, string Name) { public override string ToString() => Name; }
    private sealed record LanguageChoice(UiLanguage Language, string Name) { public override string ToString() => Name; }

    private sealed class KeyDisplay : AntInput
    {
        private bool _clearHandled;
        public event EventHandler? EditRequested;
        public event EventHandler? ClearRequested;

        public void SetConflict(bool conflict)
        {
            BorderColor = conflict ? Conflict : Border;
            BorderHover = conflict ? Conflict : Accent;
            BorderActive = conflict ? Conflict : Accent;
            BackColor = conflict ? ConflictSurface : Color.FromArgb(248, 250, 252);
            Invalidate();
        }

        protected override void OnClickContent(MouseEventArgs e)
        {
            _clearHandled = false;
            base.OnClickContent(e);
            if (!_clearHandled)
            {
                EditRequested?.Invoke(this, EventArgs.Empty);
            }
        }

        protected override void OnClearValue()
        {
            _clearHandled = true;
            ClearRequested?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Cursor = Cursors.Hand;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            e.Handled = true;
            e.SuppressKeyPress = true;
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            e.Handled = true;
        }
    }

    private sealed class HotkeyEditor
    {
        private bool _capturing;
        private readonly HotkeyBinding _defaultBinding;
        public HotkeyBinding Binding { get; private set; }
        public KeyDisplay Display { get; }

        public HotkeyEditor(HotkeyBinding binding, HotkeyBinding defaultBinding, UiLanguage language)
        {
            Binding = binding;
            _defaultBinding = defaultBinding;
            Display = CreateKeyDisplay(UiText.ShortcutName(language, binding));
            SetHotkeyDisplayText(Display, UiText.ShortcutName(language, binding));
            Display.AllowClear = !binding.IsDisabled;
        }

        public void BeginCapture(UiLanguage language)
        {
            _capturing = true;
            Display.AllowClear = false;
            SetHotkeyDisplayText(Display, UiText.Text(language, "pressShortcut"));
            Display.Focus();
        }

        public void CancelCapture(UiLanguage language)
        {
            _capturing = false;
            SetHotkeyDisplayText(Display, UiText.ShortcutName(language, Binding));
            Display.AllowClear = !Binding.IsDisabled;
        }

        public void Clear(UiLanguage language)
        {
            _capturing = false;
            Binding = HotkeyBinding.Disabled;
            SetHotkeyDisplayText(Display, UiText.ShortcutName(language, Binding));
            Display.AllowClear = false;
        }

        public void Reset(UiLanguage language)
        {
            _capturing = false;
            Binding = _defaultBinding;
            SetHotkeyDisplayText(Display, UiText.ShortcutName(language, Binding));
            Display.AllowClear = !Binding.IsDisabled;
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
            Display.AllowClear = true;
            _capturing = false;
            return true;
        }

        public void ApplyLanguage(UiLanguage language)
        {
            _capturing = false;
            SetHotkeyDisplayText(Display, UiText.ShortcutName(language, Binding));
            Display.AllowClear = !Binding.IsDisabled;
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
        public string TextKey { get; init; } = string.Empty;
        public string PageTitleKey { get; set; } = string.Empty;
        public string PageDescriptionKey { get; set; } = string.Empty;
        public string LabelText { get; set; } = string.Empty;
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
