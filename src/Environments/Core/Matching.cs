using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;

namespace Environments.Core;

public static class RuleMatcher
{
    public static bool Matches(AppRule rule, string name, string? path, IReadOnlyList<string>? titles)
    {
        var value = rule.Value.Trim();
        if (value.Length == 0) return false;
        switch (rule.Kind)
        {
            case RuleKind.Process:
                if (value.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) value = value[..^4];
                return value.Contains('*')
                    ? Regex.IsMatch(name, "^" + Regex.Escape(value).Replace(@"\*", ".*") + "$", RegexOptions.IgnoreCase)
                    : name.Equals(value, StringComparison.OrdinalIgnoreCase);
            case RuleKind.Path:
                return path != null && path.Contains(Environment.ExpandEnvironmentVariables(value), StringComparison.OrdinalIgnoreCase);
            case RuleKind.Title:
                return titles != null && titles.Any(t => t.Contains(value, StringComparison.OrdinalIgnoreCase));
            default:
                return false;
        }
    }

    public static bool MatchesAny(IEnumerable<AppRule> rules, string name, string? path, IReadOnlyList<string>? titles) =>
        rules.Any(r => Matches(r, name, path, titles));

    public static bool Matches(AppDef app, RunningApp running) =>
        MatchesAny(app.Rules, running.Name, running.Path, running.Titles);
}

public static class ConfigExtensions
{
    public static AppDef? FindApp(this Config cfg, string id) => cfg.Apps.FirstOrDefault(a => a.Id == id);
    public static AppGroup? FindGroup(this Config cfg, string id) => cfg.Groups.FirstOrDefault(g => g.Id == id);

    /// <summary>Expands a list of app and group ids to distinct apps.</summary>
    public static List<AppDef> ResolveApps(this Config cfg, IEnumerable<string> ids)
    {
        var result = new List<AppDef>();
        foreach (var id in ids)
        {
            if (cfg.FindApp(id) is { } app) result.Add(app);
            else if (cfg.FindGroup(id) is { } group)
                result.AddRange(group.AppIds.Select(cfg.FindApp).Where(a => a != null)!);
        }
        return result.Distinct().ToList();
    }

    public static AppDef? AppFor(this Config cfg, RunningApp running) =>
        cfg.Apps.FirstOrDefault(a => RuleMatcher.Matches(a, running));

    public static string NewId(this Config cfg, string baseId)
    {
        baseId = Regex.Replace(baseId.ToLowerInvariant(), "[^a-z0-9]+", "-").Trim('-');
        if (baseId.Length == 0) baseId = "item";
        var id = baseId;
        for (int i = 2; cfg.Apps.Any(a => a.Id == id) || cfg.Groups.Any(g => g.Id == id) || cfg.Presets.Any(p => p.Id == id); i++)
            id = $"{baseId}-{i}";
        return id;
    }
}
