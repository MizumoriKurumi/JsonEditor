namespace JsonEditor;

public partial class MainForm
{
    private readonly ToolStripDropDownButton languageButton = new();
    private readonly ToolStripMenuItem chineseItem = new("简体中文");
    private readonly ToolStripMenuItem englishItem = new("English");
    private string statusKey = "Ready";
    private object[] statusArguments = Array.Empty<object>();

    private void SetupLanguageMenu()
    {
        languageButton.Alignment = ToolStripItemAlignment.Right;
        languageButton.DisplayStyle = ToolStripItemDisplayStyle.Text;
        languageButton.DropDownItems.AddRange(new ToolStripItem[] { chineseItem, englishItem });
        chineseItem.Click += (_, _) => ChangeLanguage("zh-CN");
        englishItem.Click += (_, _) => ChangeLanguage("en");
        toolStrip.Items.Add(languageButton);
    }

    private void ChangeLanguage(string code)
    {
        Localization.SetLanguage(code);
        ApplyLanguage();
        try { Localization.Save(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            MessageBox.Show(this, Localization.Format("SettingsSaveFailed", ex.Message),
                Localization.Get("Error"), MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void ApplyLanguage()
    {
        SuspendLayout();
        try
        {
            Text = Localization.Get("AppTitle");
            newButton.Text = Localization.Get("New");
            openButton.Text = Localization.Get("Open");
            saveButton.Text = Localization.Get("Save");
            saveAsButton.Text = Localization.Get("SaveAs");
            addButton.Text = Localization.Get("Add");
            exitButton.Text = Localization.Get("Exit");
            languageButton.Text = Localization.Get("Language");
            chineseItem.Checked = Localization.Language.Name == "zh-CN";
            englishItem.Checked = Localization.Language.Name == "en";
            dataGridView.Columns["PathColumn"].HeaderText = Localization.Get("ColumnKey");
            dataGridView.Columns["ValueColumn"].HeaderText = Localization.Get("ColumnValue");
            dataGridView.Columns["TypeColumn"].HeaderText = Localization.Get("ColumnType");
            statusLabel.Text = Localization.Format(statusKey, statusArguments);
        }
        finally { ResumeLayout(true); }
    }

    private void SetStatus(string key, params object[] args)
    {
        statusKey = key;
        statusArguments = args;
        statusLabel.Text = Localization.Format(key, args);
    }
}
