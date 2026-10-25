using System.Windows;
using System.Windows.Controls;

namespace HardCap.App.Controls;

/// <summary>The countdown screen shared by the locked-group, settings and quit waits. Bind its DataContext to a WaitCountdown.</summary>
public partial class WaitCountdownView : UserControl
{
    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(WaitCountdownView), new PropertyMetadata(string.Empty));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(WaitCountdownView), new PropertyMetadata(string.Empty));

    public WaitCountdownView()
    {
        InitializeComponent();
    }

    /// <summary>The heading, such as "This group is locked".</summary>
    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    /// <summary>The line under the heading, saying what the wait is for.</summary>
    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }
}
