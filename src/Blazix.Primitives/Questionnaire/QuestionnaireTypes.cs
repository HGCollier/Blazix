namespace Blazix.Primitives;

/// <summary>Whether an item has an answer, was intentionally skipped, or neither.</summary>
public enum QuestionnaireItemStatus
{
    Unanswered,
    Answered,
    Skipped,
}

/// <summary>Which keys select a questionnaire's answers: A–Z, or 1–9.</summary>
public enum QuestionnaireShortcuts
{
    Letters,
    Numbers,
}

/// <summary>A fixed answer in a <see cref="QuestionnaireItemDefinition"/>.</summary>
public sealed record QuestionnaireChoiceDefinition(string Value, bool Disabled = false);

/// <summary>
/// An item of the questionnaire's collection (<c>Root.items</c>). Pass the collection to
/// <see cref="QuestionnaireRoot.Items"/> so progress, actions and shortcuts are right on the first render,
/// before the items register.
/// </summary>
public sealed record QuestionnaireItemDefinition(
    string Name,
    bool Required = false,
    bool Disabled = false,
    IReadOnlyList<QuestionnaireChoiceDefinition>? Choices = null);

/// <summary>
/// The answers a questionnaire submits, as the browser's <c>FormData</c> would read them from the form:
/// the checked choices and the selected freeform inputs of enabled, unskipped items.
/// </summary>
public sealed class QuestionnaireAnswers
{
    private readonly Dictionary<string, List<string>> _values = new(StringComparer.Ordinal);

    internal void Add(string name, string value)
    {
        if (!_values.TryGetValue(name, out var values))
        {
            values = [];
            _values[name] = values;
        }
        values.Add(value);
    }

    /// <summary>The names that have at least one answer.</summary>
    public IEnumerable<string> Names => _values.Keys;

    /// <summary>The first answer for <paramref name="name"/>, or null (<c>FormData.get</c>).</summary>
    public string? Get(string name) => _values.TryGetValue(name, out var values) ? values[0] : null;

    /// <summary>Every answer for <paramref name="name"/> (<c>FormData.getAll</c>), for multiple items.</summary>
    public IReadOnlyList<string> GetAll(string name) => _values.TryGetValue(name, out var values) ? values : [];
}

/// <summary>What <see cref="QuestionnaireRoot"/> and <see cref="QuestionnaireProgress"/> hand to <c>Render</c>.</summary>
public sealed record QuestionnaireRenderProps(IReadOnlyDictionary<string, object> Attributes, int Current, int Total, bool First, bool Last)
    : RenderProps(Attributes);

public sealed record QuestionnaireItemRenderProps(
    IReadOnlyDictionary<string, object> Attributes,
    bool Active,
    bool Disabled,
    bool Invalid,
    bool Multiple,
    bool Required,
    QuestionnaireItemStatus Status) : RenderProps(Attributes);

public sealed record QuestionnaireChoicesRenderProps(IReadOnlyDictionary<string, object> Attributes, QuestionnaireShortcuts? Shortcuts)
    : RenderProps(Attributes);

/// <summary>What <see cref="QuestionnaireChoice"/> and <see cref="QuestionnaireChoiceInput"/> hand to <c>Render</c>.</summary>
public sealed record QuestionnaireChoiceRenderProps(
    IReadOnlyDictionary<string, object> Attributes,
    bool Checked,
    bool Disabled,
    bool Invalid,
    string? Shortcut,
    string Type) : RenderProps(Attributes);

public sealed record QuestionnaireChoiceShortcutRenderProps(IReadOnlyDictionary<string, object> Attributes, string? Shortcut)
    : RenderProps(Attributes);

public sealed record QuestionnaireInputRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Disabled, bool Filled, bool Invalid)
    : RenderProps(Attributes);

public sealed record QuestionnaireErrorRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Invalid)
    : RenderProps(Attributes);

/// <summary>What the navigation buttons (Previous, Skip, Next, Submit) hand to <c>Render</c>.</summary>
public sealed record QuestionnaireNavigationRenderProps(
    IReadOnlyDictionary<string, object> Attributes,
    bool Disabled,
    string? Shortcut,
    QuestionnaireItemStatus? Status,
    bool Visible) : RenderProps(Attributes);

internal static class QuestionnaireKeys
{
    public static IReadOnlyList<string> For(QuestionnaireShortcuts? shortcuts) => shortcuts switch
    {
        QuestionnaireShortcuts.Letters => Letters,
        QuestionnaireShortcuts.Numbers => Numbers,
        _ => [],
    };

    private static readonly string[] Letters = Enumerable.Range(0, 26).Select(i => ((char)('A' + i)).ToString()).ToArray();

    private static readonly string[] Numbers = Enumerable.Range(1, 9).Select(i => i.ToString(System.Globalization.CultureInfo.InvariantCulture)).ToArray();

    public static string? Name(QuestionnaireShortcuts? shortcuts) => shortcuts switch
    {
        QuestionnaireShortcuts.Letters => "letters",
        QuestionnaireShortcuts.Numbers => "numbers",
        _ => null,
    };

    public static string Name(QuestionnaireItemStatus status) => status switch
    {
        QuestionnaireItemStatus.Answered => "answered",
        QuestionnaireItemStatus.Skipped => "skipped",
        _ => "unanswered",
    };

    public static bool HasValue(string? value) => !string.IsNullOrWhiteSpace(value);
}
