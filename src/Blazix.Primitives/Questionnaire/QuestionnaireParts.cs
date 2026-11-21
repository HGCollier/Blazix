using JetBrains.Annotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Blazix.Primitives;

/// <summary>The question (<c>Questionnaire.Title</c>): the item's &lt;legend&gt;, which names the fieldset.</summary>
[PublicAPI]
public class QuestionnaireTitle : SimplePart
{
    protected override string Tag => "legend";

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        // The legend names its fieldset natively: no attributes of its own.
    }
}

/// <summary>Help text for the question (<c>Questionnaire.Description</c>). The item is described by it.</summary>
[PublicAPI]
public class QuestionnaireDescription : SimplePart
{
    private string _id = "";

    [CascadingParameter] private QuestionnaireItem? Item { get; set; }

    protected override string Tag => "p";

    protected override void OnParametersSet()
    {
        var id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("questionnaire-description"));
        if (id != _id)
        {
            Item?.UnregisterDescription(_id);
            _id = id;
            Item?.RegisterDescription(_id);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Item?.UnregisterDescription(_id);
        }
        base.Dispose(disposing);
    }

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes.Set("id", _id);
}

/// <summary>Wraps an item's choices (<c>Questionnaire.Choices</c>). Renders <c>data-shortcuts</c> when shortcuts are on.</summary>
[PublicAPI]
public class QuestionnaireChoices : PrimitivePart<QuestionnaireChoicesRenderProps>
{
    [CascadingParameter] private QuestionnaireItem? Item { get; set; }

    protected override void BuildAttributes(AttributeBuilder attributes) =>
        attributes.Set("data-shortcuts", QuestionnaireKeys.Name(Item?.Shortcuts));

    protected override QuestionnaireChoicesRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Item?.Shortcuts);
}

/// <summary>
/// A freeform answer (<c>Questionnaire.Input</c>): an &lt;input&gt; that answers the item once it holds
/// more than whitespace. In a single-answer item, typing clears the choices and choosing clears the text.
/// </summary>
[PublicAPI]
public class QuestionnaireInput : PrimitivePart<QuestionnaireInputRenderProps>, IQuestionnaireAnswer
{
    private readonly string _answerId = Ids.Next("questionnaire-input");
    private EventCallback<ChangeEventArgs> _onInput;
    private string? _text;
    private string? _lastValue;
    private bool _synced;

    [CascadingParameter] private QuestionnaireItem? Item { get; set; }

    /// <summary>The text, when the parent controls it (<c>@bind-Value</c>).</summary>
    [Parameter] public string? Value { get; set; }

    [Parameter] public EventCallback<string?> ValueChanged { get; set; }

    /// <summary>The initial text, and the text after a reset.</summary>
    [Parameter] public string? DefaultValue { get; set; }

    [Parameter] public bool Disabled { get; set; }

    /// <summary>The input's type. Defaults to "text".</summary>
    [Parameter] public string Type { get; set; } = "text";

    protected override string Tag => "input";

    protected override bool IsVoid => true;

    private QuestionnaireItem QuestionnaireItem => Item ?? throw new InvalidOperationException("QuestionnaireInput must be inside a QuestionnaireItem.");

    private bool IsControlled => Value is not null || ValueChanged.HasDelegate;

    private string? Text => IsControlled ? Value : _text;

    private bool IsDisabled => QuestionnaireItem.IsDisabled || Disabled;

    private bool IsFilled => QuestionnaireKeys.HasValue(Text);

    private bool IsSelected => QuestionnaireItem.IsSelected(_answerId);

    string IQuestionnaireAnswer.AnswerId => _answerId;

    bool IQuestionnaireAnswer.IsInput => true;

    bool IQuestionnaireAnswer.IsDisabled => IsDisabled;

    string? IQuestionnaireAnswer.SubmitValue => IsSelected ? Text ?? "" : null;

    void IQuestionnaireAnswer.AfterReset()
    {
        if (IsControlled)
        {
            QuestionnaireItem.SyncControlled(_answerId, IsFilled);
        }
        else
        {
            _text = DefaultValue;
        }
    }

    protected override void OnInitialized()
    {
        _text = DefaultValue;
        _onInput = EventCallback.Factory.Create<ChangeEventArgs>(this, HandleInputAsync);
        QuestionnaireItem.RegisterAnswer(this, QuestionnaireKeys.HasValue(DefaultValue));
    }

    protected override void OnParametersSet()
    {
        QuestionnaireItem.SetAnswerDefault(_answerId, QuestionnaireKeys.HasValue(DefaultValue));
        if (IsControlled && (!_synced || Value != _lastValue))
        {
            _synced = true;
            _lastValue = Value;
            QuestionnaireItem.SyncControlled(_answerId, QuestionnaireKeys.HasValue(Value));
        }
    }

    private async Task HandleInputAsync(ChangeEventArgs args)
    {
        var text = args.Value as string;
        await ValueChanged.InvokeAsync(text);
        if (!IsControlled)
        {
            _text = text;
            QuestionnaireItem.SelectFromInteraction(_answerId, QuestionnaireKeys.HasValue(text));
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Item?.UnregisterAnswer(this);
        }
        base.Dispose(disposing);
    }

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        var selected = IsSelected;
        var filled = IsFilled;
        var invalid = QuestionnaireItem.IsInvalid;
        attributes
            .Set("id", ElementAttributes.IdOr(AdditionalAttributes, () => _answerId))
            .Set("type", Type)
            .Set("name", selected ? QuestionnaireItem.Name : null)
            .Set("form", selected ? null : "")
            .Set("value", Text ?? "")
            .Set("disabled", IsDisabled)
            .Set("aria-invalid", invalid ? "true" : null)
            .Set("aria-keyshortcuts", !IsDisabled && filled && selected ? "Enter" : null)
            .Set("oninput", _onInput)
            .Flag("data-filled", filled)
            .Flag("data-empty", !filled)
            .Flag("data-disabled", IsDisabled)
            .Flag("data-invalid", invalid);
    }

    protected override QuestionnaireInputRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, IsDisabled, IsFilled, QuestionnaireItem.IsInvalid);
}

/// <summary>
/// The item's error (<c>Questionnaire.Error</c>): hidden until the item is invalid, then announced.
/// Defaults to "Choose an answer to continue." or, for an optional item, "Choose an answer or skip this question."
/// </summary>
[PublicAPI]
public class QuestionnaireError : PrimitivePart<QuestionnaireErrorRenderProps>
{
    private string _id = "";

    [CascadingParameter] private QuestionnaireItem? Item { get; set; }

    protected override string Tag => "p";

    private bool IsInvalid => Item?.IsInvalid ?? false;

    protected override void OnParametersSet()
    {
        var id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("questionnaire-error"));
        if (id != _id)
        {
            Item?.UnregisterError(_id);
            _id = id;
            Item?.RegisterError(_id);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Item?.UnregisterError(_id);
        }
        base.Dispose(disposing);
    }

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("id", _id)
        .Set("hidden", !IsInvalid)
        .Set("role", IsInvalid ? "alert" : null)
        .Flag("data-invalid", IsInvalid);

    protected override void BuildChildren(RenderTreeBuilder builder)
    {
        if (ChildContent is not null)
        {
            builder.AddContent(3, ChildContent);
        }
        else
        {
            builder.AddContent(4, Item?.Required == true ? "Choose an answer to continue." : "Choose an answer or skip this question.");
        }
    }

    protected override QuestionnaireErrorRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes, IsInvalid);
}

/// <summary>
/// Where the questionnaire is (<c>Questionnaire.Progress</c>): a progressbar reading "Question 2 of 5".
/// Pass your own content, or use <c>Render</c>, which gets the current item and the total.
/// </summary>
[PublicAPI]
public class QuestionnaireProgress : PrimitivePart<QuestionnaireRenderProps>
{
    [CascadingParameter] private QuestionnaireRoot? Root { get; set; }

    private int Total => Root?.Total ?? 0;

    private int Current => Root?.Current ?? 0;

    private string? Label => Total > 0 ? $"Question {Current} of {Total}" : null;

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        var total = Total;
        attributes
            .Set("role", "progressbar")
            .Set("aria-label", "Questionnaire progress")
            .Set("aria-live", "polite")
            .Set("aria-valuemin", total > 0 ? "1" : null)
            .Set("aria-valuemax", total > 0 ? Number(total) : null)
            .Set("aria-valuenow", total > 0 ? Number(Current) : null)
            .Set("aria-valuetext", Label)
            .Set("data-current", Number(Current))
            .Set("data-total", Number(total))
            .Flag("data-first", Root?.First ?? false)
            .Flag("data-last", Root?.Last ?? false);
    }

    private static string Number(int value) => value.ToString(System.Globalization.CultureInfo.InvariantCulture);

    protected override void BuildChildren(RenderTreeBuilder builder)
    {
        if (ChildContent is not null)
        {
            builder.AddContent(3, ChildContent);
        }
        else
        {
            builder.AddContent(4, Label);
        }
    }

    protected override QuestionnaireRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, Current, Total, Root?.First ?? false, Root?.Last ?? false);
}
