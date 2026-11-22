using System.Diagnostics.CodeAnalysis;

namespace Blazix.Primitives;

/// <summary>Which way a component lays out its items, and which arrow keys move between them.</summary>
public enum Orientation
{
    Horizontal,
    Vertical,
}

/// <summary>Reading direction, for keyboard navigation and logical placement (<see cref="DirectionProvider"/>).</summary>
public enum TextDirection
{
    Ltr,
    Rtl,
}

/// <summary>Which side of its anchor a popup prefers. It flips if there isn't room.</summary>
public enum Side
{
    Bottom,
    Top,
    Left,
    Right,
    /// <summary>Left in left-to-right layouts, right in right-to-left ones.</summary>
    InlineStart,
    /// <summary>Right in left-to-right layouts, left in right-to-left ones (submenus open here).</summary>
    InlineEnd,
}

/// <summary>How a popup lines up with its anchor along the side it opens on.</summary>
public enum Align
{
    Start,
    Center,
    End,
}

/// <summary>Why a popup opened or closed.</summary>
public enum OpenChangeReason
{
    None,
    TriggerPress,
    TriggerHover,
    TriggerFocus,
    OutsidePress,
    EscapeKey,
    ItemPress,
    ClosePress,
    FocusOut,
    ListNavigation,
    CancelOpen,
    SiblingOpen,
    Disabled,
    ImperativeAction,
    Swipe,
}

/// <summary>
/// Passed to a root's <c>OnOpenChange</c> before the change happens. Call <see cref="Cancel"/> to
/// keep the current state.
/// </summary>
public sealed class OpenChangeEventArgs(bool open, OpenChangeReason reason) : EventArgs
{
    public bool Open { get; } = open;

    public OpenChangeReason Reason { get; } = reason;

    public bool Canceled { get; private set; }

    public void Cancel() => Canceled = true;
}

/// <summary>Passed to <c>OnValueChange</c> callbacks before a value changes. <see cref="Cancel"/> keeps the old value.</summary>
public sealed class ValueChangeEventArgs<T>(T value, string reason) : EventArgs
{
    public T Value { get; } = value;

    /// <summary>What caused it, e.g. "trigger-press", "item-press", "keyboard", "drag", "input-change".</summary>
    public string Reason { get; } = reason;

    public bool Canceled { get; private set; }

    public void Cancel() => Canceled = true;
}

internal static class Ids
{
    private static long _next;

    // Server and WebAssembly components can share a page (Interactive Auto, or islands with their own render
    // modes), and each runtime counts on its own. The browser side finds triggers and popups by id, so the same
    // id from both would anchor one runtime's popup to the other's trigger. WebAssembly ids get their own segment.
    private static readonly string Runtime = OperatingSystem.IsBrowser() ? "w" : "";

    public static string Next(string prefix) => $"blazix-{prefix}-{Runtime}{Interlocked.Increment(ref _next)}";
}

internal static class Js
{
    public const string Root = "./_content/Blazix.Primitives/";

    /// <summary>What JSInterop needs kept on a type it deserializes a result into (its own annotation).</summary>
    public const DynamicallyAccessedMemberTypes Deserialized =
        DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicProperties;

    public static string Side(Side side) => side switch
    {
        Primitives.Side.Top => "top",
        Primitives.Side.Left => "left",
        Primitives.Side.Right => "right",
        Primitives.Side.InlineStart => "inline-start",
        Primitives.Side.InlineEnd => "inline-end",
        _ => "bottom",
    };

    public static string Align(Align align) => align switch
    {
        Primitives.Align.Center => "center",
        Primitives.Align.End => "end",
        _ => "start",
    };

    public static string Orientation(Orientation orientation) =>
        orientation == Primitives.Orientation.Vertical ? "vertical" : "horizontal";

    public static string Data(Orientation orientation) => Orientation(orientation);
}

/// <summary>
/// A value a parent may control. Adopts the parent's value only when the parameter changes, so both
/// <c>@bind-Value</c> and "uncontrolled with an initial value" work without extra flags.
/// </summary>
internal sealed class Controlled<T>(T initial, IEqualityComparer<T>? comparer = null)
{
    private readonly IEqualityComparer<T> _comparer = comparer ?? EqualityComparer<T>.Default;
    private bool _seeded;
    private T _lastParameter = initial;

    public T Value { get; set; } = initial;

    /// <summary>Returns true when the parent's new value replaced the current one.</summary>
    public bool Sync(T parameter)
    {
        if (_seeded && _comparer.Equals(_lastParameter, parameter))
        {
            return false;
        }

        _seeded = true;
        _lastParameter = parameter;
        if (_comparer.Equals(Value, parameter))
        {
            return false;
        }

        Value = parameter;
        return true;
    }
}

internal sealed class SequenceComparer<T> : IEqualityComparer<IReadOnlyList<T>>
{
    public static readonly SequenceComparer<T> Instance = new();

    public bool Equals(IReadOnlyList<T>? x, IReadOnlyList<T>? y) =>
        ReferenceEquals(x, y) || (x is not null && y is not null && x.SequenceEqual(y));

    public int GetHashCode(IReadOnlyList<T> obj) => obj.Count;
}
