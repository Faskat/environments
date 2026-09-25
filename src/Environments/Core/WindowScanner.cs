using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;

namespace Environments.Core;

public class ProcInfo
{
    public int Pid { get; init; }
    public int ParentPid { get; init; }
    public string Name { get; init; } = "";

    private bool _pathLoaded;
    private string? _path;
    public string? Path
    {
        get
        {
            if (!_pathLoaded) { _path = Native.GetProcessPath((uint)Pid); _pathLoaded = true; }
            return _path;
        }
    }
}

/// <summary>A process that owns at least one real top-level window (or a background process, when asked for).</summary>
public class RunningApp
{
    public int Pid { get; init; }
    public string Name { get; init; } = "";
    public string? Path { get; init; }
    public List<IntPtr> Windows { get; } = new();
    public List<string> Titles { get; } = new();
    public bool HasWindow => Windows.Count > 0;
    public string MainTitle => Titles.FirstOrDefault(t => t.Length > 0) ?? "";
}

public class SystemSnapshot
{
    public Dictionary<int, ProcInfo> Procs { get; init; } = new();
    public List<RunningApp> Apps { get; init; } = new();

    /// <summary>Walks parent links up to the root. Guards against pid reuse loops.</summary>
    public IEnumerable<ProcInfo> Ancestors(int pid)
    {
        var seen = new HashSet<int> { pid };
        while (Procs.TryGetValue(pid, out var p) && p.ParentPid != 0 && seen.Add(p.ParentPid)
               && Procs.TryGetValue(p.ParentPid, out var parent))
        {
            yield return parent;
            pid = parent.Pid;
        }
    }
}

public static class WindowScanner
{
    static readonly uint MySession = GetSession((uint)Environment.ProcessId);

    static uint GetSession(uint pid) => Native.ProcessIdToSessionId(pid, out var s) ? s : uint.MaxValue;

    public static Dictionary<int, ProcInfo> SnapshotProcesses()
    {
        var result = new Dictionary<int, ProcInfo>();
        var snap = Native.CreateToolhelp32Snapshot(Native.TH32CS_SNAPPROCESS, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return result;
        try
        {
            var e = new Native.PROCESSENTRY32 { dwSize = (uint)Marshal.SizeOf<Native.PROCESSENTRY32>() };
            if (!Native.Process32FirstW(snap, ref e)) return result;
            do
            {
                var name = e.szExeFile ?? "";
                if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
                result[(int)e.th32ProcessID] = new ProcInfo
                {
                    Pid = (int)e.th32ProcessID, ParentPid = (int)e.th32ParentProcessID, Name = name
                };
            } while (Native.Process32NextW(snap, ref e));
        }
        finally { Native.CloseHandle(snap); }
        return result;
    }

    public static SystemSnapshot Scan()
    {
        var procs = SnapshotProcesses();
        var apps = new Dictionary<int, RunningApp>();

        foreach (var (hwnd, pid) in EnumAppWindows())
        {
            if (!procs.TryGetValue(pid, out var proc)) continue;
            if (!apps.TryGetValue(pid, out var app))
            {
                app = new RunningApp { Pid = pid, Name = proc.Name, Path = proc.Path };
                apps[pid] = app;
            }
            app.Windows.Add(hwnd);
            app.Titles.Add(Native.GetTitle(hwnd));
        }

        return new SystemSnapshot { Procs = procs, Apps = apps.Values.OrderBy(a => a.Name, StringComparer.OrdinalIgnoreCase).ToList() };
    }

    /// <summary>Pids that currently show at least one real window. Used to tell "went to tray" from "waits for a save dialog".</summary>
    public static HashSet<int> PidsWithWindows() => EnumAppWindows().Select(w => w.pid).ToHashSet();

    /// <summary>Top-level windows a user would see on the taskbar, mapped to the pid that really owns them.</summary>
    static List<(IntPtr hwnd, int pid)> EnumAppWindows()
    {
        var list = new List<(IntPtr, int)>();
        Native.EnumWindows((hwnd, _) =>
        {
            if (!Native.IsWindowVisible(hwnd)) return true;
            if (Native.GetWindow(hwnd, Native.GW_OWNER) != IntPtr.Zero) return true;
            long ex = Native.GetWindowLongPtr(hwnd, Native.GWL_EXSTYLE).ToInt64();
            if ((ex & Native.WS_EX_TOOLWINDOW) != 0 && (ex & Native.WS_EX_APPWINDOW) == 0) return true;
            if (Native.IsCloaked(hwnd)) return true;
            if (Native.GetWindowTextLength(hwnd) == 0) return true;

            Native.GetWindowThreadProcessId(hwnd, out uint pid);
            if (GetSession(pid) != MySession) return true;

            // UWP apps live inside ApplicationFrameHost; the real process owns a child window.
            if (IsFrameHost(pid))
            {
                uint inner = 0;
                Native.EnumChildWindows(hwnd, (child, lp) =>
                {
                    Native.GetWindowThreadProcessId(child, out uint cp);
                    if (cp != pid) { inner = cp; return false; }
                    return true;
                }, IntPtr.Zero);
                if (inner != 0) pid = inner;
            }

            list.Add((hwnd, (int)pid));
            return true;
        }, IntPtr.Zero);
        return list;
    }

    static bool IsFrameHost(uint pid)
    {
        var path = Native.GetProcessPath(pid);
        return path != null && Path.GetFileName(path).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSameSession(int pid) => GetSession((uint)pid) == MySession;
}
