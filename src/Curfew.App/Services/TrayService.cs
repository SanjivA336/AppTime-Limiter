using System.Drawing;
using System.Windows.Forms;
using Curfew.Core.Enforcement;

namespace Curfew.App.Services;

/// <summary>
/// The notification-area (tray) icon: lets the app live in the background after its window closes,
/// and delivers messages as ordinary Windows notifications.
/// </summary>
public sealed class TrayService : INotifier, IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly Icon _appIcon;

    public TrayService(Action showWindow, Action exit)
    {
        var resource = System.Windows.Application.GetResourceStream(new Uri("pack://application:,,,/Assets/app.ico"));
        _appIcon = new Icon(resource.Stream);

        var menu = new ContextMenuStrip();
        menu.Items.Add("Open Curfew", null, (_, _) => showWindow());
        menu.Items.Add(new ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => exit());

        _icon = new NotifyIcon
        {
            Icon = _appIcon,
            Text = "Curfew",
            ContextMenuStrip = menu,
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == MouseButtons.Left)
            {
                showWindow();
            }
        };
    }

    public void Notify(string title, string message)
    {
        // Balloon tips are shown by Windows 10/11 as regular toast notifications.
        _icon.ShowBalloonTip(8000, title, message, ToolTipIcon.Info);
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
        _appIcon.Dispose();
    }
}
