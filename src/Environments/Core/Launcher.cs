using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;

namespace Environments.Core;

public static class Launcher
{
    static readonly Regex UriLike = new(@"^[a-zA-Z][a-zA-Z0-9+.\-]+:(//)?", RegexOptions.Compiled);

    /// <summary>True for "steam://open/main" or "spotify:", false for "C:\...".</summary>
    public static bool IsUri(string s) => UriLike.IsMatch(s) && !Regex.IsMatch(s, @"^[a-zA-Z]:[\\/]");

    /// <summary>Expands env vars and resolves * in path segments (picks the last match, usually the newest version).</summary>
    public static string? ResolvePath(string raw)
    {
        var path = Environment.ExpandEnvironmentVariables(raw.Trim().Trim('"'));
        if (!path.Contains('*')) return File.Exists(path) ? path : null;

        var parts = path.Split('\\');
        var current = parts[0] + "\\";
        for (int i = 1; i < parts.Length; i++)
        {
            var seg = parts[i];
            bool last = i == parts.Length - 1;
            if (!seg.Contains('*'))
            {
                current = Path.Combine(current, seg);
                continue;
            }
            if (!Directory.Exists(current)) return null;
            var matches = last ? Directory.GetFiles(current, seg) : Directory.GetDirectories(current, seg);
            var pick = matches.OrderBy(m => m, StringComparer.OrdinalIgnoreCase).LastOrDefault();
            if (pick == null) return null;
            current = pick;
        }
        return File.Exists(current) ? current : null;
    }

    /// <summary>Tries each launch target in order. Returns null on success or the error text.</summary>
    public static string? Start(AppDef app)
    {
        if (app.Launch.Count == 0) return "не указано, как запускать";
        string? lastError = null;
        foreach (var t in app.Launch)
        {
            if (IsUri(t.Path))
            {
                try
                {
                    Process.Start(new ProcessStartInfo(t.Path) { UseShellExecute = true });
                    return null;
                }
                catch (Exception e) { lastError = e.Message; continue; }
            }
            var exe = ResolvePath(t.Path);
            if (exe == null) { lastError ??= "файл не найден: " + t.Path; continue; }
            var err = StartExe(exe, t.Args);
            if (err == null) return null;
            lastError = err;
        }
        return lastError;
    }

    public static string? StartExe(string exe, string args = "")
    {
        try
        {
            Process.Start(new ProcessStartInfo(exe, args)
            {
                UseShellExecute = true,
                // OBS and friends look for their data next to the exe.
                WorkingDirectory = Path.GetDirectoryName(exe) ?? "",
            });
            return null;
        }
        catch (Exception e) { return e.Message; }
    }
}
