using System.Globalization;
using Microsoft.AspNetCore.Components;

namespace Blazix.Primitives;

/// <summary>
/// Builds a component of your own with the same <c>Render</c> support as the primitives:
/// renders <see cref="DefaultTagName"/>, or your element through <c>Render</c>,
/// with your attributes, and <see cref="State"/> turned into data attributes (<c>true</c> renders
/// <c>data-name</c>, other values <c>data-name="value"</c>, <c>false</c> and null nothing).
/// </summary>
public sealed class UseRender : PrimitivePart<UseRenderProps>
{
    /// <summary>The element rendered without <c>Render</c>. Defaults to div.</summary>
    [Parameter] public string DefaultTagName { get; set; } = "div";

    /// <summary>The component's state, rendered as data attributes and handed to <c>Render</c>.</summary>
    [Parameter] public IReadOnlyDictionary<string, object?>? State { get; set; }

    protected override string Tag => DefaultTagName;

    protected override bool IsVoid => DefaultTagName is "input" or "img" or "br" or "hr" or "meta" or "link" or "source" or "area" or "col" or "embed" or "track" or "wbr";

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        foreach (var (key, value) in State ?? new Dictionary<string, object?>())
        {
            var name = "data-" + Kebab(key);
            switch (value)
            {
                case null or false:
                    break;
                case true:
                    attributes.Default(name, "");
                    break;
                default:
                    attributes.Default(name, Convert.ToString(value, CultureInfo.InvariantCulture) ?? "");
                    break;
            }
        }
    }

    private static string Kebab(string name)
    {
        var result = new System.Text.StringBuilder(name.Length + 4);
        foreach (var c in name)
        {
            if (char.IsUpper(c) && result.Length > 0)
            {
                result.Append('-');
            }
            result.Append(char.ToLowerInvariant(c));
        }
        return result.ToString();
    }

    protected override UseRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes, State);
}

/// <summary>What <see cref="UseRender"/>'s <c>Render</c> fragment receives: the attributes to spread, and the state.</summary>
public sealed record UseRenderProps(IReadOnlyDictionary<string, object> Attributes, IReadOnlyDictionary<string, object?>? State) : RenderProps(Attributes);
