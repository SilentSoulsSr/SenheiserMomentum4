namespace SenheiserControl;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private readonly NotifyIcon _trayIcon;
    private MainForm? _mainForm;

    public TrayApplicationContext()
    {
        _trayIcon = new NotifyIcon
        {
            Icon = SystemIcons.Application,
            Text = "Senheiser Momentum 4 Control",
            Visible = true,
            ContextMenuStrip = BuildMenu()
        };
        _trayIcon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                ShowMainForm();
            }
        };
    }

    private ContextMenuStrip BuildMenu()
    {
        var menu = new ContextMenuStrip();
        menu.Items.Add("Open", null, (_, _) => ShowMainForm());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());
        return menu;
    }

    private void ShowMainForm()
    {
        _mainForm ??= new MainForm();
        if (!_mainForm.Visible)
        {
            _mainForm.Show();
        }
        _mainForm.WindowState = FormWindowState.Normal;
        _mainForm.Activate();
    }

    private void ExitApplication()
    {
        _trayIcon.Visible = false;
        _mainForm?.AllowClose();
        _mainForm?.Close();
        Application.Exit();
    }
}
