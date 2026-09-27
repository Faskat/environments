using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Environments.Core;

namespace Environments.UI;

public partial class RunningPicker : Window
{
    readonly List<Candidate> _items;

    RunningPicker()
    {
        InitializeComponent();
        var cfg = App.Config;
        var snap = WindowScanner.Scan();
        var self = Environment.ProcessId;
        // Candidate doubles as a selectable row; nothing is pre-selected here.
        // Protected apps are listed too (last): they never close, but a preset may still need to launch them.
        _items = snap.Apps
            .Where(a => a.Pid != self)
            .GroupBy(a => a.Name, StringComparer.OrdinalIgnoreCase)
            .Select(g => new Candidate
            {
                App = g.First(), Def = cfg.AppFor(g.First()), Selected = false,
                IsProtected = RuleMatcher.MatchesAny(cfg.Settings.Protected, g.First().Name, g.First().Path, g.First().Titles),
            })
            .OrderBy(c => c.IsProtected)
            .ThenBy(c => c.DisplayName, StringComparer.CurrentCultureIgnoreCase)
            .ToList();
        List.ItemsSource = _items;
    }

    public static List<RunningApp> Pick(Window owner)
    {
        var w = new RunningPicker { Owner = owner };
        return w.ShowDialog() == true ? w._items.Where(c => c.Selected).Select(c => c.App).ToList() : new();
    }

    void Ok_Click(object sender, RoutedEventArgs e) => DialogResult = true;
}
