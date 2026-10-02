using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;

namespace Environments.Core;

internal class LayoutMonitor
{
    public IntPtr Handle { get; init; }
    public int Index { get; init; }
    public string Device { get; init; } = "";
    public Native.RECT Bounds { get; init; }
}

/// <summary>
/// Saves where the preset's apps sit right now (monitor, rect, maximized…) and puts them back there
/// after a preset has launched them.
/// </summary>
public static class LayoutManager
{
    static readonly TimeSpan WindowWait = TimeSpan.FromSeconds(10);

    /// <summary>Monitors in their enumeration order; index 0 is the one Windows calls primary.</summary>
    internal static List<LayoutMonitor> EnumerateMonitors()
    {
        var list = new List<LayoutMonitor>();
        Native.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, (IntPtr hMon, IntPtr _, ref Native.RECT _, IntPtr _) =>
        {
            var info = new Native.MONITORINFOEX { CbSize = Marshal.SizeOf<Native.MONITORINFOEX>() };
            if (Native.GetMonitorInfo(hMon, ref info))
                list.Add(new LayoutMonitor { Handle = hMon, Index = list.Count, Device = info.SzDevice, Bounds = info.RcMonitor });
            return true;
        }, IntPtr.Zero);
        return list;
    }

    /// <summary>
    /// Remembers where the preset's apps (keep + launch) sit right now and returns one spot per app.
    /// Only apps with a matching definition are captured; the last say in duplicates is the definition order.
    /// </summary>
    public static List<WindowPlacement> Capture(Config cfg, Preset preset)
    {
        var wanted = cfg.ResolveApps(preset.Keep.Concat(preset.Launch)).Select(a => a.Id).ToHashSet();
        var monitors = EnumerateMonitors();
        var byId = new Dictionary<string, WindowPlacement>();
        foreach (var app in WindowScanner.Scan().Apps)
        {
            if (app.Pid == Environment.ProcessId) continue;
            var def = cfg.AppFor(app);
            if (def == null || !wanted.Contains(def.Id) || byId.ContainsKey(def.Id)) continue;

            var wp = new Native.WINDOWPLACEMENT();
            wp.Length = (uint)Marshal.SizeOf<Native.WINDOWPLACEMENT>();
            if (!Native.GetWindowPlacement(app.Windows[0], ref wp)) continue;
            var r = wp.RcNormalPosition;
            // A freshly minimized window can report the phantom -32000 rect instead of a real restore spot.
            if (r.Left < -20000 || r.Width < 80 || r.Height < 60) continue;

            var mon = MonitorOf(monitors, Native.MonitorFromPoint(
                new Native.POINT((r.Left + r.Right) / 2, (r.Top + r.Bottom) / 2), Native.MONITOR_DEFAULTTONEAREST));
            if (mon == null) continue;

            byId[def.Id] = new WindowPlacement
            {
                AppId = def.Id,
                Monitor = mon.Index,
                MonitorDevice = mon.Device,
                Left = r.Left - mon.Bounds.Left,
                Top = r.Top - mon.Bounds.Top,
                Width = r.Width,
                Height = r.Height,
                State = wp.ShowCmd switch
                {
                    Native.SW_SHOWMAXIMIZED => PlacementState.Maximized,
                    Native.SW_SHOWMINIMIZED => PlacementState.Minimized,
                    _ => PlacementState.Normal,
                },
            };
        }
        return byId.Values.ToList();
    }

    static LayoutMonitor? MonitorOf(List<LayoutMonitor> monitors, IntPtr handle) =>
        handle != IntPtr.Zero ? monitors.FirstOrDefault(m => m.Handle == handle) : null;

    /// <summary>
    /// Puts windows back to their saved spots. Apps that are already running are placed on the first scan;
    /// freshly launched ones are waited for up to ten seconds. The rest are dropped silently.
    /// </summary>
    /// <param name="waitIds">App ids whose window is expected to appear (just launched).</param>
    /// <param name="limit">Test hook from --limit: only apps with a matching process rule are placed.</param>
    public static async Task<(List<string> Placed, List<string> Missed)> ApplyAsync(
        Config cfg, Preset preset, ISet<string>? waitIds = null, ISet<string>? limit = null, IProgress<string>? progress = null)
    {
        var wanted = cfg.ResolveApps(preset.Keep.Concat(preset.Launch)).Select(a => a.Id).ToHashSet(StringComparer.OrdinalIgnoreCase);
        var pending = preset.Layout
            .Where(e => wanted.Contains(e.AppId))
            .GroupBy(e => e.AppId).Select(g => g.First())
            .Where(e => cfg.FindApp(e.AppId) is { } def
                        && (limit == null || def.Rules.Any(r => r.Kind == RuleKind.Process && limit.Contains(r.Value))))
            .ToList();

        var placed = new List<string>();
        var deadline = DateTime.UtcNow + WindowWait;
        while (true)
        {
            var snap = WindowScanner.Scan();
            var monitors = EnumerateMonitors();
            foreach (var e in pending.ToList())
            {
                var def = cfg.FindApp(e.AppId);
                if (def == null) { pending.Remove(e); continue; }
                var app = snap.Apps.FirstOrDefault(a => a.HasWindow && RuleMatcher.MatchesAny(def.Rules, a.Name, a.Path, a.Titles));
                if (app == null) continue;
                if (Place(app.Windows[0], e, monitors)) placed.Add(def.Name);
                pending.Remove(e);
            }
            if (pending.Count == 0 || DateTime.UtcNow >= deadline
                || waitIds != null && pending.All(e => !waitIds.Contains(e.AppId))) break;
            progress?.Report("Расставляю окна…");
            await Task.Delay(350);
        }

        var missed = pending.Select(e => cfg.FindApp(e.AppId)?.Name ?? e.AppId).ToList();
        return (placed, missed);
    }

    static bool Place(IntPtr hwnd, WindowPlacement spot, List<LayoutMonitor> monitors)
    {
        try
        {
            // The saved monitor first, then the same index, then whatever is left: the spot must survive replugging.
            var mon = monitors.FirstOrDefault(m => m.Device.Equals(spot.MonitorDevice, StringComparison.OrdinalIgnoreCase))
                      ?? monitors.FirstOrDefault(m => m.Index == spot.Monitor)
                      ?? monitors.FirstOrDefault();
            if (mon == null) return false;
            var wp = new Native.WINDOWPLACEMENT();
            wp.Length = (uint)Marshal.SizeOf<Native.WINDOWPLACEMENT>();
            if (!Native.GetWindowPlacement(hwnd, ref wp)) return false;

            int width = Math.Clamp(spot.Width, 80, mon.Bounds.Width);
            int height = Math.Clamp(spot.Height, 60, mon.Bounds.Height);
            int left = mon.Bounds.Left + Math.Clamp(spot.Left, 0, mon.Bounds.Width - width);
            int top = mon.Bounds.Top + Math.Clamp(spot.Top, 0, mon.Bounds.Height - height);
            wp.RcNormalPosition = new Native.RECT(left, top, left + width, top + height);
            wp.ShowCmd = spot.State switch
            {
                PlacementState.Maximized => Native.SW_SHOWMAXIMIZED,
                PlacementState.Minimized => Native.SW_SHOWMINIMIZED,
                _ => Native.SW_SHOWNORMAL,
            };
            wp.Flags |= Native.WPF_SETMINPOSITION;
            return Native.SetWindowPlacement(hwnd, ref wp);
        }
        catch { return false; }
    }
}
