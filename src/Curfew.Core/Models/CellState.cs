namespace Curfew.Core.Models;

/// <summary>What a group's apps are allowed to do during one 30-minute schedule cell.</summary>
public enum CellState
{
    /// <summary>Usage is recorded but does not count toward the daily budget.</summary>
    Free,

    /// <summary>Usage is recorded and counts toward the shared daily budget.</summary>
    Limited,

    /// <summary>The apps are closed as soon as they are seen running.</summary>
    Blocked,
}
