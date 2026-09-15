using SenheiserControl.Services;

namespace SenheiserControl;

internal sealed class TrayApplicationContext : ApplicationContext
{
    private static readonly string IconPath = Path.Combine(AppContext.BaseDirectory, "Assets", "app.ico");

    private readonly HeadsetService _headsetService = new();
    private readonly NotifyIcon _trayIcon;
    private MainForm? _mainForm;

    public TrayApplicationContext()
    {
        _headsetService.StartAutoConnect();

        _trayIcon = new NotifyIcon
        {
            Icon = new Icon(IconPath),
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

        var startupItem = new ToolStripMenuItem("Start with Windows")
        {
            CheckOnClick = true,
            Checked = StartupManager.IsEnabled
        };
        startupItem.Click += (_, _) =>
        {
            if (startupItem.Checked) StartupManager.Enable(); else StartupManager.Disable();
        };
        menu.Items.Add(startupItem);

        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => ExitApplication());
        return menu;
    }

    private void ShowMainForm()
    {
        _mainForm ??= new MainForm(_headsetService);
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
        _ = _headsetService.DisposeAsync();
        Application.Exit();
    }
}
