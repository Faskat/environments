using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using Environments.Core;
using Microsoft.Win32;
using Forms = System.Windows.Forms;
using Drawing = System.Drawing;

namespace Environments.UI;

public sealed class TrayIcon : IDisposable
{
    readonly Forms.NotifyIcon _icon;

    public TrayIcon()
    {
        _icon = new Forms.NotifyIcon
        {
            Icon = LoadIcon(),
            Text = "Environments",
            Visible = true,
        };
        _icon.MouseClick += (_, e) =>
        {
            if (e.Button == Forms.MouseButtons.Left) App.Instance.ShowMain();
        };
        SystemEvents.UserPreferenceChanged += OnThemeChanged;
        Rebuild();
    }

    /// <summary>The taskbar can be light while apps are dark, so the tray icon follows the taskbar setting.</summary>
    static bool TaskbarIsLight()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            return key?.GetValue("SystemUsesLightTheme") is int v && v != 0;
        }
        catch { return false; }
    }

    static Drawing.Icon LoadIcon()
    {
        var name = TaskbarIsLight() ? "tray-light.ico" : "tray-dark.ico";
        var res = Application.GetResourceStream(new Uri($"pack://application:,,,/Assets/{name}"));
        return res != null ? new Drawing.Icon(res.Stream, Forms.SystemInformation.SmallIconSize) : Drawing.SystemIcons.Application;
    }

    void OnThemeChanged(object sender, UserPreferenceChangedEventArgs e)
    {
        if (e.Category != UserPreferenceCategory.General) return;
        var old = _icon.Icon;
        _icon.Icon = LoadIcon();
        old?.Dispose();
    }

    public void Rebuild()
    {
        var menu = new Forms.ContextMenuStrip { Renderer = new DarkRenderer(), Padding = new Forms.Padding(4) };
        menu.Font = new Drawing.Font("Segoe UI", 10f);
        int px = (int)Math.Round(16 * menu.DeviceDpi / 96.0);
        menu.ImageScalingSize = new Drawing.Size(px, px);

        foreach (var p in App.Config.Presets)
        {
            var preset = p;
            var color = HexToBrushConverter.Parse(p.Color) is SolidColorBrush b ? b.Color : Colors.Gray;
            var item = new Forms.ToolStripMenuItem(p.Name, Image(p.Icon, color, px)) { ShortcutKeyDisplayString = p.Hotkey };
            item.Click += (_, _) => App.Instance.RunPreset(preset, App.Config.Settings.HotkeyShowsPreview);
            menu.Items.Add(item);
        }
        var muted = Color.FromRgb(0x8D, 0x91, 0x9C);
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Вернуть как было", Image("undo", muted, px), (_, _) => App.Instance.Undo());
        menu.Items.Add("Закрыть текущий рабочий стол", Image("close", muted, px), async (_, _) => await VirtualDesktop.CloseCurrent());
        menu.Items.Add("Открыть", Image("app", muted, px), (_, _) => App.Instance.ShowMain());
        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Выход", Image("close", muted, px), (_, _) => App.Instance.Quit());

        var old = _icon.ContextMenuStrip;
        _icon.ContextMenuStrip = menu;
        old?.Dispose();
    }

    static Drawing.Image? Image(string icon, Color color, int px)
    {
        try { return Drawing.Image.FromStream(new MemoryStream(Icons.RenderPng(icon, color, px))); }
        catch { return null; }
    }

    public void Notify(string title, string text)
    {
        if (!App.Config.Settings.Notifications) return;
        _icon.ShowBalloonTip(4000, title, text, Forms.ToolTipIcon.None);
    }

    public void Dispose()
    {
        SystemEvents.UserPreferenceChanged -= OnThemeChanged;
        _icon.Visible = false;
        _icon.Dispose();
    }

    /// <summary>Menu colours from design/tokens.csv: surface, surface-2 on hover, border, text, text-2.</summary>
    sealed class DarkRenderer : Forms.ToolStripProfessionalRenderer
    {
        static readonly Drawing.Color Bg = Drawing.Color.FromArgb(0x1D, 0x1F, 0x25);
        static readonly Drawing.Color Hover = Drawing.Color.FromArgb(0x26, 0x29, 0x32);
        static readonly Drawing.Color Line = Drawing.Color.FromArgb(0x30, 0x33, 0x3D);
        static readonly Drawing.Color Fg = Drawing.Color.FromArgb(0xEC, 0xED, 0xEF);
        static readonly Drawing.Color Muted = Drawing.Color.FromArgb(0x8D, 0x91, 0x9C);

        public DarkRenderer() : base(new DarkColors()) { RoundedEdges = false; }

        protected override void OnRenderItemText(Forms.ToolStripItemTextRenderEventArgs e)
        {
            e.TextColor = e.Text == (e.Item as Forms.ToolStripMenuItem)?.ShortcutKeyDisplayString ? Muted : Fg;
            base.OnRenderItemText(e);
        }

        protected override void OnRenderMenuItemBackground(Forms.ToolStripItemRenderEventArgs e)
        {
            using var b = new Drawing.SolidBrush(e.Item.Selected ? Hover : Bg);
            e.Graphics.FillRectangle(b, new Drawing.Rectangle(Drawing.Point.Empty, e.Item.Size));
        }

        protected override void OnRenderSeparator(Forms.ToolStripSeparatorRenderEventArgs e)
        {
            using var p = new Drawing.Pen(Line);
            int y = e.Item.Height / 2;
            e.Graphics.DrawLine(p, 8, y, e.Item.Width - 8, y);
        }

        sealed class DarkColors : Forms.ProfessionalColorTable
        {
            public override Drawing.Color ToolStripDropDownBackground => Bg;
            public override Drawing.Color MenuBorder => Line;
            public override Drawing.Color MenuItemBorder => Hover;
            public override Drawing.Color ImageMarginGradientBegin => Bg;
            public override Drawing.Color ImageMarginGradientMiddle => Bg;
            public override Drawing.Color ImageMarginGradientEnd => Bg;
        }
    }
}
