using System;
using System.Collections.Generic;
using System.Linq;

namespace Environments.Core;

public class Candidate : Observable
{
    private bool _selected = true;

    public RunningApp App { get; init; } = null!;
    public AppDef? Def { get; init; }
    public bool Selected { get => _selected; set => Set(ref _selected, value); }

    /// <summary>False when a kept process (say, a game) was started by this one: killing the whole tree would take it down too.</summary>
    public bool TreeKillSafe { get; init; } = true;

    /// <summary>Process to kill if closing fails; the top of the app's own process chain.</summary>
    public int KillPid { get; init; }

    public string DisplayName => Def?.Name ?? App.Name;
    public string Subtitle => App.HasWindow ? App.MainTitle : "фоновый процесс";
}

public class LaunchItem
{
    public AppDef App { get; init; } = null!;
    public bool AlreadyRunning { get; init; }
}

public class Plan
{
    public Preset Preset { get; init; } = null!;
    public List<Candidate> ToClose { get; init; } = new();
    public List<RunningApp> Kept { get; init; } = new();
    public List<LaunchItem> ToLaunch { get; init; } = new();
}

public static class Planner
{
    /// <param name="limit">Test hook: only processes with these names are considered (for closing and launching).</param>
    public static Plan Build(Config cfg, Preset preset, SystemSnapshot? snap = null, ISet<string>? limit = null)
    {
        snap ??= WindowScanner.Scan();
        var keepApps = cfg.ResolveApps(preset.Keep.Concat(preset.Launch));
        var protectedRules = cfg.Settings.Protected;

        // Our own process and whatever started us (terminal, Explorer, Task Scheduler) are never touched.
        var selfTree = new HashSet<int> { Environment.ProcessId };
        foreach (var a in snap.Ancestors(Environment.ProcessId)) selfTree.Add(a.Pid);

        var titlesByPid = snap.Apps.ToDictionary(a => a.Pid, a => (IReadOnlyList<string>)a.Titles);
        IReadOnlyList<string>? TitlesOf(int pid) => titlesByPid.TryGetValue(pid, out var t) ? t : null;

        bool IsProtected(ProcInfo p) =>
            selfTree.Contains(p.Pid) || RuleMatcher.MatchesAny(protectedRules, p.Name, p.Path, TitlesOf(p.Pid));

        bool IsKeepApp(ProcInfo p) => keepApps.Any(k => RuleMatcher.MatchesAny(k.Rules, p.Name, p.Path, TitlesOf(p.Pid)));
        bool IsKeptItself(ProcInfo p) => IsProtected(p) || IsKeepApp(p);

        // A process is kept if it matches itself, or if a kept app started it (a game launched from a kept launcher).
        // Protected ancestors do not count: otherwise everything started from Explorer would be kept.
        var keptCache = new Dictionary<int, bool>();
        bool IsKept(int pid)
        {
            if (keptCache.TryGetValue(pid, out var v)) return v;
            v = snap.Procs.TryGetValue(pid, out var p) && (IsKeptItself(p) || snap.Ancestors(pid).Any(IsKeepApp));
            return keptCache[pid] = v;
        }

        // Steam's window belongs to steamwebhelper, but the app is steam.exe above it: kill from the top of the same-app chain.
        int RootOfSameApp(int pid, AppDef? def)
        {
            if (def == null) return pid;
            int root = pid;
            foreach (var a in snap.Ancestors(pid))
            {
                if (!RuleMatcher.MatchesAny(def.Rules, a.Name, a.Path, null)) break;
                root = a.Pid;
            }
            return root;
        }

        bool InLimit(string name) => limit == null || limit.Contains(name);

        var children = snap.Procs.Values.ToLookup(p => p.ParentPid);
        bool HasKeptDescendant(int pid)
        {
            var stack = new Stack<int>(children[pid].Select(c => c.Pid));
            var seen = new HashSet<int>();
            while (stack.Count > 0)
            {
                var cur = stack.Pop();
                if (!seen.Add(cur)) continue;
                if (snap.Procs.TryGetValue(cur, out var p) && IsKeptItself(p)) return true;
                foreach (var c in children[cur]) stack.Push(c.Pid);
            }
            return false;
        }

        var plan = new Plan { Preset = preset };

        foreach (var app in snap.Apps)
        {
            if (!InLimit(app.Name)) continue;
            if (IsKept(app.Pid)) { plan.Kept.Add(app); continue; }
            var def = cfg.AppFor(app);
            int root = RootOfSameApp(app.Pid, def);
            plan.ToClose.Add(new Candidate { App = app, Def = def, KillPid = root, TreeKillSafe = !HasKeptDescendant(root) });
        }

        if (preset.IncludeBackground && !preset.Minimize)
        {
            var windowed = snap.Apps.Select(a => a.Pid).ToHashSet();
            var windowsDir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            var bg = snap.Procs.Values
                .Where(p => !windowed.Contains(p.Pid) && p.Pid > 4 && InLimit(p.Name)
                            && WindowScanner.IsSameSession(p.Pid)
                            && p.Path != null && !p.Path.StartsWith(windowsDir, StringComparison.OrdinalIgnoreCase)
                            && !IsKept(p.Pid))
                .ToList();
            var covered = plan.ToClose.Select(c => c.App.Pid).Concat(bg.Select(p => p.Pid)).ToHashSet();
            foreach (var p in bg)
            {
                // Helpers of an app we already close die with it; only top-most background processes are listed.
                if (snap.Ancestors(p.Pid).Any(a => covered.Contains(a.Pid))) continue;
                var running = new RunningApp { Pid = p.Pid, Name = p.Name, Path = p.Path };
                plan.ToClose.Add(new Candidate
                {
                    App = running, Def = cfg.AppFor(running), KillPid = p.Pid, Selected = true, TreeKillSafe = !HasKeptDescendant(p.Pid)
                });
            }
        }

        foreach (var def in cfg.ResolveApps(preset.Launch).Where(a => a.CanLaunch))
        {
            bool running = snap.Procs.Values.Any(p => RuleMatcher.MatchesAny(def.Rules, p.Name, p.Path, TitlesOf(p.Pid)));
            if (limit != null && !def.Rules.Any(r => r.Kind == RuleKind.Process && limit.Contains(r.Value))) continue;
            plan.ToLaunch.Add(new LaunchItem { App = def, AlreadyRunning = running });
        }

        // On a fresh virtual desktop the old one is left untouched: nothing gets closed.
        if (preset.NewDesktop) plan.ToClose.Clear();

        return plan;
    }
}
