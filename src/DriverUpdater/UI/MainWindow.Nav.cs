using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Threading;

namespace DriverUpdater.UI;

// The menu: a tree built from the pages themselves, so a page added to MainWindow.xaml appears
// in it with no other change. A TabItem with the Section style starts a branch; the pages that
// follow belong to it.
public partial class MainWindow
{
    readonly Dictionary<TabItem, TreeViewItem> navItems = [];
    DispatcherTimer? navDelay;

    void BuildNav()
    {
        // Closing the branch of the open page moves the selection to the branch: opening it
        // again shows the page as current again.
        Nav.AddHandler(TreeViewItem.ExpandedEvent, new RoutedEventHandler((_, e) =>
        {
            if (Tabs.SelectedItem is TabItem open && navItems.TryGetValue(open, out var item) && item.Parent == e.OriginalSource)
                item.IsSelected = true;
        }));
        var section = (Style)FindResource("Section");
        ItemsControl branch = Nav;
        foreach (var tab in Tabs.Items.OfType<TabItem>())
        {
            if (tab.Style == section)
            {
                var group = new TreeViewItem { Header = tab.Header, Style = (Style)FindResource("NavGroup") };
                Nav.Items.Add(group);
                branch = group;
                continue;
            }
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            label.Children.Add(new TextBlock
            {
                Text = tab.Tag as string ?? "", FontFamily = (System.Windows.Media.FontFamily)FindResource("Icons"),
                FontSize = 16, Width = 18, Margin = new Thickness(0, 0, 14, 0), VerticalAlignment = VerticalAlignment.Center,
            });
            label.Children.Add(new TextBlock { Text = (string)tab.Header, VerticalAlignment = VerticalAlignment.Center });
            var item = new TreeViewItem { Header = label, Tag = tab, Style = (Style)FindResource("NavPage") };
            System.Windows.Automation.AutomationProperties.SetName(item, (string)tab.Header);
            branch.Items.Add(item);
            navItems[tab] = item;
        }
        if (Tabs.SelectedItem is TabItem selected && navItems.TryGetValue(selected, out var current)) current.IsSelected = true;
    }

    // A page chosen in the menu opens; a branch title only opens or closes its branch. Going
    // through the menu with the arrow keys opens the page the user stops on, not every page on
    // the way (some read a lot when first opened).
    void OnNavSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        navDelay?.Stop();
        if (e.NewValue is not TreeViewItem { Tag: TabItem page } || Tabs.SelectedItem == page) return;
        if (!Keyboard.IsKeyDown(Key.Up) && !Keyboard.IsKeyDown(Key.Down) && !Keyboard.IsKeyDown(Key.Home) && !Keyboard.IsKeyDown(Key.End))
        {
            Tabs.SelectedItem = page;
            return;
        }
        navDelay ??= new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(350) };
        navDelay.Tick -= OnNavDelay;
        navDelay.Tick += OnNavDelay;
        navDelay.Start();
    }

    void OnNavDelay(object? sender, EventArgs e)
    {
        navDelay!.Stop();
        if (Nav.SelectedItem is TreeViewItem { Tag: TabItem page }) Tabs.SelectedItem = page;
    }

    // A page opened from elsewhere (a tile, a link) is shown as current in the menu.
    void ShowInNav(TabItem tab)
    {
        if (!navItems.TryGetValue(tab, out var item) || item.IsSelected) return;
        if (item.Parent is TreeViewItem group) group.IsExpanded = true;
        item.IsSelected = true;
        item.BringIntoView();
    }
}
