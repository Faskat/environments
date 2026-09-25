using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Environments.Core;
using Microsoft.Win32;

namespace Environments.UI;

/// <summary>A row for an app or a group, used by the preset editor, the app list and group membership.</summary>
public class ItemRow : Observable
{
    private bool _keep, _launch, _checked;
    private string? _keptByGroupHint;
    private string _title = "";
    private string _icon = "";
    private ImageSource? _image;

    public string Id { get; init; } = "";
    public bool IsGroup { get; init; }
    public string Title { get => _title; set => Set(ref _title, value); }
    public string Icon { get => _icon; set => Set(ref _icon, value); }
    public ImageSource? Image { get => _image; set => Set(ref _image, value); }
    public string Subtitle { get; init; } = "";
    public bool CanLaunch { get; init; }
    public Action<ItemRow>? Changed { get; set; }

    public bool Keep
    {
        get => _keep;
        set
        {
            if (!Set(ref _keep, value)) return;
            if (!value && _launch) { _launch = false; Raise(nameof(Launch)); }
            Changed?.Invoke(this);
        }
    }

    public bool Launch
    {
        get => _launch;
        set
        {
            if (!Set(ref _launch, value)) return;
            if (value && !_keep) { _keep = true; Raise(nameof(Keep)); }
            Changed?.Invoke(this);
        }
    }

    public bool Checked
    {
        get => _checked;
        set { if (Set(ref _checked, value)) Changed?.Invoke(this); }
    }

    public string? KeptByGroupHint { get => _keptByGroupHint; set => Set(ref _keptByGroupHint, value); }

    public void Load(bool keep, bool launch)
    {
        _keep = keep; _launch = launch;
        Raise(nameof(Keep)); Raise(nameof(Launch));
    }
}

public partial class MainWindow : Window
{
    static readonly string[] Palette = { "#57D68D", "#FF6B6B", "#FFC857", "#7C8CFF", "#FF9F43", "#C38BFF", "#4FD1E8", "#9AA0A6" };

    static Config Cfg => App.Config;

    readonly ObservableCollection<Preset> _presets = new();
    readonly ObservableCollection<ItemRow> _groupRows = new();
    readonly ObservableCollection<ItemRow> _appRows = new();
    readonly ObservableCollection<ItemRow> _appList = new();
    readonly ObservableCollection<ItemRow> _members = new();
    readonly DispatcherTimer _saveTimer;

    Preset? _preset;
    AppDef? _app;
    AppGroup? _group;
    bool _loading;
    HashSet<string> _runningNames = new(StringComparer.OrdinalIgnoreCase);

    public MainWindow()
    {
        InitializeComponent();
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(400) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); App.Instance.SaveConfig(); UpdateHotkeyWarning(); };

        Swatches.ItemsSource = Palette;
        PresetList.ItemsSource = _presets;
        GroupRows.ItemsSource = _groupRows;
        AppRows.ItemsSource = _appRows;
        AppList.ItemsSource = _appList;
        GroupList.ItemsSource = Cfg.Groups;
        GroupMembers.ItemsSource = _members;

        ReloadAll();
        Closing += (_, _) => { if (_saveTimer.IsEnabled) { _saveTimer.Stop(); App.Instance.SaveConfig(); } };
    }

    void ReloadAll()
    {
        _runningNames = WindowScanner.SnapshotProcesses().Values.Select(p => p.Name).ToHashSet(StringComparer.OrdinalIgnoreCase);
        _presets.Clear();
        foreach (var p in Cfg.Presets) _presets.Add(p);
        PresetList.SelectedIndex = _presets.Count > 0 ? 0 : -1;
        RefreshAppList();
        GroupList.ItemsSource = null;
        GroupList.ItemsSource = Cfg.Groups;
        LoadSettings();
    }

    void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    // ---------- navigation ----------

    void Tab_Checked(object sender, RoutedEventArgs e)
    {
        if (PagePresets == null) return;
        PagePresets.Visibility = TabPresets.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageApps.Visibility = TabApps.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageGroups.Visibility = TabGroups.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = TabSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;

        // Other tabs may have changed apps and groups: rebuild the preset rows on the way back.
        if (TabPresets.IsChecked == true) LoadPreset(_preset);
        if (TabGroups.IsChecked == true && GroupList.SelectedIndex < 0 && Cfg.Groups.Count > 0) GroupList.SelectedIndex = 0;
        if (TabApps.IsChecked == true && AppList.SelectedIndex < 0 && _appList.Count > 0) AppList.SelectedIndex = 0;
    }

    void Undo_Click(object sender, RoutedEventArgs e) => App.Instance.Undo();

    // ---------- presets ----------

    void PresetList_SelectionChanged(object sender, SelectionChangedEventArgs e) => LoadPreset(PresetList.SelectedItem as Preset);

    void LoadPreset(Preset? p)
    {
        _preset = p;
        PresetEditor.Visibility = p == null ? Visibility.Hidden : Visibility.Visible;
        if (p == null) return;

        _loading = true;
        PresetGlyph.Icon = p.Icon;
        PresetName.Text = p.Name;
        WaitSeconds.Text = p.WaitSeconds.ToString();
        HotkeyBox.Text = p.Hotkey;
        NewDesktopBox.IsChecked = p.NewDesktop;
        MinimizeBox.IsChecked = p.Minimize;
        UpdateCloseOptions();
        BackgroundBox.IsChecked = p.IncludeBackground;
        ModeGentle.IsChecked = p.Mode == CloseMode.Gentle;
        ModeSmart.IsChecked = p.Mode == CloseMode.Smart;
        ModeForce.IsChecked = p.Mode == CloseMode.Force;
        ApplyPresetColor();

        _groupRows.Clear();
        foreach (var g in Cfg.Groups)
        {
            var members = g.AppIds.Select(Cfg.FindApp).Where(a => a != null).ToList();
            var row = new ItemRow
            {
                Id = g.Id, IsGroup = true, Title = g.Name, Icon = g.Icon,
                Subtitle = string.Join(", ", members.Select(a => a!.Name)),
                CanLaunch = members.Any(a => a!.CanLaunch),
            };
            row.Load(p.Keep.Contains(g.Id), p.Launch.Contains(g.Id));
            row.Changed = OnRowChanged;
            _groupRows.Add(row);
        }

        _appRows.Clear();
        foreach (var a in Cfg.Apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var row = new ItemRow
            {
                Id = a.Id, Title = a.Name, Icon = a.Icon, Image = IconCache.ForApp(a),
                Subtitle = a.Rules.Any(r => r.Kind == RuleKind.Process && _runningNames.Contains(r.Value)) ? "запущено" : "",
                CanLaunch = a.CanLaunch,
            };
            row.Load(p.Keep.Contains(a.Id), p.Launch.Contains(a.Id));
            row.Changed = OnRowChanged;
            _appRows.Add(row);
        }
        UpdateGroupHints();
        ApplySearch();
        UpdateHotkeyWarning();
        _loading = false;
    }

    void OnRowChanged(ItemRow row)
    {
        if (_preset == null || _loading) return;
        var all = _groupRows.Concat(_appRows).ToList();
        _preset.Keep = all.Where(r => r.Keep).Select(r => r.Id).ToList();
        _preset.Launch = all.Where(r => r.Launch).Select(r => r.Id).ToList();
        UpdateGroupHints();
        ScheduleSave();
    }

    void UpdateGroupHints()
    {
        var keptGroups = _groupRows.Where(r => r.Keep).Select(r => Cfg.FindGroup(r.Id)).Where(g => g != null).ToList();
        foreach (var row in _appRows)
        {
            var g = keptGroups.FirstOrDefault(g => g!.AppIds.Contains(row.Id));
            row.KeptByGroupHint = !row.Keep && g != null ? $"Остаётся вместе с группой «{g.Name}»" : null;
        }
    }

    void Search_Changed(object sender, TextChangedEventArgs e)
    {
        SearchPlaceholder.Visibility = Search.Text.Length == 0 ? Visibility.Visible : Visibility.Collapsed;
        ApplySearch();
    }

    void ApplySearch()
    {
        var q = Search.Text.Trim();
        Predicate<object>? filter = q.Length == 0 ? null
            : o => o is ItemRow r && (r.Title.Contains(q, StringComparison.CurrentCultureIgnoreCase)
                                      || r.Subtitle.Contains(q, StringComparison.CurrentCultureIgnoreCase));
        CollectionViewSource.GetDefaultView(_groupRows).Filter = filter;
        CollectionViewSource.GetDefaultView(_appRows).Filter = filter;
    }

    void ApplyPresetColor()
    {
        if (_preset == null) return;
        var brush = HexToBrushConverter.Parse(_preset.Color);
        PresetBadge.Background = brush;
        RunButton.Background = brush;
    }

    void PresetField_Changed(object sender, TextChangedEventArgs e)
    {
        if (_preset == null || _loading) return;
        _preset.Name = PresetName.Text.Trim().Length > 0 ? PresetName.Text.Trim() : "Без названия";
        if (int.TryParse(WaitSeconds.Text, out var s)) _preset.WaitSeconds = s;
        ScheduleSave();
    }

    void PresetIcon_Click(object sender, RoutedEventArgs e)
    {
        if (_preset == null) return;
        IconPicker.Show(PresetBadge, _preset.Icon, icon =>
        {
            _preset.Icon = icon;
            PresetGlyph.Icon = icon;
            ScheduleSave();
        });
    }

    void PresetCheck_Changed(object sender, RoutedEventArgs e)
    {
        if (_preset == null || _loading) return;
        _preset.Minimize = MinimizeBox.IsChecked == true;
        _preset.IncludeBackground = BackgroundBox.IsChecked == true;
        _preset.NewDesktop = NewDesktopBox.IsChecked == true;
        UpdateCloseOptions();
        ScheduleSave();
    }

    /// <summary>A preset on a new desktop closes nothing, so the closing options do not apply.</summary>
    void UpdateCloseOptions()
    {
        bool closes = NewDesktopBox.IsChecked != true;
        foreach (var c in new Control[] { ModeGentle, ModeSmart, ModeForce, WaitSeconds, MinimizeBox, BackgroundBox })
        {
            c.IsEnabled = closes;
            c.Opacity = closes ? 1 : 0.4;
        }
    }

    void Mode_Checked(object sender, RoutedEventArgs e)
    {
        if (_preset == null || _loading) return;
        _preset.Mode = ModeForce.IsChecked == true ? CloseMode.Force : ModeGentle.IsChecked == true ? CloseMode.Gentle : CloseMode.Smart;
        ScheduleSave();
    }

    void Swatch_Click(object sender, RoutedEventArgs e)
    {
        if (_preset == null || sender is not Button { Tag: string hex }) return;
        _preset.Color = hex;
        ApplyPresetColor();
        ScheduleSave();
    }

    void HotkeyBox_GotFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        App.Instance.SuspendHotkeys();
        HotkeyBox.Text = "Нажми сочетание…";
    }

    void HotkeyBox_LostFocus(object sender, KeyboardFocusChangedEventArgs e)
    {
        HotkeyBox.Text = _preset?.Hotkey ?? "";
        App.Instance.ResumeHotkeys();
        UpdateHotkeyWarning();
    }

    void HotkeyBox_PreviewKeyDown(object sender, KeyEventArgs e)
    {
        e.Handled = true;
        if (_preset == null) return;
        var key = e.Key == Key.System ? e.SystemKey : e.Key;
        if (Hotkey.IsModifier(key)) return;
        if (key == Key.Escape) { Keyboard.ClearFocus(); FocusManager.SetFocusedElement(this, this); return; }
        if (key is Key.Back or Key.Delete) { SetHotkey(""); return; }
        var mods = Keyboard.Modifiers;
        bool isF = key >= Key.F1 && key <= Key.F24;
        if (mods == ModifierKeys.None && !isF) { HotkeyBox.Text = "Нужен Ctrl, Alt, Shift или Win"; return; }
        SetHotkey(Hotkey.Format(mods, key));
    }

    void SetHotkey(string hotkey)
    {
        if (_preset == null) return;
        _preset.Hotkey = hotkey;
        HotkeyBox.Text = hotkey.Length > 0 ? hotkey : "Нажми сочетание…";
        ScheduleSave();
    }

    void ClearHotkey_Click(object sender, RoutedEventArgs e) { SetHotkey(""); HotkeyBox.Text = ""; }

    void UpdateHotkeyWarning()
    {
        string? text = null;
        if (_preset is { Hotkey.Length: > 0 })
        {
            var twin = Cfg.Presets.FirstOrDefault(p => p != _preset && p.Hotkey.Equals(_preset.Hotkey, StringComparison.OrdinalIgnoreCase));
            if (twin != null) text = $"Это же сочетание стоит на «{twin.Name}».";
            else if (App.Instance.FailedHotkeys.Any(f => f.StartsWith(_preset.Hotkey + " ", StringComparison.OrdinalIgnoreCase)))
                text = "Сочетание занято другой программой — выбери другое.";
        }
        HotkeyWarning.Text = text ?? "";
        HotkeyWarningBox.Visibility = text == null ? Visibility.Collapsed : Visibility.Visible;
    }

    void RunPreset_Click(object sender, RoutedEventArgs e)
    {
        if (_preset == null) return;
        if (_saveTimer.IsEnabled) { _saveTimer.Stop(); App.Instance.SaveConfig(); }
        App.Instance.RunPreset(_preset, preview: true);
    }

    void NewPreset_Click(object sender, RoutedEventArgs e)
    {
        var p = new Preset
        {
            Id = Cfg.NewId("preset"), Name = "Новый пресет", Icon = "bolt",
            Color = Palette[Cfg.Presets.Count % Palette.Length],
        };
        AddPreset(p);
    }

    void DuplicatePreset_Click(object sender, RoutedEventArgs e)
    {
        if (_preset == null) return;
        var copy = Storage.ImportPreset(Storage.ExportPreset(_preset))!;
        copy.Id = Cfg.NewId(_preset.Id);
        copy.Name = _preset.Name + " (копия)";
        copy.Hotkey = "";
        AddPreset(copy);
    }

    void AddPreset(Preset p)
    {
        Cfg.Presets.Add(p);
        _presets.Add(p);
        PresetList.SelectedItem = p;
        ScheduleSave();
        PresetName.Focus();
        PresetName.SelectAll();
    }

    void DeletePreset_Click(object sender, RoutedEventArgs e)
    {
        if (_preset == null) return;
        if (MessageBox.Show(this, $"Удалить пресет «{_preset.Name}»?", "Environments", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        Cfg.Presets.Remove(_preset);
        int idx = PresetList.SelectedIndex;
        _presets.Remove(_preset);
        PresetList.SelectedIndex = Math.Min(idx, _presets.Count - 1);
        ScheduleSave();
    }

    void ExportPreset_Click(object sender, RoutedEventArgs e)
    {
        if (_preset == null) return;
        var dlg = new SaveFileDialog { FileName = _preset.Name + ".json", Filter = "Пресет (*.json)|*.json" };
        if (dlg.ShowDialog(this) == true) File.WriteAllText(dlg.FileName, Storage.ExportPreset(_preset));
    }

    void ImportPreset_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "Пресет (*.json)|*.json" };
        if (dlg.ShowDialog(this) != true) return;
        try
        {
            var p = Storage.ImportPreset(File.ReadAllText(dlg.FileName)) ?? throw new InvalidDataException();
            p.Id = Cfg.NewId(p.Id.Length > 0 ? p.Id : "preset");
            if (Cfg.Presets.Any(x => x.Hotkey.Equals(p.Hotkey, StringComparison.OrdinalIgnoreCase))) p.Hotkey = "";
            AddPreset(p);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "Не получилось прочитать пресет: " + ex.Message, "Environments");
        }
    }

    void AddFromRunning_Click(object sender, RoutedEventArgs e)
    {
        if (_preset == null) return;
        var picked = RunningPicker.Pick(this);
        if (picked.Count == 0) return;
        foreach (var app in picked.Select(EnsureApp))
            if (!_preset.Keep.Contains(app.Id)) _preset.Keep.Add(app.Id);
        App.Instance.SaveConfig();
        RefreshAppList();
        LoadPreset(_preset);
    }

    /// <summary>Finds the app definition for a running process or creates one from it.</summary>
    static AppDef EnsureApp(RunningApp running)
    {
        if (Cfg.AppFor(running) is { } existing) return existing;
        string name = running.Name;
        try
        {
            if (running.Path != null && FileVersionInfo.GetVersionInfo(running.Path).FileDescription is { Length: > 0 } d) name = d;
        }
        catch { }
        var app = new AppDef
        {
            Id = Cfg.NewId(running.Name), Name = name, Icon = "app",
            Rules = { AppRule.Proc(running.Name) },
        };
        if (running.Path != null) app.Launch.Add(new LaunchTarget(running.Path));
        Cfg.Apps.Add(app);
        return app;
    }

    // ---------- apps ----------

    void RefreshAppList()
    {
        var selected = _app?.Id;
        _appList.Clear();
        foreach (var a in Cfg.Apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
            _appList.Add(new ItemRow { Id = a.Id, Title = a.Name, Icon = a.Icon, Image = IconCache.ForApp(a) });
        AppList.SelectedItem = _appList.FirstOrDefault(r => r.Id == selected);
    }

    void AppList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _app = AppList.SelectedItem is ItemRow r ? Cfg.FindApp(r.Id) : null;
        AppEditor.Visibility = _app == null ? Visibility.Hidden : Visibility.Visible;
        if (_app == null) return;
        _loading = true;
        AppGlyph.Icon = _app.Icon;
        AppName.Text = _app.Name;
        AppRules.Text = string.Join(Environment.NewLine, _app.Rules);
        AppLaunch.Text = string.Join(Environment.NewLine, _app.Launch);
        AppLaunchStatus.Text = "";
        var groups = Cfg.Groups.Where(g => g.AppIds.Contains(_app.Id)).Select(g => g.Name).ToList();
        AppGroupsText.Text = groups.Count > 0 ? string.Join(", ", groups) + " — меняется во вкладке «Группы»" : "Ни в одной. Добавить можно во вкладке «Группы».";
        _loading = false;
    }

    void AppField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_app == null || _loading) return;
        if (AppName.Text.Trim().Length > 0) _app.Name = AppName.Text.Trim();
        _app.Rules = AppRules.Text.Split('\n').Select(l => AppRule.TryParse(l, out var r) ? r : null).OfType<AppRule>().ToList();
        _app.Launch = AppLaunch.Text.Split('\n').Select(l => LaunchTarget.TryParse(l, out var t) ? t : null).OfType<LaunchTarget>().ToList();
        if (AppList.SelectedItem is ItemRow row)
        {
            row.Title = _app.Name;
            row.Icon = _app.Icon;
            row.Image = IconCache.ForApp(_app);
        }
        ScheduleSave();
    }

    void AppIcon_Click(object sender, RoutedEventArgs e)
    {
        if (_app == null) return;
        IconPicker.Show(AppIconButton, _app.Icon, icon =>
        {
            _app.Icon = icon;
            AppGlyph.Icon = icon;
            if (AppList.SelectedItem is ItemRow row) row.Icon = icon;
            ScheduleSave();
        });
    }

    void TestLaunch_Click(object sender, RoutedEventArgs e)
    {
        if (_app == null) return;
        AppField_LostFocus(sender, e);
        var err = Launcher.Start(_app);
        AppLaunchStatus.Text = err == null ? "Запустилось." : "Не запустилось: " + err;
    }

    void NewApp_Click(object sender, RoutedEventArgs e)
    {
        var app = new AppDef { Id = Cfg.NewId("app"), Name = "Новое приложение", Icon = "app" };
        Cfg.Apps.Add(app);
        _app = app;
        RefreshAppList();
        ScheduleSave();
        AppName.Focus();
        AppName.SelectAll();
    }

    void AppsAddFromRunning_Click(object sender, RoutedEventArgs e)
    {
        var picked = RunningPicker.Pick(this);
        if (picked.Count == 0) return;
        _app = picked.Select(EnsureApp).Last();
        RefreshAppList();
        ScheduleSave();
    }

    void DeleteApp_Click(object sender, RoutedEventArgs e)
    {
        if (_app == null) return;
        if (MessageBox.Show(this, $"Удалить «{_app.Name}» из списка? Сама программа останется на компьютере.", "Environments",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var id = _app.Id;
        Cfg.Apps.Remove(_app);
        foreach (var g in Cfg.Groups) g.AppIds.Remove(id);
        foreach (var p in Cfg.Presets) { p.Keep.Remove(id); p.Launch.Remove(id); }
        _app = null;
        RefreshAppList();
        ScheduleSave();
    }

    // ---------- groups ----------

    void GroupList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        _group = GroupList.SelectedItem as AppGroup;
        GroupEditor.Visibility = _group == null ? Visibility.Hidden : Visibility.Visible;
        if (_group == null) return;
        _loading = true;
        GroupGlyph.Icon = _group.Icon;
        GroupName.Text = _group.Name;
        _members.Clear();
        foreach (var a in Cfg.Apps.OrderBy(a => a.Name, StringComparer.CurrentCultureIgnoreCase))
        {
            var row = new ItemRow { Id = a.Id, Title = a.Name, Icon = a.Icon, Image = IconCache.ForApp(a) };
            row.Checked = _group.AppIds.Contains(a.Id);
            row.Changed = OnMemberChanged;
            _members.Add(row);
        }
        _loading = false;
    }

    void OnMemberChanged(ItemRow row)
    {
        if (_group == null || _loading) return;
        _group.AppIds = _members.Where(m => m.Checked).Select(m => m.Id).ToList();
        ScheduleSave();
    }

    void GroupField_LostFocus(object sender, RoutedEventArgs e)
    {
        if (_group == null || _loading) return;
        if (GroupName.Text.Trim().Length > 0) _group.Name = GroupName.Text.Trim();
        ScheduleSave();
    }

    void GroupIcon_Click(object sender, RoutedEventArgs e)
    {
        if (_group == null) return;
        IconPicker.Show(GroupIconButton, _group.Icon, icon =>
        {
            _group.Icon = icon;
            GroupGlyph.Icon = icon;
            ScheduleSave();
        });
    }

    void NewGroup_Click(object sender, RoutedEventArgs e)
    {
        var g = new AppGroup { Id = Cfg.NewId("g-group"), Name = "Новая группа", Icon = "work" };
        Cfg.Groups.Add(g);
        GroupList.ItemsSource = null;
        GroupList.ItemsSource = Cfg.Groups;
        GroupList.SelectedItem = g;
        ScheduleSave();
        GroupName.Focus();
        GroupName.SelectAll();
    }

    void DeleteGroup_Click(object sender, RoutedEventArgs e)
    {
        if (_group == null) return;
        if (MessageBox.Show(this, $"Удалить группу «{_group.Name}»? Приложения останутся.", "Environments",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        var id = _group.Id;
        Cfg.Groups.Remove(_group);
        foreach (var p in Cfg.Presets) { p.Keep.Remove(id); p.Launch.Remove(id); }
        GroupList.ItemsSource = null;
        GroupList.ItemsSource = Cfg.Groups;
        ScheduleSave();
    }

    // ---------- settings ----------

    void LoadSettings()
    {
        _loading = true;
        AutostartBox.IsChecked = Autostart.IsEnabled;
        HotkeyPreviewBox.IsChecked = Cfg.Settings.HotkeyShowsPreview;
        NotificationsBox.IsChecked = Cfg.Settings.Notifications;
        ProtectedBox.Text = string.Join(Environment.NewLine, Cfg.Settings.Protected);
        _loading = false;
    }

    void Setting_Changed(object sender, RoutedEventArgs e)
    {
        if (_loading) return;
        if (sender == AutostartBox)
        {
            try { Autostart.IsEnabled = AutostartBox.IsChecked == true; }
            catch (Exception ex) { MessageBox.Show(this, "Не получилось: " + ex.Message, "Environments"); }
            return;
        }
        Cfg.Settings.HotkeyShowsPreview = HotkeyPreviewBox.IsChecked == true;
        Cfg.Settings.Notifications = NotificationsBox.IsChecked == true;
        ScheduleSave();
    }

    void Protected_LostFocus(object sender, RoutedEventArgs e)
    {
        Cfg.Settings.Protected = ProtectedBox.Text.Split('\n')
            .Select(l => AppRule.TryParse(l, out var r) ? r : null).OfType<AppRule>().ToList();
        ScheduleSave();
    }

    void OpenData_Click(object sender, RoutedEventArgs e) =>
        Process.Start(new ProcessStartInfo(Storage.DataDir) { UseShellExecute = true });

    void Reset_Click(object sender, RoutedEventArgs e)
    {
        if (MessageBox.Show(this, "Вернуть встроенные пресеты, приложения и группы? Твои изменения пропадут.", "Environments",
                MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
        App.Instance.ReplaceConfig(Defaults.Create());
        GroupList.ItemsSource = Cfg.Groups;
        ReloadAll();
    }
}
