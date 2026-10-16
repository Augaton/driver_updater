using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;

namespace DriverUpdater.UI;

/// <summary>
/// English for the texts written in the XAML, put in as each element appears (templates and
/// lists included). French stays the source: a text is translated when Localization/en.json
/// has it. Texts bound to data are left alone: the code translates those itself (L.T).
/// </summary>
public static class Translator
{
    public static void Install() =>
        EventManager.RegisterClassHandler(typeof(FrameworkElement), FrameworkElement.LoadedEvent,
            new RoutedEventHandler((sender, _) => Translate((FrameworkElement)sender)), handledEventsToo: true);

    static void Translate(FrameworkElement element)
    {
        Swap(element, AutomationProperties.NameProperty);
        Swap(element, FrameworkElement.ToolTipProperty);
        switch (element)
        {
            case TextBlock text:
                Swap(text, TextBlock.TextProperty);
                // Text written as runs: each run on its own (a TextBlock bound to data has none of ours).
                if (text.ReadLocalValue(TextBlock.TextProperty) == DependencyProperty.UnsetValue && !BindingOperations.IsDataBound(text, TextBlock.TextProperty))
                    foreach (var run in text.Inlines.OfType<Run>()) Swap(run, Run.TextProperty);
                break;
            case PageHeader header:
                Swap(header, PageHeader.TitleProperty);
                Swap(header, PageHeader.DescriptionProperty);
                Swap(header, PageHeader.NoteProperty);
                break;
            case DataGrid grid:
                // On the columns themselves: the grid makes its header cells again when it likes.
                foreach (var column in grid.Columns)
                    if (column.Header is string french && L.TryTranslate(french, out var english)) column.Header = english;
                break;
            case HeaderedItemsControl headered:
                Swap(headered, HeaderedItemsControl.HeaderProperty);
                break;
            case Window window:
                Swap(window, Window.TitleProperty);
                break;
            case HeaderedContentControl headered:
                Swap(headered, HeaderedContentControl.HeaderProperty);
                Swap(headered, ContentControl.ContentProperty);
                break;
            case ContentControl content:
                Swap(content, ContentControl.ContentProperty);
                Swap(content, ContentControl.ContentStringFormatProperty);
                break;
        }
    }

    static void Swap(DependencyObject target, DependencyProperty property)
    {
        if (BindingOperations.IsDataBound(target, property)) return;
        if (target.GetValue(property) is string french && L.TryTranslate(french, out var english)) target.SetCurrentValue(property, english);
    }
}
