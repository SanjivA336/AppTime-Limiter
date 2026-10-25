using AppTimeLimiter.Core.Models;

namespace AppTimeLimiter.Core.Settings;

/// <summary>
/// A "draft" of one group that can be edited freely and then either kept or thrown away. The group's own daily
/// budgets, schedule, lock and everything else are part of it.
///
/// Nothing the user does to the draft touches the real settings. The enforcement loop keeps running on the real
/// ones, so a half-finished edit can never close an app. Only <see cref="Commit"/> copies the draft across.
/// </summary>
public sealed class GroupEditSession
{
    private readonly AppSettings _real;
    private readonly AppGroup _original;

    /// <param name="real">The app's real settings.</param>
    /// <param name="original">
    /// The group to edit. If it isn't in the real settings yet, it is a brand new group, and committing adds it.
    /// </param>
    public GroupEditSession(AppSettings real, AppGroup original)
    {
        _real = real;
        _original = original;
        Draft = original.Clone();
        DraftSettings = BuildDraftSettings();
    }

    /// <summary>The copy of the group that is being edited.</summary>
    public AppGroup Draft { get; }

    /// <summary>
    /// A stand-in for the settings in which the draft takes the original's place. Validation (is this name taken, is
    /// this app already in another group) is run against this, so it sees the draft as it currently is. Other
    /// groups are shared with the real settings and must not be changed.
    /// </summary>
    public AppSettings DraftSettings { get; }

    /// <summary>True when this group isn't in the real settings yet, so saving it creates it.</summary>
    public bool IsNew => !_real.Groups.Contains(_original);

    /// <summary>True if the draft differs from the real group (or is a new group, which saving would create).</summary>
    public bool HasChanges => IsNew || !Draft.IsSameAs(_original);

    /// <summary>Puts the draft back exactly as it was when the session began.</summary>
    public void Revert() => Draft.CopyFrom(_original);

    /// <summary>Makes the draft real: copies it into the group, and adds the group if it is new.</summary>
    public void Commit()
    {
        var isNew = IsNew;
        _original.CopyFrom(Draft);

        if (isNew)
        {
            _real.Groups.Add(_original);
        }
    }

    private AppSettings BuildDraftSettings()
    {
        var groups = _real.Groups.Select(g => g.Id == _original.Id ? Draft : g).ToList();
        if (!groups.Contains(Draft))
        {
            groups.Add(Draft); // a new group, which isn't in the real list yet
        }

        return new AppSettings
        {
            SchemaVersion = _real.SchemaVersion,
            StartWithWindows = _real.StartWithWindows,
            Groups = groups,
        };
    }
}
