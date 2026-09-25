using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Data;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Environments.Core;
using Microsoft.Win32;

namespace Environments.UI;

public static class IconCache
{
    static readonly Dictionary<string, ImageSource?> Cache = new(StringComparer.OrdinalIgnoreCase);

    public static ImageSource? ForPath(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        if (Cache.TryGetValue(path, out var cached)) return cached;
        ImageSource? result = null;
        try
        {
            if (File.Exists(path))
            {
                using var icon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                if (icon != null)
                {
                    var src = Imaging.CreateBitmapSourceFromHIcon(icon.Handle, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
                    src.Freeze();
                    result = src;
                }
            }
        }
        catch { }
        return Cache[path] = result;
    }

    /// <summary>Icon of the first launch target that exists on disk.</summary>
    public static ImageSource? ForApp(AppDef app)
    {
        foreach (var t in app.Launch)
        {
            if (Launcher.IsUri(t.Path)) continue;
            var exe = Launcher.ResolvePath(t.Path);
            // Discord's Update.exe has a generic icon; the real one sits next to it.
            if (exe != null && Path.GetFileName(exe).Equals("Update.exe", StringComparison.OrdinalIgnoreCase))
            {
                var real = Directory.GetFiles(Path.GetDirectoryName(exe)!, "*.exe", SearchOption.AllDirectories)
                    .FirstOrDefault(f => !f.EndsWith("Update.exe", StringComparison.OrdinalIgnoreCase)
                                         && Path.GetFileNameWithoutExtension(f).Equals(app.Name, StringComparison.OrdinalIgnoreCase));
                if (real != null) exe = real;
            }
            if (ForPath(exe) is { } img) return img;
        }
        return null;
    }
}

public class PathToIconConverter : IValueConverter
{
    public object? Convert(object value, Type targetType, object parameter, CultureInfo culture) => IconCache.ForPath(value as string);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public class HexToBrushConverter : IValueConverter
{
    public static Brush Parse(string? hex, Brush? fallback = null)
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(hex) && ColorConverter.ConvertFromString(hex) is Color c)
            {
                var b = new SolidColorBrush(c);
                b.Freeze();
                return b;
            }
        }
        catch { }
        return fallback ?? Brushes.SlateBlue;
    }

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => Parse(value as string);
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public class NullToCollapsedConverter : IValueConverter
{
    public bool Invert { get; set; }
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        (value == null || value is string { Length: 0 }) ^ Invert ? Visibility.Collapsed : Visibility.Visible;
    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}

public static class Autostart
{
    const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    const string Name = "Environments";

    static string Command => $"\"{Environment.ProcessPath}\" --hidden";

    public static bool IsEnabled
    {
        get
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunKey);
            return key?.GetValue(Name) is string s && s.Length > 0;
        }
        set
        {
            using var key = Registry.CurrentUser.CreateSubKey(RunKey);
            if (value) key.SetValue(Name, Command);
            else key.DeleteValue(Name, false);
        }
    }
}
