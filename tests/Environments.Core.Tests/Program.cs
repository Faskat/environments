using Environments.Core;

int passed = 0;
void Check(bool condition, string description)
{
    if (!condition) throw new Exception(description);
    Console.WriteLine($"PASS {description}");
    passed++;
}

Environment.SetEnvironmentVariable("ENVIRONMENTS_DEMO_CLOSE", null);
Check(RuleMatcher.Matches(AppRule.Proc(" Steam*.EXE "), "steamwebhelper", null, null), "process wildcard ignores case and exe suffix");
Check(!RuleMatcher.Matches(AppRule.Proc("steam"), "steamwebhelper", null, null), "literal process name requires an exact match");
Check(!RuleMatcher.Matches(AppRule.Proc(" "), "anything", null, null), "empty rules cannot match everything");
Check(RuleMatcher.Matches(AppRule.PathHas("Games"), "game", "C:/Games/game.exe", null), "path fragment matches");
Check(RuleMatcher.Matches(AppRule.TitleHas("report"), "editor", null, new[] { "Quarterly REPORT" }), "title matching ignores case");

AppDef Def(string id, string process) => new() { Id = id, Rules = new() { AppRule.Proc(process) } };
ProcInfo Proc(int pid, int parent, string name) => new() { Pid = pid, ParentPid = parent, Name = name };
RunningApp App(int pid, string name) => new() { Pid = pid, Name = name };
var launcher = Def("launcher", "launcher");
var game = Def("game", "game");
game.Launch.Add(new LaunchTarget("C:/Games/game.exe"));
var helper = Def("helper", "helper*");
var cfg = new Config { Apps = new() { launcher, game, helper } };
cfg.Groups.Add(new AppGroup { Id = "gaming", AppIds = new() { "game", "game", "missing" } });
Check(cfg.ResolveApps(new[] { "gaming", "game" }).SequenceEqual(new[] { game }), "groups deduplicate and ignore unknown app ids");
var snap = new SystemSnapshot
{
    Procs = new()
    {
        [10] = Proc(10, 0, "launcher"), [11] = Proc(11, 10, "game"),
        [12] = Proc(12, 11, "child"), [20] = Proc(20, 0, "helper"),
        [21] = Proc(21, 20, "helperweb"), [Environment.ProcessId] = Proc(Environment.ProcessId, 99, "self"),
        [99] = Proc(99, 0, "terminal")
    },
    Apps = new() { App(10, "launcher"), App(11, "game"), App(12, "child"), App(21, "helperweb"), App(99, "terminal"), App(Environment.ProcessId, "self") }
};
var preset = new Preset { Keep = new() { "game" }, Launch = new() { "game" } };
var plan = Planner.Build(cfg, preset, snap);
Check(plan.Kept.Any(a => a.Pid == 11) && plan.Kept.Any(a => a.Pid == 12), "kept app and its child remain open");
Check(plan.Kept.Any(a => a.Pid == Environment.ProcessId) && plan.Kept.Any(a => a.Pid == 99), "own process and terminal ancestor remain open");
Check(!plan.ToClose.Single(c => c.App.Pid == 10).TreeKillSafe, "launcher with a kept descendant cannot be tree-killed");
Check(plan.ToClose.Single(c => c.App.Pid == 21).KillPid == 20, "same-app helper chain closes from its root");
Check(plan.ToLaunch.Single().AlreadyRunning, "launch plan recognizes an already running app");
cfg.Settings.Protected.Add(AppRule.Proc("launcher"));
plan = Planner.Build(cfg, preset, snap);
Check(plan.Kept.Any(a => a.Pid == 10), "protected app remains open");
preset.NewDesktop = true;
Check(Planner.Build(cfg, preset, snap).ToClose.Count == 0, "new desktop leaves existing applications open");
preset.NewDesktop = false;
Check(Planner.Build(cfg, preset, snap, new HashSet<string> { "helperweb" }).ToClose.Count == 1, "test limit excludes unrelated processes");
var cyclic = new SystemSnapshot { Procs = new() { [1] = Proc(1, 2, "a"), [2] = Proc(2, 1, "b") } };
Check(cyclic.Ancestors(1).Count() == 1, "reused pid loops terminate");
Native.TestPaths[30] = "C:/Synthetic/background.exe";
Native.TestPaths[31] = "C:/Synthetic/background-helper.exe";
var background = new SystemSnapshot
{
    Procs = new() { [30] = Proc(30, 0, "background"), [31] = Proc(31, 30, "background-helper") }
};
var backgroundPreset = new Preset { IncludeBackground = true };
var backgroundPlan = Planner.Build(new Config(), backgroundPreset, background);
Check(backgroundPlan.ToClose.Count == 1 && backgroundPlan.ToClose[0].App.Pid == 30, "background helper closes with its synthetic root");
backgroundPreset.Minimize = true;
Check(Planner.Build(new Config(), backgroundPreset, background).ToClose.Count == 0, "minimize mode never closes background processes");
Console.WriteLine($"{passed} isolated checks passed; no OS process APIs were called.");
