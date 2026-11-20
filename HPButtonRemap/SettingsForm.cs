namespace HPButtonRemap;

internal sealed class SettingsForm : Form
{
    private readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox _target = new() { Dock = DockStyle.Fill };
    private readonly Button _browse = new() { Text = "Browse…", AutoSize = true };
    private readonly Label _error = new() { AutoSize = true, ForeColor = SystemColors.WindowText, MaximumSize = new Size(450, 0) };
    private readonly Action<ButtonAction> _save;

    public SettingsForm(ButtonAction? current, Action<ButtonAction> save)
    {
        _save = save;
        Text = "HP Button Remap";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        AutoScaleMode = AutoScaleMode.Font;
        ClientSize = new Size(500, 245);

        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(16), ColumnCount = 2 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        Controls.Add(layout);

        var typeLabel = new Label { Text = "&Action", AutoSize = true };
        layout.Controls.Add(typeLabel, 0, 0);
        layout.Controls.Add(_type, 0, 1);
        layout.SetColumnSpan(_type, 2);

        var targetLabel = new Label { Text = "&Target", AutoSize = true, Margin = new Padding(3, 10, 3, 0) };
        layout.Controls.Add(targetLabel, 0, 2);
        layout.Controls.Add(_target, 0, 3);
        layout.Controls.Add(_browse, 1, 3);
        layout.Controls.Add(_error, 0, 4);
        layout.SetColumnSpan(_error, 2);
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, FlowDirection = FlowDirection.RightToLeft };
        var saveButton = new Button { Text = "&Save", AutoSize = true };
        var cancel = new Button { Text = "Cancel", AutoSize = true, DialogResult = DialogResult.Cancel };
        buttons.Controls.Add(cancel);
        buttons.Controls.Add(saveButton);
        layout.Controls.Add(buttons, 0, 5);
        layout.SetColumnSpan(buttons, 2);
        AcceptButton = saveButton;
        CancelButton = cancel;

        _type.Items.AddRange(new object[] { "Open an app or file", "Open a website" });
        _type.SelectedIndexChanged += (_, _) =>
        {
            _browse.Enabled = _type.SelectedIndex == 0;
            _target.PlaceholderText = _browse.Enabled ? @"C:\Path\to\file" : "https://example.com";
            _target.Clear();
        };
        _type.SelectedIndex = current?.Type == ActionType.OpenWebsite ? 1 : 0;
        _target.Text = current?.Type switch
        {
            ActionType.OpenWebsite => current.OpenWebsite ?? "",
            ActionType.OpenFile => current.FilePath ?? "",
            _ => ""
        };
        if (current?.Type == ActionType.RunCommand)
            _error.Text = "A custom command is configured. Saving replaces it.";

        _browse.Click += (_, _) =>
        {
            using var picker = new OpenFileDialog { CheckFileExists = true, Filter = "All files (*.*)|*.*" };
            if (picker.ShowDialog(this) == DialogResult.OK)
                _target.Text = picker.FileName;
        };
        saveButton.Click += (_, _) => SaveAction();
    }

    private void SaveAction()
    {
        string target = _target.Text.Trim();
        ButtonAction action;
        if (_type.SelectedIndex == 1)
        {
            if (!ButtonAction.TryGetWebsite(target, out var website) &&
                (target.Contains(':') || target.Contains('\\') || !target.Contains('.') ||
                 !ButtonAction.TryGetWebsite("https://" + target, out website)))
            {
                _error.Text = "Enter a valid HTTP or HTTPS website address.";
                return;
            }
            action = new ButtonAction { Type = ActionType.OpenWebsite, OpenWebsite = website!.AbsoluteUri };
        }
        else
        {
            if (!Path.IsPathFullyQualified(target) || !File.Exists(target))
            {
                _error.Text = "Choose an existing app or file.";
                return;
            }
            action = new ButtonAction { Type = ActionType.OpenFile, FilePath = target };
        }

        try
        {
            _save(action);
            DialogResult = DialogResult.OK;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _error.Text = "Could not save settings. " + ex.Message;
        }
    }
}
