using Microsoft.AspNetCore.Components.WebView.WindowsForms;
using Microsoft.Extensions.DependencyInjection;
using SenheiserControl.Components;
using SenheiserControl.Services;

namespace SenheiserControl;

public sealed class MainForm : Form
{
    private readonly BlazorWebView _blazorWebView;
    private bool _allowClose;

    public MainForm()
    {
        Text = "Senheiser Momentum 4 Control";
        Width = 480;
        Height = 640;
        StartPosition = FormStartPosition.CenterScreen;

        var services = new ServiceCollection();
        services.AddWindowsFormsBlazorWebView();
        services.AddSingleton<HeadsetService>();

        _blazorWebView = new BlazorWebView
        {
            Dock = DockStyle.Fill,
            HostPage = "wwwroot/index.html",
            Services = services.BuildServiceProvider()
        };
        _blazorWebView.RootComponents.Add<Main>("#app");

        Controls.Add(_blazorWebView);

        FormClosing += OnFormClosing;
    }

    public void AllowClose() => _allowClose = true;

    private void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        if (_allowClose)
        {
            return;
        }

        e.Cancel = true;
        Hide();
    }
}
