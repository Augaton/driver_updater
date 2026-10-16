using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Markup;

namespace DriverUpdater.UI;

/// <summary>
/// English for the texts written in the XAML. French stays the source: a text is translated
/// when Localization/en.json has it. Texts bound to data are left alone: the code translates
/// those itself (L.T).
/// </summary>
public static class Translator
{
    /// <summary>
    /// Every text of a window, its pages included, right after InitializeComponent: they are all
    /// in its logical tree by then, shown or not. Templates are not; their few texts use
    /// {local:Translate}. Not done on Loaded: WPF only raises it on elements that have a Loaded
    /// handler of their own.
    /// </summary>
    public static void Translate(DependencyObject root)
    {
        if (L.Language != "en") return;
        Walk(root);
    }

    static void Walk(DependencyObject node)
    {
        switch (node)
        {
            case FrameworkElement element:
                TranslateElement(element);
                break;
            case Run run:
                Swap(run, Run.TextProperty);
                break;
        }
        foreach (var child in LogicalTreeHelper.GetChildren(node).OfType<DependencyObject>())
            Walk(child);
    }

    static void TranslateElement(FrameworkElement element)
    {
        Swap(element, AutomationProperties.NameProperty);
        Swap(element, FrameworkElement.ToolTipProperty);
        switch (element)
        {
            case TextBlock text:
                Swap(text, TextBlock.TextProperty);
                break;
            case PageHeader header:
                Swap(header, PageHeader.TitleProperty);
                Swap(header, PageHeader.DescriptionProperty);
                Swap(header, PageHeader.NoteProperty);
                break;
            case DataGrid grid:
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

/// <summary>
/// A text written in a template, where Translator does not reach: {local:Translate 'Ouvrir {0}'}.
/// The tests find its key in the XAML, like the other texts.
/// </summary>
[MarkupExtensionReturnType(typeof(string))]
public sealed class TranslateExtension(string french) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) =>
        L.TryTranslate(french, out var english) ? english : french;
}
