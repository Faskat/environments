using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Environments.UI;

/// <summary>The line icon set. Icons are referenced by name ("game", "code"…); anything else is drawn as text (old emoji).</summary>
public static partial class Icons
{
    static readonly Dictionary<string, (Geometry? Stroke, Geometry? Fill)> Parsed = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Icons offered in the picker for presets, groups and apps (UI-only glyphs are left out).</summary>
    public static readonly string[] Pickable =
    {
        "game", "stream", "record", "study", "school", "code", "terminal", "cube", "draw", "photo", "video",
        "chill", "music", "headphones", "messenger", "mail", "browser", "launcher", "work", "focus", "bolt",
        "shield", "broom", "download", "app",
    };

    /// <summary>Emoji used by older configs, mapped to the icon that replaced them.</summary>
    static readonly Dictionary<string, string> FromEmoji = new()
    {
        ["🎮"] = "game", ["🕹"] = "game", ["⏺"] = "record", ["📚"] = "study", ["⌨"] = "code", ["🧊"] = "cube",
        ["🎧"] = "headphones", ["🧹"] = "broom", ["💬"] = "messenger", ["✈"] = "messenger", ["🌐"] = "browser",
        ["🦊"] = "browser", ["🦁"] = "browser", ["🎵"] = "music", ["📓"] = "draw", ["🏫"] = "school",
        ["🎬"] = "video", ["⛏"] = "game", ["✨"] = "bolt", ["🪟"] = "app", ["📁"] = "work",
    };

    public static bool Has(string? name) => name != null && Data.ContainsKey(name);

    /// <summary>Returns the icon name for an old emoji, or the value unchanged.</summary>
    public static string Migrate(string icon)
    {
        var key = icon.Trim().Replace("️", "");
        return FromEmoji.TryGetValue(key, out var name) ? name : icon;
    }

    public static (Geometry? Stroke, Geometry? Fill) Get(string name)
    {
        if (Parsed.TryGetValue(name, out var g)) return g;
        if (!Data.TryGetValue(name, out var d)) return (null, null);
        g = (Parse(d.Stroke), Parse(d.Fill));
        Parsed[name] = g;
        return g;
    }

    static Geometry? Parse(string data)
    {
        if (data.Length == 0) return null;
        var g = Geometry.Parse(data);
        g.Freeze();
        return g;
    }

    /// <summary>Renders an icon to PNG bytes, for WinForms menus.</summary>
    public static byte[] RenderPng(string name, Color color, int px)
    {
        var glyph = new Glyph { Icon = name, Foreground = new SolidColorBrush(color), Width = px, Height = px };
        glyph.Measure(new Size(px, px));
        glyph.Arrange(new Rect(0, 0, px, px));
        var bmp = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        bmp.Render(glyph);
        var enc = new PngBitmapEncoder();
        enc.Frames.Add(BitmapFrame.Create(bmp));
        using var ms = new MemoryStream();
        enc.Save(ms);
        return ms.ToArray();
    }
}

/// <summary>Draws one icon from <see cref="Icons"/> in the inherited foreground colour.</summary>
public class Glyph : FrameworkElement
{
    public static readonly DependencyProperty IconProperty = DependencyProperty.Register(
        nameof(Icon), typeof(string), typeof(Glyph),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

    public static readonly DependencyProperty ForegroundProperty = TextElement.ForegroundProperty.AddOwner(
        typeof(Glyph), new FrameworkPropertyMetadata(Brushes.Gray,
            FrameworkPropertyMetadataOptions.Inherits | FrameworkPropertyMetadataOptions.AffectsRender));

    public string? Icon { get => (string?)GetValue(IconProperty); set => SetValue(IconProperty, value); }
    public Brush Foreground { get => (Brush)GetValue(ForegroundProperty); set => SetValue(ForegroundProperty, value); }

    public Glyph()
    {
        Width = Height = 16;
        SnapsToDevicePixels = true;
    }

    protected override void OnRender(DrawingContext dc)
    {
        var size = Math.Min(ActualWidth, ActualHeight);
        if (size <= 0 || string.IsNullOrEmpty(Icon)) return;
        var (stroke, fill) = Icons.Get(Icon);

        if (stroke == null && fill == null)
        {
            // Not an icon name: a custom emoji or letter typed by the user.
            var text = new FormattedText(Icon, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI Emoji"), size * 0.8, Foreground, VisualTreeHelper.GetDpi(this).PixelsPerDip);
            dc.DrawText(text, new Point((ActualWidth - text.Width) / 2, (ActualHeight - text.Height) / 2));
            return;
        }

        dc.PushTransform(new TranslateTransform((ActualWidth - size) / 2, (ActualHeight - size) / 2));
        dc.PushTransform(new ScaleTransform(size / 24, size / 24));
        if (fill != null) dc.DrawGeometry(Foreground, null, fill);
        if (stroke != null)
        {
            var pen = new Pen(Foreground, 1.75) { StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round, LineJoin = PenLineJoin.Round };
            dc.DrawGeometry(null, pen, stroke);
        }
        dc.Pop();
        dc.Pop();
    }
}

/// <summary>A small popup grid of icons; calls back with the picked name.</summary>
public static class IconPicker
{
    public static void Show(UIElement anchor, string? current, Action<string> picked)
    {
        var panel = new WrapPanel { Width = 5 * 40 };
        var popup = new Popup
        {
            PlacementTarget = anchor, Placement = PlacementMode.Bottom, StaysOpen = false, AllowsTransparency = true,
            PopupAnimation = PopupAnimation.Fade, VerticalOffset = 6,
        };
        foreach (var name in Icons.Pickable)
        {
            var btn = new Button
            {
                Width = 36, Height = 36, Margin = new Thickness(2), Padding = new Thickness(0), ToolTip = name,
                Style = (Style)Application.Current.FindResource(name == current ? "IconButtonSelected" : "IconButton"),
                Content = new Glyph { Icon = name, Width = 20, Height = 20 },
            };
            btn.Click += (_, _) => { popup.IsOpen = false; picked(name); };
            panel.Children.Add(btn);
        }
        popup.Child = new Border
        {
            Style = (Style)Application.Current.FindResource("PopupCard"),
            Child = panel,
        };
        popup.IsOpen = true;
    }
}
