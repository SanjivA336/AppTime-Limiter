using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Media;

namespace HardCap.App.Views;

/// <summary>
/// The navigation area wraps every page in its own ScrollViewer, which offers the page unlimited height. Then a page's
/// own list grows to fit everything in it, has nothing to scroll, and still swallows the mouse wheel, so scrolling did
/// nothing. Pinning the page to the visible height gives the list a real limit, so it scrolls properly.
/// </summary>
internal static class PageHeightFix
{
    public static void Apply(Page page)
    {
        DependencyObject? ancestor = VisualTreeHelper.GetParent(page);
        while (ancestor is not null and not ScrollViewer)
        {
            ancestor = VisualTreeHelper.GetParent(ancestor);
        }

        if (ancestor is ScrollViewer host)
        {
            page.SetBinding(FrameworkElement.HeightProperty, new Binding(nameof(ScrollViewer.ViewportHeight)) { Source = host });
        }
    }
}
