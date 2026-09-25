using System.Collections.Generic;
using System.Linq;

namespace Environments.Core;

/// <summary>Built-in apps, groups and presets written on first start.</summary>
public static class Defaults
{
    public static Config Create() => new()
    {
        Apps = Apps(),
        Groups = Groups(),
        Presets = Presets(),
        Settings = new Settings { Protected = Protected() }
    };

    static AppDef App(string id, string name, string icon, AppRule[] rules, params LaunchTarget[] launch) => new()
    {
        Id = id, Name = name, Icon = icon, Rules = rules.ToList(), Launch = launch.ToList()
    };

    static LaunchTarget L(string path, string args = "") => new(path, args);

    public static List<AppDef> Apps() => new()
    {
        App("discord", "Discord", "messenger", new[] { AppRule.Proc("Discord") },
            L(@"%LOCALAPPDATA%\Discord\Update.exe", "--processStart Discord.exe"), L("discord://")),
        App("telegram", "Telegram", "messenger", new[] { AppRule.Proc("Telegram"), AppRule.Proc("AyuGram") },
            L(@"%APPDATA%\Telegram Desktop\Telegram.exe"), L("tg://")),

        App("edge", "Microsoft Edge", "browser", new[] { AppRule.Proc("msedge") },
            L(@"%ProgramFiles(x86)%\Microsoft\Edge\Application\msedge.exe"), L(@"%ProgramFiles%\Microsoft\Edge\Application\msedge.exe")),
        App("chrome", "Google Chrome", "browser", new[] { AppRule.Proc("chrome") },
            L(@"%ProgramFiles%\Google\Chrome\Application\chrome.exe"), L(@"%LOCALAPPDATA%\Google\Chrome\Application\chrome.exe")),
        App("firefox", "Firefox", "browser", new[] { AppRule.Proc("firefox") },
            L(@"%ProgramFiles%\Mozilla Firefox\firefox.exe")),
        App("opera", "Opera / Opera GX", "browser", new[] { AppRule.Proc("opera") },
            L(@"%LOCALAPPDATA%\Programs\Opera GX\opera.exe"), L(@"%LOCALAPPDATA%\Programs\Opera\opera.exe")),
        App("brave", "Brave", "browser", new[] { AppRule.Proc("brave") },
            L(@"%ProgramFiles%\BraveSoftware\Brave-Browser\Application\brave.exe")),

        App("steam", "Steam", "launcher", new[] { AppRule.Proc("steam"), AppRule.Proc("steamwebhelper") },
            L(@"%ProgramFiles(x86)%\Steam\steam.exe"), L("steam://open/main")),
        App("epic", "Epic Games", "launcher", new[] { AppRule.Proc("EpicGamesLauncher"), AppRule.Proc("EpicWebHelper") },
            L("com.epicgames.launcher://")),
        App("ea", "EA app", "launcher", new[] { AppRule.Proc("EADesktop") },
            L(@"%ProgramFiles%\Electronic Arts\EA Desktop\EA Desktop\EALauncher.exe")),
        App("battlenet", "Battle.net", "launcher", new[] { AppRule.Proc("Battle.net") },
            L(@"%ProgramFiles(x86)%\Battle.net\Battle.net Launcher.exe")),
        App("riot", "Riot Client", "launcher", new[] { AppRule.Proc("RiotClientServices"), AppRule.Proc("Riot Client") },
            L(@"C:\Riot Games\Riot Client\RiotClientServices.exe")),
        App("prism", "Prism / ElyPrism", "launcher", new[] { AppRule.Proc("prismlauncher"), AppRule.Proc("elyprismlauncher") },
            L(@"D:\Games\ElyPrismLauncher\elyprismlauncher.exe"), L(@"%LOCALAPPDATA%\Programs\PrismLauncher\prismlauncher.exe")),
        App("games", "Игры (по папкам)", "game", new[]
        {
            AppRule.PathHas(@"steamapps\common"), AppRule.PathHas(@"\Games\"), AppRule.PathHas("Riot Games"),
            AppRule.PathHas(@"Epic Games\"), AppRule.Proc("javaw"), AppRule.Proc("Minecraft*")
        }),

        App("obs", "OBS Studio", "record", new[] { AppRule.Proc("obs64") },
            L(@"D:\Programs\obs-studio\bin\64bit\obs64.exe"), L(@"%ProgramFiles%\obs-studio\bin\64bit\obs64.exe")),
        App("spotify", "Spotify", "music", new[] { AppRule.Proc("Spotify") },
            L(@"%APPDATA%\Spotify\Spotify.exe"), L("spotify:")),

        App("vscode", "VS Code", "code", new[] { AppRule.Proc("Code") },
            L(@"%LOCALAPPDATA%\Programs\Microsoft VS Code\Code.exe"), L(@"%ProgramFiles%\Microsoft VS Code\Code.exe")),
        App("jetbrains", "JetBrains IDE", "code", new[]
        {
            AppRule.Proc("pycharm64"), AppRule.Proc("idea64"), AppRule.Proc("rider64"),
            AppRule.Proc("webstorm64"), AppRule.Proc("clion64")
        }),
        App("obsidian", "Obsidian", "draw", new[] { AppRule.Proc("Obsidian") },
            L(@"%LOCALAPPDATA%\Programs\Obsidian\Obsidian.exe"), L("obsidian://")),

        App("teams", "Microsoft Teams", "school", new[] { AppRule.Proc("ms-teams"), AppRule.Proc("Teams") },
            L(@"%WINDIR%\explorer.exe", @"shell:AppsFolder\MSTeams_8wekyb3d8bbwe!MSTeams"), L("msteams:")),
        App("zoom", "Zoom", "school", new[] { AppRule.Proc("Zoom") },
            L(@"%APPDATA%\Zoom\bin\Zoom.exe")),

        App("blender", "Blender", "cube", new[] { AppRule.Proc("blender") },
            L(@"%ProgramFiles%\Blender Foundation\Blender *\blender.exe")),
        App("blockbench", "Blockbench", "cube", new[] { AppRule.Proc("Blockbench") },
            L(@"%LOCALAPPDATA%\Programs\Blockbench\Blockbench.exe")),
        App("davinci", "DaVinci Resolve", "video", new[] { AppRule.Proc("Resolve") },
            L(@"%ProgramFiles%\Blackmagic Design\DaVinci Resolve\Resolve.exe")),
    };

    public static List<AppGroup> Groups() => new()
    {
        G("g-messengers", "Мессенджеры", "messenger", "discord", "telegram"),
        G("g-browsers", "Браузеры", "browser", "edge", "chrome", "firefox", "opera", "brave"),
        G("g-launchers", "Лаунчеры", "launcher", "steam", "epic", "ea", "battlenet", "riot", "prism"),
        G("g-games", "Игры", "game", "games"),
        G("g-recording", "Запись", "record", "obs"),
        G("g-music", "Музыка", "music", "spotify"),
        G("g-code", "Код", "code", "vscode", "jetbrains"),
        G("g-notes", "Заметки", "draw", "obsidian"),
        G("g-school", "Школа", "school", "teams", "zoom"),
        G("g-3d", "3D и видео", "cube", "blender", "blockbench", "davinci"),
    };

    static AppGroup G(string id, string name, string icon, params string[] apps) => new()
    {
        Id = id, Name = name, Icon = icon, AppIds = apps.ToList()
    };

    static Preset P(string id, string name, string icon, string color, string hotkey, string[] keep, string[] launch) => new()
    {
        Id = id, Name = name, Icon = icon, Color = color, Hotkey = hotkey, Keep = keep.ToList(), Launch = launch.ToList()
    };

    public static List<Preset> Presets() => new()
    {
        P("game", "Игра", "game", "#57D68D", "Ctrl+Alt+1",
            new[] { "g-games", "g-launchers", "g-messengers", "g-music", "g-recording" },
            new[] { "discord", "telegram", "steam" }),
        P("stream", "Стрим / запись", "stream", "#FF6B6B", "Ctrl+Alt+2",
            new[] { "g-games", "g-launchers", "g-recording", "discord", "g-music" },
            new[] { "obs", "discord", "steam" }),
        P("study", "Учёба / НМТ", "study", "#7C8CFF", "Ctrl+Alt+3",
            new[] { "g-browsers", "g-notes", "g-school" },
            new[] { "obsidian" }),
        P("code", "Код", "code", "#C38BFF", "Ctrl+Alt+4",
            new[] { "g-code", "g-browsers", "g-notes", "g-music", "telegram" },
            new[] { "obsidian" }),
        P("3d", "3D / Blender", "cube", "#FF9F43", "Ctrl+Alt+5",
            new[] { "g-3d", "g-browsers", "discord", "g-music" },
            new[] { "blender" }),
        P("chill", "Чилл", "chill", "#4FD1E8", "Ctrl+Alt+6",
            new[] { "g-browsers", "g-music", "g-messengers" },
            new[] { "spotify", "telegram" }),
        P("clean", "Чистый лист", "broom", "#9AA0A6", "Ctrl+Alt+0",
            new string[0], new string[0]),
    };

    /// <summary>Never closed by any preset: shell, system, drivers, overlays, terminals and Claude.</summary>
    public static List<AppRule> Protected() => new[]
    {
        "explorer", "dwm", "csrss", "winlogon", "sihost", "fontdrvhost", "ctfmon", "svchost", "RuntimeBroker",
        "ShellExperienceHost", "StartMenuExperienceHost", "SearchHost", "SearchApp", "TextInputHost", "LockApp",
        "SystemSettings", "Taskmgr", "ShellHost", "Widgets", "WidgetService", "SecurityHealthSystray", "SecHealthUI",
        "MsMpEng", "NVIDIA*", "nvcontainer", "nvsphelper64", "RtkAudUService64", "Razer*", "SteelSeries*",
        "PowerToys*", "msedgewebview2", "WindowsTerminal", "OpenConsole", "conhost", "cmd", "powershell", "pwsh",
        "claude", "Environments", "RTSS", "MSIAfterburner",
    }.Select(AppRule.Proc).ToList();
}
