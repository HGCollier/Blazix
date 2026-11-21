namespace Blazix.Primitives;

/// <summary>
/// What a part's <c>Render</c> fragment receives.
/// Spread <see cref="Attributes"/> onto the element you render (<c>@attributes="props.Attributes"</c>):
/// it carries the id, role, ARIA and data-* state, and the click handler the part needs.
/// If you render a component, it must pass them on to its root element.
/// </summary>
public abstract record RenderProps(IReadOnlyDictionary<string, object> Attributes);
