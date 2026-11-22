namespace Blazix.Registry;

/// <summary>How a <see cref="Calendar"/> selects days (react-day-picker's <c>mode</c>).</summary>
public enum CalendarMode
{
    Single,
    Multiple,
    Range,
}

/// <summary>What the month caption shows (react-day-picker's <c>captionLayout</c>).</summary>
public enum CalendarCaptionLayout
{
    /// <summary>The month and year as text.</summary>
    Label,

    /// <summary>Month and year selects.</summary>
    Dropdown,

    /// <summary>A month select and the year as text.</summary>
    DropdownMonths,

    /// <summary>The month as text and a year select.</summary>
    DropdownYears,
}

/// <summary>A range of days; either end may be missing while the user picks it.</summary>
public sealed record DateRange(DateOnly? From, DateOnly? To);

/// <summary>A day in a month grid. Outside days belong to another month than the grid shows.</summary>
public readonly record struct CalendarDay(DateOnly Date, DateOnly DisplayMonth)
{
    public bool Outside => Date.Year != DisplayMonth.Year || Date.Month != DisplayMonth.Month;
}

/// <summary>The state of a day. The range flags are null unless a range is selected.</summary>
public sealed record CalendarModifiers
{
    public bool Focused { get; init; }
    public bool Disabled { get; init; }
    public bool Hidden { get; init; }
    public bool Outside { get; init; }
    public bool Today { get; init; }
    public bool Selected { get; init; }
    public bool? RangeStart { get; init; }
    public bool? RangeEnd { get; init; }
    public bool? RangeMiddle { get; init; }

    /// <summary>The names of the custom modifiers that match the day.</summary>
    public IReadOnlyList<string> Custom { get; init; } = [];
}

/// <summary>
/// What a <see cref="Calendar"/> passes to its <c>DayButton</c> fragment. Spread <c>Attributes</c> onto a
/// <see cref="CalendarDayButton"/>: they carry the day, its modifiers, the handlers and the aria attributes.
/// </summary>
public sealed record CalendarDayButtonProps(CalendarDay Day, CalendarModifiers Modifiers, IReadOnlyDictionary<string, object> Attributes);
