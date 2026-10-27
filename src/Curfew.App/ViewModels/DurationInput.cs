using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using Curfew.Core.Models;

namespace Curfew.App.ViewModels;

/// <summary>
/// A length of time (up to 5 minutes) edited in two number boxes, minutes and seconds. It is stored as plain seconds
/// somewhere else (a group's lock, its grace period, the general wait); this class converts both ways and keeps the two
/// boxes in step: stepping the seconds past 59 carries into the minutes, and stepping below 0 borrows a minute.
/// </summary>
public sealed class DurationInput : ObservableObject
{
    private readonly Func<int> _getSeconds;
    private readonly Action<int> _setSeconds;
    private readonly string _zeroText;

    /// <param name="getSeconds">Reads the stored length.</param>
    /// <param name="setSeconds">Stores a new length. Only called when the length really changed.</param>
    /// <param name="zeroText">What the summary says when the length is 0, such as "No wait".</param>
    public DurationInput(Func<int> getSeconds, Action<int> setSeconds, string zeroText)
    {
        _getSeconds = getSeconds;
        _setSeconds = setSeconds;
        _zeroText = zeroText;
    }

    /// <summary>The minutes box. It is a double because that is what a number box uses.</summary>
    public double Minutes
    {
        get => LockTime.ToParts(_getSeconds()).Minutes;
        set => Apply(value, Seconds);
    }

    /// <summary>The seconds box. It steps by 5, and going past 59 or below 0 carries into the minutes.</summary>
    public double Seconds
    {
        get => LockTime.ToParts(_getSeconds()).Seconds;
        set => Apply(Minutes, value);
    }

    /// <summary>The chosen length in words, like "1 minute 15 seconds", or the zero text.</summary>
    public string Summary => LockTime.Describe(_getSeconds(), _zeroText);

    /// <summary>Re-reads the stored length into both boxes (after a revert, say).</summary>
    public void Refresh()
    {
        OnPropertyChanged(nameof(Minutes));
        OnPropertyChanged(nameof(Seconds));
        OnPropertyChanged(nameof(Summary));
    }

    /// <summary>
    /// Turns what is in the two boxes into one total (carrying and borrowing between them), stores it, and shows the
    /// tidied-up result back in both boxes.
    /// </summary>
    private void Apply(double minutes, double seconds)
    {
        // (A box that is empty or holds something that isn't a number counts as 0.)
        static int Whole(double value) => double.IsFinite(value) ? (int)Math.Round(value) : 0;

        var total = LockTime.FromParts(Whole(minutes), Whole(seconds));
        if (total != _getSeconds())
        {
            _setSeconds(total);
        }

        Refresh();

        // A number box that was just typed into keeps what was typed unless it is told again after its own update
        // has finished (for example when "5:05" was tidied to "5:00", or "0:60" to "1:00").
        Application.Current.Dispatcher.BeginInvoke(DispatcherPriority.Background, Refresh);
    }
}
