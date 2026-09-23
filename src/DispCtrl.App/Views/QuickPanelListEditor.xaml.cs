using System.Collections.ObjectModel;
using DispCtrl.App.Services;
using DispCtrl.App.ViewModels;
using DispCtrl.Core.Settings;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace DispCtrl.App.Views;

/// <summary>One row of a list editor: an item of the panel, as the page shows it.</summary>
public sealed class QuickPanelEditorItem
{
    public string Id { get; set; } = "";
    public string Label { get; set; } = "";
    public string Glyph { get; set; } = "";
    public string Hint { get; set; } = "";
}

/// <summary>
/// Edits one of the panel's ordered lists: what is shown, and in what order.
/// </summary>
/// <remarks>
/// Shows only what is switched on, in the order the panel draws it, the way
/// Windows' own "edit quick settings" does: drag to reorder, the cross to take
/// something out, and Add for everything that is not there yet. What is hidden
/// is not a row with its switch off - a list of forty rows, half of them off,
/// is a list nobody can find their place in.
/// <para>
/// One control for all four lists, and for the tiles people make themselves,
/// so a new kind of item needs no new editor.
/// </para>
/// </remarks>
public sealed partial class QuickPanelListEditor : UserControl
{
    public ObservableCollection<QuickPanelEditorItem> Items { get; } = [];

    public string AutomationName { get; private set; } = "";
    public string AddAutomationName { get; private set; } = "";

    private MainViewModel? _vm;
    private QuickPanelGroup _group;

    public QuickPanelListEditor() => InitializeComponent();

    /// <summary>Shows a group's list, and what could be added to it.</summary>
    public void Load(MainViewModel vm, QuickPanelGroup group)
    {
        _vm = vm;
        _group = group;
        AutomationName = $"QuickList {group}";
        AddAutomationName = $"QuickAdd {group}";

        // Set after the control has loaded, so its bindings are told directly.
        Bindings.Update();
        Refresh();
    }

    private void Refresh()
    {
        if (_vm is null) return;

        Items.Clear();
        AddMenu.Items.Clear();

        foreach (QuickPanelItem item in _vm.QuickPanel.List(_group))
        {
            QuickPanelEditorItem? row = Describe(item.Id);
            if (row is null) continue;

            if (item.Visible)
            {
                Items.Add(row);
                continue;
            }

            var add = new MenuFlyoutItem
            {
                Text = row.Label,
                Icon = new FontIcon { Glyph = row.Glyph },
                Tag = row.Id,
            };
            ToolTipService.SetToolTip(add, row.Hint);
            add.Click += OnAdd;
            AddMenu.Items.Add(add);
        }

        Empty.Visibility = Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        AddButton.IsEnabled = AddMenu.Items.Count > 0;
    }

    private QuickPanelEditorItem? Describe(string id)
    {
        if (_vm is null) return null;

        if (_group == QuickPanelGroup.Tiles && _vm.QuickPanel.Custom(id) is { } custom)
        {
            return new QuickPanelEditorItem
            {
                Id = id,
                Label = custom.Label.Length > 0 ? custom.Label : "Untitled",
                Glyph = QuickPanelCommands.Glyph(custom.Glyph),
                Hint = "Your tile. " + custom.Hint,
            };
        }

        QuickPanelCatalog.Entry? e = QuickPanelCatalog.Find(_group, id);
        return e is null ? null : new QuickPanelEditorItem { Id = e.Id, Label = e.Label, Glyph = e.Glyph, Hint = e.Hint };
    }

    private void Save(IEnumerable<string> order)
    {
        if (_vm is null) return;
        _vm.ReorderQuickPanel(_group, order.ToList());
        Refresh();
    }

    private void OnDragCompleted(ListViewBase sender, DragItemsCompletedEventArgs args) =>
        Save(Items.Select(i => i.Id));

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        // At the end of what is shown, which is where somebody who has just
        // asked for it will look.
        if ((sender as FrameworkElement)?.Tag is string id) Save(Items.Select(i => i.Id).Append(id));
    }

    private void OnRemove(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is string id) Save(Items.Select(i => i.Id).Where(i => i != id));
    }

    private void OnMoveUp(object sender, RoutedEventArgs e) => Move(sender, -1);

    private void OnMoveDown(object sender, RoutedEventArgs e) => Move(sender, +1);

    private void Move(object sender, int by)
    {
        if ((sender as FrameworkElement)?.Tag is not string id) return;

        var order = Items.Select(i => i.Id).ToList();
        int from = order.IndexOf(id);
        int to = Math.Clamp(from + by, 0, order.Count - 1);
        if (from < 0 || from == to) return;

        order.RemoveAt(from);
        order.Insert(to, id);
        Save(order);
    }
}
