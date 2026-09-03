using GhostSlacking.Core;

namespace GhostSlacking.App;

internal sealed class SettingsForm : Form
{
    private readonly NumericUpDown _diameter;
    private readonly ComboBox _shape;
    private readonly ComboBox _peekKey;
    private readonly CheckBox _restoreOnExit;
    private readonly CheckBox _startWithWindows;
    private readonly ComboBox _logLevel;
    private readonly ComboBox _language;

    public SettingsForm(AppSettings settings)
    {
        Text = UiText.Text(settings.Language, "title");
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(380, 320);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(12), ColumnCount = 2, RowCount = 8 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 55));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 45));
        Controls.Add(layout);

        _diameter = new NumericUpDown { Minimum = 64, Maximum = 800, Increment = 8, Value = settings.RevealDiameterPx, Dock = DockStyle.Fill };
        _shape = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        _shape.Items.AddRange([
            new ShapeChoice(RevealShape.Circle, UiText.Text(settings.Language, "circle")),
            new ShapeChoice(RevealShape.Rectangle, UiText.Text(settings.Language, "rectangle")),
            new ShapeChoice(RevealShape.RoundedRectangle, UiText.Text(settings.Language, "roundedRectangle"))]);
        SelectShape(settings.RevealShape);
        _peekKey = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        _peekKey.Items.AddRange([new KeyChoice(0x12, UiText.PeekKeyName(settings.Language, 0x12)), new KeyChoice(0x20, UiText.PeekKeyName(settings.Language, 0x20)), new KeyChoice(0x10, UiText.PeekKeyName(settings.Language, 0x10))]);
        SelectKey(settings.PeekVirtualKey);
        _restoreOnExit = new CheckBox { Text = UiText.Text(settings.Language, "restoreOnExit"), Checked = settings.RestoreOnExit, AutoSize = true };
        _startWithWindows = new CheckBox { Text = UiText.Text(settings.Language, "startWindows"), Checked = settings.StartWithWindows, AutoSize = true };
        _logLevel = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        _logLevel.Items.AddRange(Enum.GetValues<LogLevel>().Cast<object>().ToArray());
        _logLevel.SelectedItem = settings.MinimumLogLevel;
        _language = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
        _language.Items.AddRange([new LanguageChoice(UiLanguage.Chinese, "中文"), new LanguageChoice(UiLanguage.English, "English")]);
        _language.SelectedItem = settings.Language == UiLanguage.Chinese ? _language.Items[0] : _language.Items[1];

        AddRow(layout, 0, UiText.Text(settings.Language, "diameter"), _diameter);
        AddRow(layout, 1, UiText.Text(settings.Language, "shape"), _shape);
        AddRow(layout, 2, UiText.Text(settings.Language, "peekKey"), _peekKey);
        AddRow(layout, 3, string.Empty, _restoreOnExit, 2);
        AddRow(layout, 4, string.Empty, _startWithWindows, 2);
        AddRow(layout, 5, UiText.Text(settings.Language, "logLevel"), _logLevel);
        AddRow(layout, 6, settings.Language == UiLanguage.Chinese ? "界面语言" : "Language", _language);

        var buttons = new FlowLayoutPanel { FlowDirection = FlowDirection.RightToLeft, Dock = DockStyle.Fill };
        var cancel = new Button { Text = UiText.Text(settings.Language, "cancel"), DialogResult = DialogResult.Cancel, AutoSize = true };
        var save = new Button { Text = UiText.Text(settings.Language, "save"), DialogResult = DialogResult.OK, AutoSize = true };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(save);
        layout.Controls.Add(buttons, 0, 7);
        layout.SetColumnSpan(buttons, 2);
        AcceptButton = save;
        CancelButton = cancel;
    }

    public AppSettings GetSettings(AppSettings current)
    {
        var choice = _peekKey.SelectedItem as KeyChoice ?? new KeyChoice(0x12, "Alt");
        var shape = _shape.SelectedItem is ShapeChoice selectedShape ? selectedShape.Shape : current.RevealShape;
        var logLevel = _logLevel.SelectedItem is LogLevel selectedLevel ? selectedLevel : current.MinimumLogLevel;
        var language = _language.SelectedItem is LanguageChoice selectedLanguage ? selectedLanguage.Language : current.Language;
        return current with
        {
            RevealDiameterPx = (int)_diameter.Value,
            RevealShape = shape,
            Language = language,
            PeekVirtualKey = choice.VirtualKey,
            RestoreOnExit = _restoreOnExit.Checked,
            StartWithWindows = _startWithWindows.Checked,
            MinimumLogLevel = logLevel
        };
    }

    private void SelectKey(int virtualKey)
    {
        for (var i = 0; i < _peekKey.Items.Count; i++)
        {
            if (_peekKey.Items[i] is KeyChoice choice && choice.VirtualKey == virtualKey)
            {
                _peekKey.SelectedIndex = i;
                return;
            }
        }

        _peekKey.SelectedIndex = 0;
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

    private static void AddRow(TableLayoutPanel layout, int row, string label, Control control, int columnSpan = 1)
    {
        if (!string.IsNullOrEmpty(label))
        {
            layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            layout.Controls.Add(control, 1, row);
        }
        else
        {
            layout.Controls.Add(control, 0, row);
            layout.SetColumnSpan(control, columnSpan);
        }
    }

    private sealed record KeyChoice(int VirtualKey, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record ShapeChoice(RevealShape Shape, string Name)
    {
        public override string ToString() => Name;
    }

    private sealed record LanguageChoice(UiLanguage Language, string Name)
    {
        public override string ToString() => Name;
    }
}
