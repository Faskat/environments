using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using Environments.Core;
using Environments.UI;

namespace Environments;

public partial class App : Application
{
    const string MutexName = "Environments.SingleInstance.v1";
    const string ShowEventName = "Environments.Show.v1";

    public static Config Config { get; private set; } = new();
    public static App Instance => (App)Current;

    Mutex? _mutex;
    EventWaitHandle? _showEvent;
    TrayIcon? _tray;
    HotkeyManager? _hotkeys;
    MainWindow? _main;
    bool _busy;

    public List<string> FailedHotkeys { get; } = new();

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += (_, ex) =>
        {
            LogError(ex.Exception);
            ex.Handled = true;
        };
        AppDomain.CurrentDomain.UnhandledException += (_, ex) => LogError(ex.ExceptionObject as Exception);

        if (Cli.IsCli(e.Args))
        {
            Shutdown(Cli.Run(e.Args));
            return;
        }

        _mutex = new Mutex(true, MutexName, out bool first);
        if (!first)
        {
            // Already running: ask that instance to show its window.
            try { EventWaitHandle.OpenExisting(ShowEventName).Set(); } catch { }
            Shutdown();
            return;
        }
        _showEvent = new EventWaitHandle(false, EventResetMode.AutoReset, ShowEventName);
        ThreadPool.RegisterWaitForSingleObject(_showEvent, (_, _) => Dispatcher.BeginInvoke(ShowMain), null, -1, false);

        // Dark title bars for every window.
        EventManager.RegisterClassHandler(typeof(Window), FrameworkElement.LoadedEvent, new RoutedEventHandler((s, _) =>
        {
            var hwnd = new System.Windows.Interop.WindowInteropHelper((Window)s).Handle;
            int on = 1;
            Native.DwmSetWindowAttribute(hwnd, 20, ref on, sizeof(int));
        }));

        Config = Storage.Load();
        MigrateIcons();
        _hotkeys = new HotkeyManager();
        _tray = new TrayIcon();
        ApplyHotkeys();

        if (FailedHotkeys.Count > 0)
            _tray.Notify("Горячие клавиши заняты", string.Join(", ", FailedHotkeys) + " — выбери другие в пресетах.");

        // --preview "Игра" opens the preview window straight away (handy for scripts and demos).
        int pv = Array.FindIndex(e.Args, a => a.Equals("--preview", StringComparison.OrdinalIgnoreCase));
        var previewPreset = pv >= 0 && pv + 1 < e.Args.Length
            ? Config.Presets.FirstOrDefault(p => p.Name.Equals(e.Args[pv + 1], StringComparison.OrdinalIgnoreCase) || p.Id == e.Args[pv + 1])
            : null;
        if (previewPreset != null) RunPreset(previewPreset, true);
        else if (!e.Args.Contains("--hidden")) ShowMain();
    }

    /// <summary>Configs written before the icon set used emoji; swap them for icon names once.</summary>
    static void MigrateIcons()
    {
        bool changed = false;
        string M(string icon)
        {
            var next = Icons.Migrate(icon);
            changed |= next != icon;
            return next;
        }
        foreach (var p in Config.Presets) p.Icon = M(p.Icon);
        foreach (var g in Config.Groups) g.Icon = M(g.Icon);
        foreach (var a in Config.Apps) a.Icon = M(a.Icon);
        if (changed) Storage.Save(Config);
    }

    public void ShowMain()
    {
        if (_main == null)
        {
            _main = new MainWindow();
            _main.Closed += (_, _) => _main = null;
        }
        _main.Show();
        if (_main.WindowState == WindowState.Minimized) _main.WindowState = WindowState.Normal;
        _main.Activate();
    }

    /// <summary>Persists the config and refreshes everything that depends on it.</summary>
    public void SaveConfig()
    {
        Storage.Save(Config);
        ApplyHotkeys();
        _tray?.Rebuild();
    }

    public void ReplaceConfig(Config cfg)
    {
        Config = cfg;
        SaveConfig();
    }

    /// <summary>While the user records a new hotkey, ours must not swallow the keystrokes.</summary>
    public void SuspendHotkeys() => _hotkeys?.Clear();
    public void ResumeHotkeys() => ApplyHotkeys();

    void ApplyHotkeys()
    {
        if (_hotkeys == null) return;
        _hotkeys.Clear();
        FailedHotkeys.Clear();
        foreach (var p in Config.Presets.Where(p => p.Hotkey.Length > 0))
        {
            var preset = p;
            if (!_hotkeys.Register(p.Hotkey, () => RunPreset(preset, Config.Settings.HotkeyShowsPreview)))
                FailedHotkeys.Add($"{p.Hotkey} ({p.Name})");
        }
    }

    /// <summary>Entry point for tray, hotkeys and the main window.</summary>
    public async void RunPreset(Preset preset, bool preview)
    {
        if (preview)
        {
            var w = new PreviewWindow(preset);
            w.Show();
            w.Activate();
            return;
        }
        if (_busy) return;
        _busy = true;
        try
        {
            var plan = await Task.Run(() => Planner.Build(Config, preset));
            var report = await Runner.Execute(plan);
            _tray?.Notify(preset.Name, report.Summary());
        }
        finally { _busy = false; }
    }

    public async void Undo()
    {
        var report = await Runner.Undo(Config);
        _tray?.Notify("Вернул как было", report.Summary());
    }

    public void Notify(string title, string text) => _tray?.Notify(title, text);

    public void Quit()
    {
        _hotkeys?.Dispose();
        _tray?.Dispose();
        Shutdown();
    }

    static void LogError(Exception? ex)
    {
        try
        {
            System.IO.Directory.CreateDirectory(Storage.DataDir);
            System.IO.File.AppendAllText(System.IO.Path.Combine(Storage.DataDir, "error.log"), $"[{DateTime.Now:s}] {ex}{Environment.NewLine}");
        }
        catch { }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _tray?.Dispose();
        _mutex?.Dispose();
        base.OnExit(e);
    }
}
