using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Environments.Core;

namespace Environments;

/// <summary>
/// Command line for scripts and testing:
///   --list
///   --dry-run "Игра" [--limit notepad,mspaint]
///   --apply "Игра"   [--limit notepad,mspaint]
///   --undo
/// </summary>
public static class Cli
{
    static readonly string[] Commands = { "--list", "--dry-run", "--apply", "--undo" };

    public static bool IsCli(string[] args) => args.Any(a => Commands.Contains(a, StringComparer.OrdinalIgnoreCase));

    public static int Run(string[] args)
    {
        Native.AttachConsole(-1);
        try { Console.OutputEncoding = Encoding.UTF8; } catch { }
        Console.WriteLine();

        var cfg = Storage.Load();
        string? Arg(string name)
        {
            int i = Array.FindIndex(args, a => a.Equals(name, StringComparison.OrdinalIgnoreCase));
            return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
        }
        ISet<string>? limit = Arg("--limit") is { } l
            ? l.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToHashSet(StringComparer.OrdinalIgnoreCase)
            : null;

        if (args.Contains("--list"))
        {
            foreach (var p in cfg.Presets) Console.WriteLine($"{p.Name}  [{p.Id}]  {p.Hotkey}");
            return 0;
        }

        if (args.Contains("--undo"))
        {
            var r = Task.Run(() => Runner.Undo(cfg)).GetAwaiter().GetResult();
            Console.WriteLine(r.Details());
            return 0;
        }

        var name = Arg("--dry-run") ?? Arg("--apply");
        var preset = cfg.Presets.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase)
                                                  || p.Id.Equals(name, StringComparison.OrdinalIgnoreCase));
        if (preset == null)
        {
            Console.WriteLine($"Нет пресета «{name}». Есть: {string.Join(", ", cfg.Presets.Select(p => p.Name))}");
            return 2;
        }

        var plan = Planner.Build(cfg, preset, limit: limit);
        Console.WriteLine($"Пресет: {preset.Name}");
        Console.WriteLine($"Закроется ({plan.ToClose.Count}):");
        foreach (var c in plan.ToClose)
            Console.WriteLine($"  - {c.DisplayName} [{c.App.Name}, pid {c.App.Pid}] {c.Subtitle}");
        Console.WriteLine($"Останется: {string.Join(", ", plan.Kept.Select(k => k.Name).Distinct())}");
        Console.WriteLine($"Запуск: {string.Join(", ", plan.ToLaunch.Select(i => i.App.Name + (i.AlreadyRunning ? " (уже запущено)" : "")))}");

        if (args.Contains("--apply"))
        {
            var report = Task.Run(() => Runner.Execute(plan)).GetAwaiter().GetResult();
            Console.WriteLine();
            Console.WriteLine(report.Details());
        }
        return 0;
    }
}
