using System.Collections.Concurrent;
using System.Globalization;
using System.Net;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.AspNetCore.Components;

namespace Blazix.Docs.Components.Docs;

/// <summary>A component's parameters, read from its assembly and XML documentation.</summary>
internal sealed record ApiParameter(string Name, string Type, string? Default, string? Summary, bool Required);

/// <summary>A registry component and its parameters.</summary>
internal sealed record ApiComponent(string Name, IReadOnlyList<ApiParameter> Parameters, bool PassesAttributes);

/// <summary>What a primitive's API Reference entry is: a part with an element, a part without one, or a type such as a handle.</summary>
internal enum ApiPartKind
{
    Element,
    NoElement,
    NotComponent,
}

/// <summary>
/// A part of a primitive: its summary, what it is, the element it renders (null when it renders none of its own, or
/// when that depends on where it's used) and its parameters.
/// </summary>
internal sealed record ApiPart(string Name, string? Summary, ApiPartKind Kind, string? Element, IReadOnlyList<ApiParameter> Parameters, bool PassesAttributes);

/// <summary>
/// What an API Reference section lists: every component a registry item copies, or a primitive's parts, with their
/// parameters, types, defaults and summaries from the assembly's XML documentation. Read by reflection, so it can't
/// drift from the code.
/// </summary>
internal static partial class ApiReader
{
    private static readonly Assembly RegistryAssembly = typeof(Registry.Button).Assembly;
    private static readonly ConcurrentDictionary<string, IReadOnlyList<ApiComponent>> Cache = new();
    private static readonly ConcurrentDictionary<Type, ApiPart> Parts = new();
    private static readonly ConcurrentDictionary<Assembly, IReadOnlyDictionary<string, string>> Summaries = new();

    private static readonly Dictionary<Type, string> Aliases = new()
    {
        [typeof(string)] = "string", [typeof(bool)] = "bool", [typeof(int)] = "int", [typeof(double)] = "double",
        [typeof(float)] = "float", [typeof(long)] = "long", [typeof(object)] = "object", [typeof(decimal)] = "decimal",
    };

    public static IReadOnlyList<ApiComponent> For(string item) => Cache.GetOrAdd(item, static name =>
    {
        if (Sources.Item(name) is not { } registryItem)
        {
            return [];
        }
        var root = Sources.Pascal(name);
        return registryItem.Files
            .Where(f => f.EndsWith(".razor", StringComparison.Ordinal))
            .Select(Path.GetFileNameWithoutExtension)
            .OrderBy(n => n == root ? 0 : 1)
            .Select(n => RegistryAssembly.GetType($"Blazix.Registry.{n}"))
            .OfType<Type>()
            .Select(Read)
            .ToList();
    });

    /// <summary>A primitive part (<c>AccordionRoot</c>), as its API Reference shows it.</summary>
    public static ApiPart Part(Type type) => Parts.GetOrAdd(type, static t =>
    {
        var instance = Create(t);
        var parameters = Parameters(t, instance, out var passes);
        // The part's own parameters, then render, then the content.
        var ordered = parameters
            .OrderBy(p => p.Name switch { "Render" => 1, "ChildContent" => 2, _ => 0 })
            .Select(p => p.Summary is null ? p with { Summary = ConventionalSummary(p, parameters) } : p)
            .ToList();
        return new ApiPart(t.Name, SummaryOf($"T:{XmlName(t)}", t.Assembly), KindOf(t), ElementOf(t, instance), ordered, passes);
    });

    private static ApiPartKind KindOf(Type type)
    {
        if (RenderedPart(type) is not null)
        {
            return ApiPartKind.Element;
        }
        return typeof(IComponent).IsAssignableFrom(type) ? ApiPartKind.NoElement : ApiPartKind.NotComponent;
    }

    private static ApiComponent Read(Type type)
    {
        var parameters = Parameters(type, Create(type), out var passes);
        // ChildContent last.
        parameters.Sort((a, b) => (a.Name == "ChildContent").CompareTo(b.Name == "ChildContent"));
        return new ApiComponent(type.Name, parameters, passes);
    }

    private static object? Create(Type type)
    {
        try
        {
            return Activator.CreateInstance(type);
        }
        catch (Exception e) when (e is MissingMethodException or TargetInvocationException or MemberAccessException)
        {
            // No public parameterless constructor (or an abstract type): no defaults to show.
            return null;
        }
    }

    private static List<ApiParameter> Parameters(Type type, object? instance, out bool passesAttributes)
    {
        var nullability = new NullabilityInfoContext();
        var parameters = new List<ApiParameter>();
        passesAttributes = false;
        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            if (property.GetCustomAttribute<ParameterAttribute>() is not { } parameter)
            {
                continue;
            }
            if (parameter.CaptureUnmatchedValues)
            {
                passesAttributes = true;
                continue;
            }
            var value = instance is null ? null : ValueOf(property, instance);
            parameters.Add(new ApiParameter(
                property.Name,
                TypeName(property.PropertyType, nullability.Create(property)),
                DefaultOf(property.PropertyType, value),
                SummaryOf($"P:{XmlName(property.DeclaringType ?? type)}.{property.Name}", type.Assembly),
                property.GetCustomAttribute<EditorRequiredAttribute>() is not null));
        }
        return parameters;
    }

    private static object? ValueOf(PropertyInfo property, object instance)
    {
        try
        {
            return property.GetValue(instance);
        }
        catch (TargetInvocationException)
        {
            // A getter that needs the part's context: no default to show.
            return null;
        }
    }

    /// <summary>What the primitives' common parameters mean, wherever a part leaves them undocumented.</summary>
    private static readonly Dictionary<string, string> CommonSummaries = new()
    {
        ["ChildContent"] = "The part's content.",
        ["Disabled"] = "Whether the component should ignore user interaction.",
        ["ReadOnly"] = "Whether the user should be unable to change the value. It stays focusable.",
        ["Required"] = "Whether the user must enter a value before submitting a form.",
        ["Orientation"] = "Which way the parts are laid out, and which arrow keys move between them.",
        ["Min"] = "The minimum value.",
        ["Max"] = "The maximum value.",
        ["Step"] = "The amount the value changes by with the arrow keys or the buttons.",
        ["Culture"] = "The culture the value is formatted with.",
        ["OnValueChange"] = "Runs before the value changes, with the reason; call <code>Cancel()</code> to keep it.",
        ["OnCheckedChange"] = "Runs before the checked state changes; call <code>Cancel()</code> to keep it.",
        ["HighlightItemOnHover"] = "Whether moving the pointer over an item highlights it.",
    };

    /// <summary>What a primitive's undocumented parameter means, by Blazor's conventions.</summary>
    private static string? ConventionalSummary(ApiParameter parameter, IReadOnlyList<ApiParameter> parameters)
    {
        if (CommonSummaries.TryGetValue(parameter.Name, out var summary))
        {
            return summary;
        }
        if (parameter.Name.EndsWith("Changed", StringComparison.Ordinal)
            && parameters.FirstOrDefault(p => p.Name == parameter.Name[..^"Changed".Length]) is { } bound)
        {
            return $"Called with the new value when <code>{bound.Name}</code> changes, for <code>@bind-{bound.Name}</code>.";
        }
        return null;
    }

    /// <summary>The <c>PrimitivePart&lt;TRenderProps&gt;</c> a part derives from, or null for parts that render no element of their own.</summary>
    private static Type? RenderedPart(Type type)
    {
        for (var t = type; t is not null; t = t.BaseType)
        {
            if (t.IsGenericType && t.GetGenericTypeDefinition() == typeof(PrimitivePart<>))
            {
                return t;
            }
        }
        return null;
    }

    /// <summary>The element a part renders when there's no <c>Render</c>: its <c>Tag</c>.</summary>
    private static string? ElementOf(Type type, object? instance)
    {
#pragma warning disable S3011 // Read-only: the docs say which element a part renders, which its protected Tag knows.
        if (instance is null || RenderedPart(type)?.GetProperty("Tag", BindingFlags.Instance | BindingFlags.NonPublic) is not { } tag)
#pragma warning restore S3011
        {
            return null;
        }
        try
        {
            return tag.GetValue(instance) as string;
        }
        catch (TargetInvocationException)
        {
            // The tag depends on where the part is used (its context isn't there outside a page).
            return null;
        }
    }

    /// <summary>A type's name as XML documentation writes it: "Blazix.Primitives.PrimitivePart`1".</summary>
    private static string XmlName(Type type)
    {
        var definition = type.IsGenericType ? type.GetGenericTypeDefinition() : type;
        return (definition.FullName ?? definition.Name).Replace('+', '.');
    }

    private static string? SummaryOf(string member, Assembly assembly) =>
        Summaries.GetOrAdd(assembly, static a =>
        {
            // Embedded by the project file: in the browser there's no file next to the assembly to read.
            using var stream = typeof(ApiReader).Assembly.GetManifestResourceStream($"xmldoc/{a.GetName().Name}.xml");
            if (stream is null)
            {
                return new Dictionary<string, string>();
            }
            return XDocument.Load(stream).Descendants("member")
                .Where(m => m.Element("summary") is not null)
                .ToDictionary(m => (string)m.Attribute("name")!, m => SummaryHtml(m.Element("summary")!, OwnerOf((string)m.Attribute("name")!)));
        }).GetValueOrDefault(member);

    /// <summary>The type a documented member is on: "Blazix.Primitives.AccordionRoot" for "P:Blazix.Primitives.AccordionRoot.Value", or for the type itself.</summary>
    private static string OwnerOf(string member)
    {
        var name = member[(member.IndexOf(':') + 1)..].Split('(')[0];
        return member.StartsWith("T:", StringComparison.Ordinal) ? name : name[..Math.Max(0, name.LastIndexOf('.'))];
    }

    private static string TypeName(Type type, NullabilityInfo? nullability)
    {
        if (Nullable.GetUnderlyingType(type) is { } underlying)
        {
            return TypeName(underlying, null) + "?";
        }
        var nullable = nullability?.ReadState == NullabilityState.Nullable && !type.IsValueType ? "?" : "";
        if (Aliases.TryGetValue(type, out var alias))
        {
            return alias + nullable;
        }
        if (type.IsArray)
        {
            return TypeName(type.GetElementType()!, nullability?.ElementType) + "[]" + nullable;
        }
        if (!type.IsGenericType)
        {
            return type.Name + nullable;
        }
        var arguments = type.GetGenericArguments()
            .Select((t, i) => TypeName(t, nullability?.GenericTypeArguments.ElementAtOrDefault(i)));
        return $"{type.Name[..type.Name.IndexOf('`')]}<{string.Join(", ", arguments)}>{nullable}";
    }

    private static string? DefaultOf(Type type, object? value) => value switch
    {
        null => null,
        bool b => b ? "true" : "false",
        string s => s.Length == 0 ? null : $"\"{s}\"",
        Enum e => $"{type.Name}.{e}",
        IFormattable f when type.IsPrimitive => f.ToString(null, CultureInfo.InvariantCulture),
        _ => null,
    };

    /// <summary>A summary as HTML: &lt;c&gt; as code, &lt;see cref&gt; as the name in code, &lt;paramref&gt; likewise.</summary>
    private static string SummaryHtml(XElement summary, string owner)
    {
        var parts = summary.Nodes().Select(node => node switch
        {
            XText text => WebUtility.HtmlEncode(text.Value),
            XElement { Name.LocalName: "c" } c => $"<code>{WebUtility.HtmlEncode(c.Value)}</code>",
            XElement { Name.LocalName: "see" } see => $"<code>{WebUtility.HtmlEncode(CrefName((string?)see.Attribute("cref") ?? see.Value, owner))}</code>",
            XElement { Name.LocalName: "paramref" or "typeparamref" } p => $"<code>{WebUtility.HtmlEncode((string?)p.Attribute("name"))}</code>",
            XElement e => WebUtility.HtmlEncode(e.Value),
            _ => "",
        });
        return Whitespace().Replace(string.Concat(parts), " ").Trim();
    }

    /// <summary>
    /// How a summary names what it refers to: "Multiple" for a member of the type it documents, "PopupHandle.OpenAsync"
    /// for another type's member, "PrimitivePart" for a type.
    /// </summary>
    private static string CrefName(string cref, string owner)
    {
        var name = cref.Contains(':') ? cref[(cref.IndexOf(':') + 1)..] : cref;
        name = name.Split('(')[0];
        if (!cref.StartsWith("T:", StringComparison.Ordinal) && name.StartsWith(owner + ".", StringComparison.Ordinal))
        {
            return name[(owner.Length + 1)..];
        }
        var parts = GenericArity().Replace(name, "").Split('.');
        return parts.Length >= 2 && char.IsUpper(parts[^2][0]) && parts[^2] != "Registry" && parts[^2] != "Primitives"
            ? $"{parts[^2]}.{parts[^1]}"
            : parts[^1];
    }

    [GeneratedRegex(@"\s+")]
    private static partial Regex Whitespace();

    [GeneratedRegex(@"`\d+")]
    private static partial Regex GenericArity();
}
