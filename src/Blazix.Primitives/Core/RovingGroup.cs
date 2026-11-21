using Microsoft.JSInterop;

namespace Blazix.Primitives;

/// <summary>
/// One tab stop for a group of items (WAI-ARIA roving tabindex), kept in C# so it renders on the
/// server and hydrates. The stop goes to the item focused last, else the first preferred enabled
/// item (the selected tab, the checked radio), else the first enabled one. Arrow keys are handled
/// in the browser by <c>core/roving.js</c>, which reads the markers this renders.
/// </summary>
internal sealed class RovingGroup
{
    private readonly List<RovingItem> _items;
    private readonly Func<bool> _disabled;
    private readonly Action _requestRender;
    private string? _remembered;

    public RovingGroup(Action requestRender, Func<bool>? disabled = null, bool remember = true)
    {
        _items = [];
        Root = this;
        _requestRender = requestRender;
        _disabled = disabled ?? (() => false);
        Remember = remember;
    }

    // A scope inside a group (a toolbar group): shares the group's items and tab stop, adds its own disabled state.
    private RovingGroup(RovingGroup parent, Func<bool> disabled)
    {
        Root = parent.Root;
        _items = parent._items;
        _requestRender = parent._requestRender;
        _disabled = () => parent.Disabled || disabled();
        Remember = parent.Remember;
    }

    /// <summary>Whether the item focused last keeps the stop (toolbars), rather than the preferred one (tabs).</summary>
    private bool Remember { get; }

    /// <summary>Whether the whole group (or scope) is disabled; its items read it.</summary>
    public bool Disabled => _disabled();

    /// <summary>A nested scope (e.g. a toolbar group) that shares this group's tab stop.</summary>
    public RovingGroup Scope(Func<bool> disabled) => new(this, disabled);

    private RovingGroup Root { get; }

    public IReadOnlyList<RovingItem> Items => _items;

    public void Register(RovingItem item)
    {
        if (!_items.Contains(item))
        {
            _items.Add(item);
        }
    }

    public void Unregister(RovingItem item)
    {
        _items.Remove(item);
        if (Root._remembered == item.Id)
        {
            Root._remembered = null;
        }
    }

    private string? Stop
    {
        get
        {
            var remembered = Root._remembered;
            if (remembered is not null && _items.Exists(item => item.Id == remembered && item.Focusable))
            {
                return remembered;
            }

            var enabled = _items.FindAll(item => !item.Disabled);
            return (enabled.Find(item => item.Preferred) ?? enabled.FirstOrDefault() ?? _items.Find(item => item.Focusable))?.Id;
        }
    }

    private string TabIndexFor(RovingItem item) => item.Id == Stop ? "0" : "-1";

    /// <summary>An item took focus: it keeps the stop. Re-renders only when the stop moved.</summary>
    public void Focused(RovingItem item)
    {
        var root = Root;
        if (!Remember || root._remembered == item.Id)
        {
            return;
        }
        var before = Stop;
        root._remembered = item.Id;
        if (Stop != before)
        {
            _requestRender();
        }
    }

    /// <summary>Markers core/roving.js reads on the group element.</summary>
    public static void AddGroupAttributes(AttributeBuilder attributes, string orientation, bool loop, bool activateOnFocus = false)
    {
        attributes.Set("data-blazix-roving", orientation);
        attributes.Flag("data-blazix-loop", loop);
        attributes.Flag("data-blazix-activate", activateOnFocus);
    }

    /// <summary>Attributes every item renders: its tab stop, the marker, and the focus report.</summary>
    public void AddItemAttributes(AttributeBuilder attributes, RovingItem item, object onFocus, bool focusableWhenDisabled = false)
    {
        attributes
            .Set("tabindex", TabIndexFor(item))
            .Set("data-blazix-roving-item", "")
            .Flag("data-focusable", focusableWhenDisabled)
            .Set("onfocus", onFocus);
    }

    public static Task EnsureScriptAsync(IJSRuntime js) => JsModules.EnsureAsync(js, JsModules.Roving);
}

/// <summary>An item of a <see cref="RovingGroup"/>, read live so disabled and preferred state stay current.</summary>
internal sealed class RovingItem(string id, Func<bool> disabled, Func<bool> preferred, Func<bool>? focusableWhenDisabled = null)
{
    public string Id { get; set; } = id;

    public bool Disabled => disabled();

    public bool Preferred => preferred();

    public bool Focusable => !Disabled || (focusableWhenDisabled?.Invoke() ?? false);
}
