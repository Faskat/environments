using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Text.Json.Serialization;

namespace Environments.Core;

public abstract class Observable : INotifyPropertyChanged
{
    public event PropertyChangedEventHandler? PropertyChanged;

    protected bool Set<T>(ref T field, T value, [CallerMemberName] string? name = null)
    {
        if (EqualityComparer<T>.Default.Equals(field, value)) return false;
        field = value;
        Raise(name!);
        return true;
    }

    protected void Raise(string name) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum RuleKind { Process, Path, Title }

/// <summary>One way to recognise a running app: by exe name, by path fragment or by window title fragment.</summary>
public class AppRule
{
    public RuleKind Kind { get; set; }
    public string Value { get; set; } = "";

    public AppRule() { }
    public AppRule(RuleKind kind, string value) { Kind = kind; Value = value; }

    public static AppRule Proc(string v) => new(RuleKind.Process, v);
    public static AppRule PathHas(string v) => new(RuleKind.Path, v);
    public static AppRule TitleHas(string v) => new(RuleKind.Title, v);

    public override string ToString() => $"{Kind.ToString().ToLowerInvariant()}: {Value}";

    /// <summary>Parses "process: discord", "path: D:\Games", "title: YouTube". A bare word means process.</summary>
    public static bool TryParse(string line, out AppRule rule)
    {
        rule = new AppRule();
        line = line.Trim();
        if (line.Length == 0) return false;
        int colon = line.IndexOf(':');
        if (colon > 0)
        {
            var head = line[..colon].Trim().ToLowerInvariant();
            var tail = line[(colon + 1)..].Trim();
            RuleKind? kind = head switch
            {
                "process" or "proc" or "exe" or "процесс" => RuleKind.Process,
                "path" or "путь" => RuleKind.Path,
                "title" or "заголовок" => RuleKind.Title,
                _ => null
            };
            if (kind != null)
            {
                if (tail.Length == 0) return false;
                rule = new AppRule(kind.Value, tail);
                return true;
            }
        }
        rule = new AppRule(RuleKind.Process, line);
        return true;
    }
}

/// <summary>How to start an app: exe path (env vars and * allowed) or a URI like steam://open/main.</summary>
public class LaunchTarget
{
    public string Path { get; set; } = "";
    public string Args { get; set; } = "";

    public LaunchTarget() { }
    public LaunchTarget(string path, string args = "") { Path = path; Args = args; }

    public override string ToString() => Args.Length > 0 ? $"{Path} | {Args}" : Path;

    public static bool TryParse(string line, out LaunchTarget target)
    {
        target = new LaunchTarget();
        line = line.Trim();
        if (line.Length == 0) return false;
        int bar = line.IndexOf('|');
        target = bar < 0
            ? new LaunchTarget(line.Trim('"'))
            : new LaunchTarget(line[..bar].Trim().Trim('"'), line[(bar + 1)..].Trim());
        return target.Path.Length > 0;
    }
}

public class AppDef : Observable
{
    private string _name = "";
    private string _icon = "";

    public string Id { get; set; } = "";
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Icon { get => _icon; set => Set(ref _icon, value); }
    public List<AppRule> Rules { get; set; } = new();
    public List<LaunchTarget> Launch { get; set; } = new();

    [JsonIgnore] public bool CanLaunch => Launch.Count > 0;
}

public class AppGroup : Observable
{
    private string _name = "";
    private string _icon = "";

    public string Id { get; set; } = "";
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Icon { get => _icon; set => Set(ref _icon, value); }
    public List<string> AppIds { get; set; } = new();
}

[JsonConverter(typeof(JsonStringEnumConverter))]
public enum CloseMode
{
    /// <summary>Only ask windows to close. Apps that hide to the tray stay alive.</summary>
    Gentle,
    /// <summary>Ask to close; apps that went to the tray get killed, apps still showing a window (save dialog) are left alone.</summary>
    Smart,
    /// <summary>Ask to close, then kill whatever is still alive.</summary>
    Force
}

public class Preset : Observable
{
    private string _name = "";
    private string _icon = "";
    private string _color = "#7C8CFF";
    private string _hotkey = "";
    private CloseMode _mode = CloseMode.Smart;
    private int _waitSeconds = 6;
    private bool _minimize;
    private bool _includeBackground;
    private bool _newDesktop;

    public string Id { get; set; } = "";
    public string Name { get => _name; set => Set(ref _name, value); }
    public string Icon { get => _icon; set => Set(ref _icon, value); }
    public string Color { get => _color; set => Set(ref _color, value); }
    public string Hotkey { get => _hotkey; set => Set(ref _hotkey, value); }

    /// <summary>Ids of apps and groups that must stay open.</summary>
    public List<string> Keep { get; set; } = new();

    /// <summary>Ids of apps and groups to start if they are not running. Launched items are kept too.</summary>
    public List<string> Launch { get; set; } = new();

    public CloseMode Mode { get => _mode; set => Set(ref _mode, value); }
    public int WaitSeconds { get => _waitSeconds; set => Set(ref _waitSeconds, Math.Clamp(value, 1, 60)); }
    public bool Minimize { get => _minimize; set => Set(ref _minimize, value); }
    public bool IncludeBackground { get => _includeBackground; set => Set(ref _includeBackground, value); }

    /// <summary>Create a new virtual desktop, switch to it and launch there instead of closing anything.</summary>
    public bool NewDesktop { get => _newDesktop; set => Set(ref _newDesktop, value); }
}

public class Settings
{
    public List<AppRule> Protected { get; set; } = new();
    public bool HotkeyShowsPreview { get; set; }
    public bool Notifications { get; set; } = true;
}

public class Config
{
    public int Version { get; set; } = 1;
    public List<AppDef> Apps { get; set; } = new();
    public List<AppGroup> Groups { get; set; } = new();
    public List<Preset> Presets { get; set; } = new();
    public Settings Settings { get; set; } = new();
}
