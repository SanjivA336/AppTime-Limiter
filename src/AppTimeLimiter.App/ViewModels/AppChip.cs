using System.Windows;
using System.Windows.Media;

namespace AppTimeLimiter.App.ViewModels;

/// <summary>One app shown on a group card: its icon if we have one, otherwise a letter badge.</summary>
public sealed class AppChip
{
    public AppChip(string name, ImageSource? icon)
    {
        Name = name;
        Icon = icon;
    }

    public string Name { get; }

    public ImageSource? Icon { get; }

    /// <summary>The letter shown when there is no icon.</summary>
    public string Initial => Name.Length > 0 ? Name[..1].ToUpperInvariant() : "?";

    public Visibility IconVisibility => Icon is null ? Visibility.Collapsed : Visibility.Visible;

    public Visibility InitialVisibility => Icon is null ? Visibility.Visible : Visibility.Collapsed;
}
