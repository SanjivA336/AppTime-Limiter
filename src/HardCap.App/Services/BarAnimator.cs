using System.Windows.Media;
using HardCap.App.ViewModels;

namespace HardCap.App.Services;

/// <summary>
/// Makes the cards' bars glide to their new length instead of jumping. While any bar is still on its way, this
/// is called once per screen frame and moves each one a little closer. When every bar has arrived it stops
/// listening, so a quiet app does no work.
/// </summary>
internal static class BarAnimator
{
    /// <summary>A frame that took longer than this (a hiccup, a pause) is treated as this long, so bars don't lurch.</summary>
    private const double LongestStepSeconds = 0.1;

    private static readonly HashSet<GroupViewModel> Moving = new();
    private static TimeSpan _lastFrameTime;
    private static bool _listening;

    /// <summary>Starts (or continues) moving this group's bar toward its goal.</summary>
    public static void Start(GroupViewModel group)
    {
        Moving.Add(group);
        if (_listening)
        {
            return;
        }

        _listening = true;
        _lastFrameTime = TimeSpan.Zero;
        CompositionTarget.Rendering += OnFrame;
    }

    private static void OnFrame(object? sender, EventArgs e)
    {
        var frameTime = ((RenderingEventArgs)e).RenderingTime;
        var seconds = _lastFrameTime == TimeSpan.Zero ? 0 : Math.Min((frameTime - _lastFrameTime).TotalSeconds, LongestStepSeconds);
        _lastFrameTime = frameTime;

        foreach (var group in Moving.ToList())
        {
            var stillMoving = group.StepDisplayedFraction(seconds);
            if (!stillMoving)
            {
                Moving.Remove(group);
            }
        }

        if (Moving.Count == 0)
        {
            CompositionTarget.Rendering -= OnFrame;
            _listening = false;
        }
    }
}
