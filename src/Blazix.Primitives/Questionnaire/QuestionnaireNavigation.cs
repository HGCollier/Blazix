using JetBrains.Annotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;
using Microsoft.AspNetCore.Components.Web;

namespace Blazix.Primitives;

/// <summary>
/// The questionnaire's navigation buttons. Each stays rendered when it doesn't apply, but hidden,
/// inert and out of the tab order (<c>data-hidden</c>), so a layout can keep its place.
/// </summary>
public abstract class QuestionnaireNavigationPart : PrimitivePart<QuestionnaireNavigationRenderProps>
{
    private EventCallback<MouseEventArgs> _onClick;

    [CascadingParameter] private protected QuestionnaireRoot? Root { get; set; }

    [Parameter] public bool Disabled { get; set; }

    protected override string Tag => "button";

    private protected abstract bool Visible { get; }

    private protected abstract string DefaultText { get; }

    private protected virtual string ButtonType => "button";

    /// <summary>"Enter" for the buttons Enter stands for (Next and Submit).</summary>
    private protected virtual string? KeyShortcut => null;

    private protected virtual Task ClickAsync() => Task.CompletedTask;

    private string? ActiveShortcut => Visible && !Disabled ? KeyShortcut : null;

    protected override void OnInitialized() => _onClick = EventCallback.Factory.Create<MouseEventArgs>(this, ClickAsync);

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        var visible = Visible;
        attributes
            .Default("type", ButtonType)
            .Set("disabled", Disabled)
            .Set("hidden", !visible)
            .Set("inert", !visible)
            .Set("aria-hidden", visible ? null : "true")
            .Set("aria-keyshortcuts", ActiveShortcut)
            .Set("onclick", _onClick)
            .Flag("data-disabled", Disabled)
            .Set("data-shortcut", ActiveShortcut)
            .Set("data-status", Root?.ActiveItemStatus is { } status ? QuestionnaireKeys.Name(status) : null)
            .Flag("data-visible", visible)
            .Flag("data-hidden", !visible);
        if (!visible)
        {
            attributes.Set("tabindex", "-1");
        }
    }

    protected override void BuildChildren(RenderTreeBuilder builder)
    {
        if (ChildContent is not null)
        {
            builder.AddContent(3, ChildContent);
        }
        else
        {
            builder.AddContent(4, DefaultText);
        }
    }

    protected override QuestionnaireNavigationRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Disabled, ActiveShortcut, Root?.ActiveItemStatus, Visible);
}

/// <summary>Goes back an item (<c>Questionnaire.Previous</c>). Hidden on the first item.</summary>
[PublicAPI]
public class QuestionnairePrevious : QuestionnaireNavigationPart
{
    private protected override bool Visible => Root is { Total: > 1, First: false };

    private protected override string DefaultText => "Previous";

    private protected override Task ClickAsync() => Root?.GoPreviousAsync() ?? Task.CompletedTask;
}

/// <summary>Skips the active item (<c>Questionnaire.Skip</c>). Shown only for items that aren't required; skipping the last one submits.</summary>
[PublicAPI]
public class QuestionnaireSkip : QuestionnaireNavigationPart
{
    private protected override bool Visible => Root?.ActiveItemRequired == false;

    private protected override string DefaultText => "Skip";

    private protected override Task ClickAsync() => Root?.SkipAsync() ?? Task.CompletedTask;
}

/// <summary>Validates the active item and moves to the next (<c>Questionnaire.Next</c>). Hidden on the last item.</summary>
[PublicAPI]
public class QuestionnaireNext : QuestionnaireNavigationPart
{
    private protected override bool Visible => Root is { Total: > 1, Last: false };

    private protected override string DefaultText => "Next";

    private protected override string KeyShortcut => "Enter";

    private protected override Task ClickAsync() => Root?.GoNextAsync() ?? Task.CompletedTask;
}

/// <summary>Submits the questionnaire (<c>Questionnaire.Submit</c>): a submit button, shown on the last item.</summary>
[PublicAPI]
public class QuestionnaireSubmit : QuestionnaireNavigationPart
{
    private protected override bool Visible => Root is { Total: > 0, Last: true };

    private protected override string DefaultText => "Submit";

    private protected override string ButtonType => "submit";

    private protected override string KeyShortcut => "Enter";
}
