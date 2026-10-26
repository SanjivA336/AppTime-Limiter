namespace HardCap.Core.Models;

/// <summary>Which of the two recorded amounts of time a group's budget (and its warnings) is measured against.</summary>
public enum CountMode
{
    /// <summary>Every moment one of the group's apps is running, in front or not. This is how HardCap always counted.</summary>
    WhileRunning = 0,

    /// <summary>Only the moments one of the group's apps owns the window you are using.</summary>
    WhileInFront = 1,
}
