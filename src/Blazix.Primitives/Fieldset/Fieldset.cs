using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Blazix.Primitives;

public sealed record FieldsetRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Disabled)
    : RenderProps(Attributes);

/// <summary>Cascaded from <see cref="FieldsetRoot"/>: disables every field inside it.</summary>
public sealed class FieldsetContext
{
    private readonly FieldsetRoot _root;

    internal FieldsetContext(FieldsetRoot root) => _root = root;

    public bool Disabled => _root.IsDisabled;

    internal string? LegendId { get; set; }

    internal void RegisterLegend(string id) => _root.RegisterLegend(id);
}

/// <summary>Groups a shared legend with related controls. Renders a &lt;fieldset&gt;.</summary>
public class FieldsetRoot : PrimitivePart<FieldsetRenderProps>
{
    private readonly FieldsetContext _context;

    public FieldsetRoot() => _context = new FieldsetContext(this);

    [CascadingParameter] private FieldsetContext? Parent { get; set; }

    [Parameter] public bool Disabled { get; set; }

    protected override string Tag => "fieldset";

    internal bool IsDisabled => Disabled || (Parent?.Disabled ?? false);

    internal void RegisterLegend(string id)
    {
        if (_context.LegendId == id)
        {
            return;
        }
        _context.LegendId = id;
        StateHasChanged();
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Cascade(builder, _context, BuildPart);

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .LabelledBy(_context.LegendId)
        .Set("disabled", IsDisabled)
        .Flag("data-disabled", IsDisabled);

    protected override FieldsetRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes, IsDisabled);
}

/// <summary>An accessible label for the fieldset. Renders a &lt;div&gt; that labels it.</summary>
public class FieldsetLegend : PrimitivePart<FieldsetRenderProps>
{
    private string _id = "";

    [CascadingParameter] private FieldsetContext Fieldset { get; set; } = null!;

    protected override void OnParametersSet()
    {
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("fieldset-legend"));
        Fieldset.RegisterLegend(_id);
    }

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("id", _id)
        .Flag("data-disabled", Fieldset.Disabled);

    protected override FieldsetRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes, Fieldset.Disabled);
}
