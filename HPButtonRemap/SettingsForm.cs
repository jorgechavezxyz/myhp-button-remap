namespace HPButtonRemap;

internal sealed class SettingsForm : Form
{
    private readonly ComboBox _type = new() { DropDownStyle = ComboBoxStyle.DropDownList, Dock = DockStyle.Fill };
    private readonly TextBox _target = new() { Dock = DockStyle.Fill };
    private readonly Button _browse = new() { Text = "Browse…", AutoSize = true };
    private readonly Label _error = new() { AutoSize = true, ForeColor = SystemColors.WindowText, MaximumSize = new Size(450, 0) };
    private readonly Action<ButtonAction> _save;
    private readonly ActionExecutor _executor;
    private readonly Button _test = new() { Text = "&Test action", AutoSize = true };

    public SettingsForm(ButtonAction? current, ActionExecutor executor, Action<ButtonAction> save)
    {
        _save = save;
        _executor = executor;
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
        buttons.Controls.Add(_test);
        layout.Controls.Add(buttons, 0, 5);
        layout.SetColumnSpan(buttons, 2);
        AcceptButton = saveButton;
        CancelButton = cancel;

        _type.Items.AddRange(new object[] { "Open an app or file", "Open a website", "Send a keyboard shortcut" });
        _type.SelectedIndexChanged += (_, _) =>
        {
            _browse.Enabled = _type.SelectedIndex == 0;
            _target.PlaceholderText = _type.SelectedIndex switch
            {
                1 => "https://example.com",
                2 => "Win+Shift+S",
                _ => @"C:\Path\to\file"
            };
            _target.Clear();
            _error.Text = _type.SelectedIndex == 2 ? KeyboardShortcut.FormatHelp : "";
        };
        _type.SelectedIndex = current?.Type switch
        {
            ActionType.OpenWebsite => 1,
            ActionType.SendShortcut => 2,
            _ => 0
        };
        _target.Text = current?.Type switch
        {
            ActionType.OpenWebsite => current.OpenWebsite ?? "",
            ActionType.OpenFile => current.FilePath ?? "",
            ActionType.SendShortcut => current.Shortcut ?? "",
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
        _test.Click += async (_, _) => await TestActionAsync();
    }

    private ButtonAction? ReadAction()
    {
        string target = _target.Text.Trim();
        _error.Text = "";
        if (_type.SelectedIndex == 1)
        {
            if (!ButtonAction.TryGetWebsite(target, out var website) &&
                (target.Contains(':') || target.Contains('\\') || !target.Contains('.') ||
                 !ButtonAction.TryGetWebsite("https://" + target, out website)))
            {
                _error.Text = "Enter a valid HTTP or HTTPS website address.";
                return null;
            }
            return new ButtonAction { Type = ActionType.OpenWebsite, OpenWebsite = website!.AbsoluteUri };
        }

        if (_type.SelectedIndex == 2)
        {
            if (KeyboardShortcut.TryParse(target, out _))
                return new ButtonAction { Type = ActionType.SendShortcut, Shortcut = target };
            _error.Text = KeyboardShortcut.FormatHelp;
            return null;
        }

        if (Path.IsPathFullyQualified(target) && File.Exists(target))
            return new ButtonAction { Type = ActionType.OpenFile, FilePath = target };

        _error.Text = "Choose an existing app or file.";
        return null;
    }

    private async Task TestActionAsync()
    {
        if (ReadAction() is not { } action)
            return;

        _test.Enabled = false;
        try
        {
            if (action.Type == ActionType.SendShortcut)
            {
                _error.Text = "Switch to the window that should receive the shortcut. Sending in 3 seconds.";
                await Task.Delay(TimeSpan.FromSeconds(3));
                if (!Visible || !IsHandleCreated)
                    return;
                if (ContainsFocus)
                {
                    _error.Text = "Switch to another window before the shortcut is sent.";
                    return;
                }
            }

            string? error = await Task.Run(() => _executor.ExecuteAction(action));
            if (Visible && IsHandleCreated)
                _error.Text = error ?? "Test action sent.";
        }
        finally
        {
            if (!IsDisposed)
                _test.Enabled = true;
        }
    }

    private void SaveAction()
    {
        if (ReadAction() is not { } action)
            return;

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
