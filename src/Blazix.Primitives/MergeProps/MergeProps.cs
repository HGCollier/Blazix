using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;

namespace Blazix.Primitives;

/// <summary>
/// Builds the attributes a part renders: the author's (class, style, data-testid, their own
/// handlers...) overlaid with the part's. The part's values win, except that:
/// <list type="bullet">
/// <item>an author's event handler and the part's both run (author first);</item>
/// <item>an author's <c>style</c> is appended after the part's, so the author's declarations win;</item>
/// <item><see cref="Default"/> values apply only when the author didn't set one.</item>
/// </list>
/// A null value removes the attribute. Booleans follow Blazor's rules: false is omitted on an element.
/// Use it to build your own parts on <see cref="PrimitivePart{TRenderProps}"/>.
/// </summary>
public sealed class AttributeBuilder(IReadOnlyDictionary<string, object>? authored)
{
    private readonly Dictionary<string, object> _values = authored is null
        ? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
        : new Dictionary<string, object>(authored, StringComparer.OrdinalIgnoreCase);

    public AttributeBuilder Set(string name, object? value)
    {
        if (value is null)
        {
            _values.Remove(name);
            return this;
        }

        if (authored is not null && authored.TryGetValue(name, out var mine))
        {
            if (string.Equals(name, "style", StringComparison.OrdinalIgnoreCase) && mine is string authoredStyle && value is string ownStyle)
            {
                _values[name] = $"{ownStyle.TrimEnd(';')};{authoredStyle}";
                return this;
            }

            if (EventHandlers.TryChain(mine, value, out var chained))
            {
                _values[name] = chained;
                return this;
            }
        }

        _values[name] = value;
        return this;
    }

    /// <summary>A presence attribute such as <c>data-open</c>: rendered as <c>""</c> when true, omitted when false.</summary>
    public AttributeBuilder Flag(string name, bool present) => Set(name, present ? "" : null);

    /// <summary>Sets the value unless the author passed one (e.g. <c>type="button"</c>).</summary>
    public AttributeBuilder Default(string name, object? value)
    {
        if (value is not null)
        {
            _values.TryAdd(name, value);
        }
        return this;
    }

    /// <summary>
    /// Names the element by <c>aria-labelledby</c>, e.g. with a title or label part's id, unless the author named it:
    /// their own <c>aria-labelledby</c> or <c>aria-label</c> wins. Null leaves the author's attributes as they are.
    /// </summary>
    public AttributeBuilder LabelledBy(string? ids)
    {
        if (!string.IsNullOrWhiteSpace(ids) && !IsAuthored("aria-labelledby") && !IsAuthored("aria-label"))
        {
            _values["aria-labelledby"] = ids;
        }
        return this;
    }

    /// <summary>Adds ids to <c>aria-describedby</c>, after the author's own. Null adds nothing.</summary>
    public AttributeBuilder DescribedBy(string? ids)
    {
        if (string.IsNullOrWhiteSpace(ids))
        {
            return this;
        }
        var current = _values.TryGetValue("aria-describedby", out var value) ? value as string : null;
        var all = (current ?? "").Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Concat(ids.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            .Distinct(StringComparer.Ordinal);
        _values["aria-describedby"] = string.Join(' ', all);
        return this;
    }

    private bool IsAuthored(string name) =>
        authored?.Any(pair => string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase) && pair.Value is string { Length: > 0 }) == true;

    /// <summary>Only when the author didn't pass one: e.g. a part's own id.</summary>
    public bool Has(string name) => _values.ContainsKey(name);

    public IReadOnlyDictionary<string, object> Build() => _values;
}

internal static class ElementAttributes
{
    /// <summary>The caller's own id if they passed one, otherwise a generated one.</summary>
    public static string IdOr(IReadOnlyDictionary<string, object>? additional, Func<string> generate) =>
        additional is not null && additional.TryGetValue("id", out var id) && id is string { Length: > 0 } s ? s : generate();

    /// <summary>
    /// The caller's attributes overlaid with the part's own. The part's values win; a null value removes the attribute.
    /// The author's event handlers still run, before the part's.
    /// </summary>
    public static IReadOnlyDictionary<string, object> Merge(
        IReadOnlyDictionary<string, object>? additional,
        params (string Name, object? Value)[] own)
    {
        var builder = new AttributeBuilder(additional);
        foreach (var (name, value) in own)
        {
            builder.Set(name, value);
        }
        return builder.Build();
    }
}

/// <summary>Runs an author's handler and a part's handler for the same event, author first.</summary>
internal static class EventHandlers
{
    public static bool TryChain(object authored, object own, out object chained)
    {
        chained = own;
        if (!IsHandler(authored))
        {
            return false;
        }

        chained = own switch
        {
            EventCallback<MouseEventArgs> typed => Chain(authored, typed),
            EventCallback<KeyboardEventArgs> typed => Chain(authored, typed),
            EventCallback<FocusEventArgs> typed => Chain(authored, typed),
            EventCallback<PointerEventArgs> typed => Chain(authored, typed),
            EventCallback<ChangeEventArgs> typed => Chain(authored, typed),
            EventCallback<EventArgs> typed => Chain(authored, typed),
            EventCallback untyped => Chain(authored, untyped),
            _ => own,
        };
        return !ReferenceEquals(chained, own);
    }

    private static bool IsHandler(object value) =>
        value is Delegate || (value.GetType() is { IsGenericType: true } type && type.GetGenericTypeDefinition() == typeof(EventCallback<>)) || value is EventCallback;

    private static EventCallback<T> Chain<T>(object authored, EventCallback<T> own) =>
        EventCallback.Factory.Create<T>(new object(), async args =>
        {
            await InvokeAuthoredAsync(authored, args);
            await own.InvokeAsync(args);
        });

    private static EventCallback Chain(object authored, EventCallback own) =>
        EventCallback.Factory.Create(new object(), async args =>
        {
            await InvokeAuthoredAsync(authored, args);
            await own.InvokeAsync(args);
        });

    private static Task InvokeAuthoredAsync<T>(object authored, T args) => authored switch
    {
        EventCallback<T> callback => callback.InvokeAsync(args),
        EventCallback callback => callback.InvokeAsync(args),
        Func<T, Task> func => func(args),
        Action<T> action => Invoke(() => action(args)),
        Func<Task> func => func(),
        Action action => Invoke(action),
        Delegate other => other.DynamicInvoke(args) as Task ?? Task.CompletedTask,
        _ => InvokeUntypedCallbackAsync(authored, args),
    };

    // An EventCallback of one of the event's base types (e.g. the author typed MouseEventArgs where the part uses
    // PointerEventArgs). Matched by type rather than by reflection, so it's safe to trim and AOT compile; every
    // part's event args derive from MouseEventArgs, EventArgs or object.
    private static Task InvokeUntypedCallbackAsync<T>(object authored, T args) => (authored, args) switch
    {
        (EventCallback<MouseEventArgs> callback, MouseEventArgs e) => callback.InvokeAsync(e),
        (EventCallback<EventArgs> callback, EventArgs e) => callback.InvokeAsync(e),
        (EventCallback<object> callback, _) => callback.InvokeAsync(args),
        _ => Task.CompletedTask,
    };

    private static Task Invoke(Action action)
    {
        action();
        return Task.CompletedTask;
    }
}

/// <summary>
/// Merges sets of attributes: later sets win, except that event
/// handlers all run (the later set's first), <c>class</c> values are joined, and <c>style</c>
/// declarations are appended (so the later set's win). Use it when a Render fragment needs the
/// part's attributes and your own on one element:
/// <c>@attributes="MergeProps.Merge(props.Attributes, mine)"</c>.
/// </summary>
public static class MergeProps
{
    public static IReadOnlyDictionary<string, object> Merge(params IReadOnlyDictionary<string, object>?[] sets)
    {
        var result = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        foreach (var set in sets)
        {
            if (set is null)
            {
                continue;
            }
            foreach (var (name, value) in set)
            {
                result[name] = result.TryGetValue(name, out var earlier) ? Combine(name, earlier, value) : value;
            }
        }
        return result;
    }

    private static object Combine(string name, object earlier, object later)
    {
        if (string.Equals(name, "class", StringComparison.OrdinalIgnoreCase) && earlier is string a && later is string b)
        {
            return $"{a} {b}".Trim();
        }
        if (string.Equals(name, "style", StringComparison.OrdinalIgnoreCase) && earlier is string first && later is string second)
        {
            return $"{first.TrimEnd(';')};{second}";
        }
        // The later handler runs first.
        return EventHandlers.TryChain(later, earlier, out var chained) ? chained : later;
    }
}
