using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using Environments.Core;

namespace Environments.UI;

public partial class PreviewWindow : Window
{
    readonly Preset _preset;
    Plan? _plan;
    bool _done;

    public PreviewWindow(Preset preset)
    {
        InitializeComponent();
        _preset = preset;
        var brush = HexToBrushConverter.Parse(preset.Color);
        Badge.Background = brush;
        ApplyButton.Background = brush;
        BadgeIcon.Icon = preset.Icon;
        Header.Text = preset.Name;
        SubHeader.Text = "Ищу открытые программы…";
        ApplyButton.IsEnabled = false;
        KeyDown += (_, e) => { if (e.Key == Key.Escape) Close(); };
        Loaded += async (_, _) => await BuildPlan();
    }

    async Task BuildPlan()
    {
        _plan = await Task.Run(() => Planner.Build(App.Config, _preset));
        var verb = _preset.Minimize ? "Свернётся" : "Закроется";
        CloseTitle.Text = _preset.NewDesktop ? "Новый рабочий стол" : $"{verb} ({_plan.ToClose.Count})";
        CloseList.ItemsSource = _plan.ToClose;
        NothingToClose.Text = _preset.NewDesktop
            ? "Ничего не закроется: откроется отдельный рабочий стол, текущий останется как есть."
            : "Закрывать нечего.";
        NothingToClose.Visibility = _plan.ToClose.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        ToggleAll.Visibility = _plan.ToClose.Count > 1 ? Visibility.Visible : Visibility.Collapsed;

        // System and protected stuff always stays; listing it is noise.
        var kept = _plan.Kept
            .Where(k => k.Pid != Environment.ProcessId && !RuleMatcher.MatchesAny(App.Config.Settings.Protected, k.Name, k.Path, k.Titles))
            .Select(k => App.Config.AppFor(k)?.Name ?? k.Name).Distinct().ToList();
        KeptText.Text = kept.Count > 0 ? string.Join(" · ", kept) : "ничего, кроме системы";

        var launch = _plan.ToLaunch.Select(i => i.AlreadyRunning ? $"{i.App.Name} (уже запущено)" : i.App.Name).ToList();
        LaunchTitle.Visibility = LaunchText.Visibility = launch.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        LaunchText.Text = string.Join(" · ", launch);

        SubHeader.Text = ModeText();
        ApplyButton.IsEnabled = true;
        ApplyButton.Focus();
    }

    string ModeText() => _preset.NewDesktop ? "На новом рабочем столе" : _preset.Minimize ? "Лишнее свернётся" : _preset.Mode switch
    {
        CloseMode.Gentle => "Аккуратно: только попросить закрыться",
        CloseMode.Force => "Жёстко: не закрылось — будет убито",
        _ => "Умно: трей добивается, окна с «сохранить?» остаются",
    };

    void ToggleAll_Click(object sender, RoutedEventArgs e)
    {
        if (_plan == null) return;
        bool any = _plan.ToClose.Any(c => c.Selected);
        foreach (var c in _plan.ToClose) c.Selected = !any;
        ToggleAll.Content = any ? "Выбрать все" : "Снять все";
    }

    async void Apply_Click(object sender, RoutedEventArgs e)
    {
        if (_done) { Close(); return; }
        if (_plan == null) return;
        ApplyButton.IsEnabled = false;
        CancelButton.IsEnabled = false;
        Topmost = false;

        if (_preset.NewDesktop)
        {
            // This window stays on the old desktop; the result goes to a notification instead.
            Hide();
            var result = await Runner.Execute(_plan);
            App.Instance.Notify(_preset.Name, result.Summary());
            Close();
            return;
        }

        var progress = new Progress<string>(s => SubHeader.Text = s);
        var report = await Runner.Execute(_plan, progress);

        _done = true;
        SubHeader.Text = "Готово";
        ResultText.Text = report.Details();
        ResultBox.Visibility = Visibility.Visible;
        CloseList.IsEnabled = false;
        ApplyButton.Content = "Готово";
        ApplyButton.IsEnabled = true;
        CancelButton.Visibility = Visibility.Collapsed;
        UndoButton.Visibility = report.Closed.Count + report.KilledFromTray.Count > 0 ? Visibility.Visible : Visibility.Collapsed;
        Activate();
    }

    async void Undo_Click(object sender, RoutedEventArgs e)
    {
        UndoButton.IsEnabled = false;
        var report = await Runner.Undo(App.Config);
        ResultText.Text = "Вернул: " + report.Summary();
    }

    void Cancel_Click(object sender, RoutedEventArgs e) => Close();
}
