namespace Blazix.Registry;

/// <summary>
/// What a Field cascades so its control is described by its text: each FieldDescription and shown FieldError adds its
/// id, and the control (Input, Select, Checkbox...) puts them in its <c>aria-describedby</c>, after any you give it.
/// </summary>
public sealed class FieldDescriptions
{
    private readonly List<string> _ids = [];
    private readonly Action _changed;

    internal FieldDescriptions(Action changed) => _changed = changed;

    /// <summary>The ids of the field's description and error, in the order they rendered, or null when it has none.</summary>
    public string? Ids => _ids.Count == 0 ? null : string.Join(' ', _ids);

    internal void Add(string id)
    {
        if (!_ids.Contains(id))
        {
            _ids.Add(id);
            _changed();
        }
    }

    internal void Remove(string id)
    {
        if (_ids.Remove(id))
        {
            _changed();
        }
    }

    /// <summary>The attributes with the field's descriptions added to their <c>aria-describedby</c>.</summary>
    public static IReadOnlyDictionary<string, object>? Merge(FieldDescriptions? descriptions, IReadOnlyDictionary<string, object>? attributes)
    {
        if (descriptions?.Ids is not { } ids)
        {
            return attributes;
        }
        var merged = attributes is null
            ? new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase)
            : new Dictionary<string, object>(attributes, StringComparer.OrdinalIgnoreCase);
        merged["aria-describedby"] = merged.TryGetValue("aria-describedby", out var own) && own is string { Length: > 0 } authored
            ? $"{authored} {ids}"
            : ids;
        return merged;
    }
}
