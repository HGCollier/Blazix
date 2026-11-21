using Microsoft.AspNetCore.Components;
using Microsoft.JSInterop;

namespace Blazix.Primitives;

/// <summary>
/// A part that acts as a button: <c>Button</c>, <c>Toggle</c>, every trigger. Renders a native
/// &lt;button&gt;; with <c>Render</c> it detects what you rendered, and a &lt;div&gt;, &lt;span&gt; or
/// &lt;a&gt; gets role="button", a tab stop and Enter/Space activation, while a real &lt;button&gt; must not.
/// </summary>
public abstract class ButtonPart<TRenderProps> : PrimitivePart<TRenderProps> where TRenderProps : RenderProps
{
    private bool? _detected;
    private string? _id;

    [Inject] private IJSRuntime JsRuntime { get; set; } = null!;

    /// <summary>
    /// Whether the element <c>Render</c> produces is a real &lt;button&gt;. Leave it unset: after the
    /// first interactive render the browser reports what you rendered and the part re-renders to
    /// match. Set it to skip that check (e.g. so prerendered HTML is right from the first byte); a
    /// value that doesn't match the element logs a console warning.
    /// </summary>
    [Parameter] public bool? NativeButton { get; set; }

    protected override string Tag => "button";

    /// <summary>The default &lt;button&gt; is always native. With Render: what you declared, else what the browser saw.</summary>
    private bool IsNativeButton => Render is null || (NativeButton ?? _detected ?? true);

    /// <summary>The element's id: the author's, or a generated one.</summary>
    protected string ButtonId => _id ??= ElementAttributes.IdOr(AdditionalAttributes, () => PreferredId ?? Ids.Next(IdPrefix));

    /// <summary>The id to use when the author gives none, e.g. a field's control id.</summary>
    protected virtual string? PreferredId => null;

    protected virtual string IdPrefix => "button";

    /// <summary>Items of a roving group: the group renders their tabindex.</summary>
    protected virtual bool IsComposite => false;

    protected override void OnParametersSet()
    {
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => PreferredId ?? _id ?? Ids.Next(IdPrefix));
        base.OnParametersSet();
    }

    /// <summary>Role, type, tab stop and disabled state.</summary>
    protected void AddButtonAttributes(AttributeBuilder attributes, bool disabled, bool focusableWhenDisabled = false)
    {
        attributes.Set("id", ButtonId);
        if (IsNativeButton)
        {
            attributes.Default("type", "button");
            if (focusableWhenDisabled)
            {
                attributes.Set("aria-disabled", disabled ? "true" : null);
            }
            else
            {
                attributes.Set("disabled", disabled);
            }
        }
        else
        {
            attributes.Default("role", "button");
            attributes.Set("data-blazix-button", "");
            attributes.Set("aria-disabled", disabled ? "true" : null);
            if (!IsComposite)
            {
                if (disabled && !focusableWhenDisabled)
                {
                    attributes.Set("tabindex", "-1");
                }
                else
                {
                    attributes.Default("tabindex", "0");
                }
            }
        }
        attributes.Flag("data-disabled", disabled);
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (firstRender && Render is not null)
        {
            var native = await JsModules.InvokeAsync<bool?>(JsRuntime, JsModules.Button, "isNativeButton", ButtonId, NativeButton, GetType().Name);
            if (native is { } value && value != (NativeButton ?? _detected ?? true))
            {
                _detected = value;
                StateHasChanged();
            }
            else if (native is { } same)
            {
                _detected = same;
            }
        }
        await base.OnAfterRenderAsync(firstRender);
    }
}
