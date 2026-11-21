using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace Blazix.Primitives;

public sealed record RadioRenderProps(IReadOnlyDictionary<string, object> Attributes, bool Checked, bool Disabled, bool ReadOnly, bool Required)
    : RenderProps(Attributes);

/// <summary>State a <see cref="RadioRoot"/> shares with its indicator.</summary>
public sealed class RadioContext
{
    internal RadioContext(RadioRoot root) => Root = root;

    internal RadioRoot Root { get; }

    public bool Checked => Root.CurrentChecked;
}

/// <summary>
/// An easily stylable radio button: a &lt;span role="radio"&gt; with a
/// hidden native radio beside it. Goes inside a <see cref="RadioGroupRoot"/>.
/// </summary>
public class RadioRoot : CheckableRoot<RadioRenderProps>
{
    private readonly RadioContext _context;
    private RovingItem? _item;
    private EventCallback<FocusEventArgs> _onRovingFocus;

    public RadioRoot() => _context = new RadioContext(this);

    [CascadingParameter] private RadioGroupContext Group { get; set; } = null!;

    /// <summary>Identifies the radio in its group's value.</summary>
    [Parameter, EditorRequired] public string Value { get; set; } = "";

    internal string RadioValue => Value;

    internal bool CurrentChecked => IsChecked;

    internal string InputElementId => HiddenInputId;

    internal string RootElementId => RootId;

    protected override string Role => "radio";

    protected override string InputType => "radio";

    protected override bool RegistersWithField => false;

    protected override bool IsChecked => Group.Value == Value;

    protected override bool IsDisabled => base.IsDisabled || Group.Disabled;

    protected override string InputValue => Value;

    protected override string EffectiveName => Name ?? Group.Name;

    protected override bool IsReadOnly => ReadOnly || Group.ReadOnly;

    protected override bool IsRequired => Required || Group.Required;

    internal bool CurrentReadOnly => IsReadOnly;

    internal bool CurrentRequired => IsRequired;

    internal bool CurrentDisabled => IsDisabled;

    protected override void OnInitialized()
    {
        base.OnInitialized();
        _onRovingFocus = EventCallback.Factory.Create<FocusEventArgs>(this, async () =>
        {
            Group.Roving.Focused(_item!);
            await Group.FocusAsync();
        });
    }

    protected override void OnParametersSet()
    {
        base.OnParametersSet();
        _item ??= new RovingItem(RootId, () => IsDisabled, () => IsChecked);
        _item.Id = RootId;
        Group.Roving.Register(_item);
        Group.Register(this);
    }

    protected override void AddTabStop(AttributeBuilder attributes) =>
        Group.Roving.AddItemAttributes(attributes, _item!, _onRovingFocus);

    protected override Task ToggleAsync(bool? checkedByInput) => Group.SelectAsync(Value);

    protected override void BuildRenderTree(RenderTreeBuilder builder)
    {
        Cascade(builder, _context, BuildPart);
        BuildHiddenInput(builder);
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        AddCheckableAttributes(attributes);
        attributes
            .Flag("data-checked", IsChecked)
            .Flag("data-unchecked", !IsChecked);
    }

    protected override RadioRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, IsChecked, IsDisabled, ReadOnly, Required);

    protected override void Dispose(bool disposing)
    {
        if (_item is not null)
        {
            Group.Roving.Unregister(_item);
        }
        Group.Unregister(this);
        base.Dispose(disposing);
    }
}

/// <summary>Indicates whether the radio is selected. Renders a &lt;span&gt; while checked.</summary>
public class RadioIndicator : PrimitivePart<RadioRenderProps>
{
    [CascadingParameter] private RadioContext Radio { get; set; } = null!;

    [CascadingParameter] private FieldContext? Field { get; set; }

    /// <summary>Render while unchecked too (e.g. to animate it out). Defaults to false.</summary>
    [Parameter] public bool KeepMounted { get; set; }

    protected override string Tag => "span";

    protected override bool IsPresent => KeepMounted || Radio.Checked;

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .FieldState(Field, Radio.Root.CurrentDisabled)
        .Flag("data-checked", Radio.Checked)
        .Flag("data-unchecked", !Radio.Checked);

    protected override RadioRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Radio.Checked, Radio.Root.CurrentDisabled, Radio.Root.CurrentReadOnly, Radio.Root.CurrentRequired);
}
