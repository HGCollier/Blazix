using Microsoft.AspNetCore.Components;

namespace Blazix.Primitives;

/// <summary>State a <see cref="ButtonRoot"/> passes to its Render fragment.</summary>
public sealed record ButtonRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Disabled)
    : RenderProps(Attributes);

/// <summary>
/// A button that can be rendered as another element. Named <c>ButtonRoot</c>
/// so it never collides with a styled <c>Button</c> in your app.
/// </summary>
public class ButtonRoot : ButtonPart<ButtonRenderProps>
{
    [Parameter] public bool Disabled { get; set; }

    /// <summary>
    /// Keep the button focusable while disabled (it renders aria-disabled instead of disabled), e.g.
    /// so a loading button keeps focus. Defaults to false.
    /// </summary>
    [Parameter] public bool FocusableWhenDisabled { get; set; }

    protected override void BuildAttributes(AttributeBuilder attributes) =>
        AddButtonAttributes(attributes, Disabled, FocusableWhenDisabled);

    protected override ButtonRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Disabled);
}
