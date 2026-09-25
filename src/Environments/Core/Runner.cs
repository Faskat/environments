using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

namespace Environments.Core;

public class Report
{
    public List<string> Closed { get; } = new();
    public List<string> KilledFromTray { get; } = new();
    public List<string> StillOpen { get; } = new();
    public List<string> Failed { get; } = new();
    public List<string> Minimized { get; } = new();
    public List<string> Launched { get; } = new();
    public List<string> AlreadyRunning { get; } = new();
    public List<string> LaunchFailed { get; } = new();
    public bool NewDesktop { get; set; }

    public string Summary()
    {
        var parts = new List<string>();
        if (NewDesktop) parts.Add("новый рабочий стол");
        int closed = Closed.Count + KilledFromTray.Count;
        if (closed > 0) parts.Add($"закрыто {closed}");
        if (Minimized.Count > 0) parts.Add($"свёрнуто {Minimized.Count}");
        if (StillOpen.Count > 0) parts.Add($"не закрылось {StillOpen.Count} ({string.Join(", ", StillOpen)})");
        if (Failed.Count > 0) parts.Add($"ошибки: {string.Join(", ", Failed)}");
        if (Launched.Count > 0) parts.Add($"запущено: {string.Join(", ", Launched)}");
        if (LaunchFailed.Count > 0) parts.Add($"не запустилось: {string.Join(", ", LaunchFailed)}");
        if (NewDesktop && AlreadyRunning.Count > 0) parts.Add($"на старом столе: {string.Join(", ", AlreadyRunning)}");
        return parts.Count == 0 ? "Всё уже как надо" : char.ToUpper(parts[0][0]) + string.Join(", ", parts)[1..];
    }

    public string Details()
    {
        var lines = new List<string>();
        void Add(string title, List<string> items) { if (items.Count > 0) lines.Add($"{title}: {string.Join(", ", items)}"); }
        if (NewDesktop) lines.Add("Открыт новый рабочий стол");
        Add("Закрыто", Closed);
        Add("Добито из трея", KilledFromTray);
        Add("Свёрнуто", Minimized);
        Add("Не закрылось (ждёт сохранения?)", StillOpen);
        Add("Ошибки", Failed);
        Add("Запущено", Launched);
        Add(NewDesktop ? "Уже запущено (осталось на старом столе)" : "Уже было запущено", AlreadyRunning);
        Add("Не запустилось", LaunchFailed);
        return lines.Count == 0 ? "Всё уже как надо" : string.Join(Environment.NewLine, lines);
    }
}

public static class Runner
{
    public static async Task<Report> Execute(Plan plan, IProgress<string>? progress = null)
    {
        var preset = plan.Preset;
        var report = new Report();
        var targets = plan.ToClose.Where(c => c.Selected).ToList();

        if (preset.NewDesktop)
        {
            progress?.Report("Создаю рабочий стол…");
            await VirtualDesktop.CreateAndSwitch();
            report.NewDesktop = true;
        }
        else if (preset.Minimize)
        {
            foreach (var c in targets.Where(c => c.App.HasWindow))
            {
                foreach (var w in c.App.Windows) Native.ShowWindow(w, Native.SW_MINIMIZE);
                report.Minimized.Add(c.DisplayName);
            }
        }
        else if (targets.Count > 0)
        {
            progress?.Report($"Закрываю {targets.Count}…");
            SaveUndo(targets);

            foreach (var c in targets)
                foreach (var w in c.App.Windows)
                    if (Native.IsWindow(w)) Native.PostMessage(w, Native.WM_CLOSE, IntPtr.Zero, IntPtr.Zero);

            var pending = targets.ToList();
            var deadline = DateTime.UtcNow.AddSeconds(preset.WaitSeconds);
            var startedAt = DateTime.UtcNow;
            while (pending.Count > 0 && DateTime.UtcNow < deadline)
            {
                await Task.Delay(250);
                foreach (var c in pending.Where(c => HasExited(c.App.Pid)).ToList())
                {
                    report.Closed.Add(c.DisplayName);
                    pending.Remove(c);
                }
                // Apps that only hid to the tray will never exit on their own: stop waiting once nothing shows a window.
                if (preset.Mode != CloseMode.Gentle && DateTime.UtcNow - startedAt > TimeSpan.FromSeconds(1.5))
                {
                    var withWindows = WindowScanner.PidsWithWindows();
                    if (pending.All(c => !withWindows.Contains(c.App.Pid))) break;
                }
            }

            var visible = WindowScanner.PidsWithWindows();
            foreach (var c in pending)
            {
                if (HasExited(c.App.Pid)) { report.Closed.Add(c.DisplayName); continue; }
                bool showsWindow = visible.Contains(c.App.Pid);
                bool kill = preset.Mode == CloseMode.Force || (preset.Mode == CloseMode.Smart && !showsWindow);
                if (!kill)
                {
                    report.StillOpen.Add(c.DisplayName);
                    continue;
                }
                var err = Kill(c);
                if (err == null) (showsWindow ? report.Closed : report.KilledFromTray).Add(c.DisplayName);
                else report.Failed.Add($"{c.DisplayName} ({err})");
            }
        }

        foreach (var item in plan.ToLaunch)
        {
            if (item.AlreadyRunning) { report.AlreadyRunning.Add(item.App.Name); continue; }
            progress?.Report($"Запускаю {item.App.Name}…");
            var err = Launcher.Start(item.App);
            if (err == null) report.Launched.Add(item.App.Name);
            else report.LaunchFailed.Add($"{item.App.Name} ({err})");
            await Task.Delay(400);
        }

        return report;
    }

    static bool HasExited(int pid)
    {
        try
        {
            using var p = Process.GetProcessById(pid);
            return p.HasExited;
        }
        catch (ArgumentException) { return true; }
        catch (Exception) { return false; }
    }

    static string? Kill(Candidate c)
    {
        try
        {
            using var p = Process.GetProcessById(c.KillPid != 0 ? c.KillPid : c.App.Pid);
            p.Kill(entireProcessTree: c.TreeKillSafe);
            p.WaitForExit(3000);
            return null;
        }
        catch (ArgumentException) { return null; }
        catch (Exception e) { return e is System.ComponentModel.Win32Exception ? "нет прав" : e.Message; }
    }

    static void SaveUndo(List<Candidate> closing)
    {
        var entries = closing
            .Where(c => c.App.Path != null)
            .GroupBy(c => c.App.Path!, StringComparer.OrdinalIgnoreCase)
            .Select(g => new UndoEntry { Name = g.First().DisplayName, Path = g.Key, AppId = g.First().Def?.Id })
            .ToList();
        if (entries.Count > 0) Storage.SaveUndo(entries);
    }

    /// <summary>Starts again whatever the last preset closed, skipping what is already running.</summary>
    public static async Task<Report> Undo(Config cfg)
    {
        var report = new Report();
        var procs = WindowScanner.SnapshotProcesses().Values.ToList();
        foreach (var e in Storage.LoadUndo())
        {
            var def = e.AppId != null ? cfg.FindApp(e.AppId) : null;
            bool running = procs.Any(p => string.Equals(p.Path, e.Path, StringComparison.OrdinalIgnoreCase))
                           || (def != null && procs.Any(p => RuleMatcher.MatchesAny(def.Rules, p.Name, p.Path, null)));
            if (running) { report.AlreadyRunning.Add(e.Name); continue; }

            var err = def is { CanLaunch: true } ? Launcher.Start(def) : Launcher.StartExe(e.Path);
            if (err == null) report.Launched.Add(e.Name);
            else report.LaunchFailed.Add($"{e.Name} ({err})");
            await Task.Delay(300);
        }
        return report;
    }
}
