using JetBrains.Annotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Blazix.Primitives;

/// <summary>
/// A fixed answer (<c>Questionnaire.Choice</c>): a &lt;label&gt; around a <see cref="QuestionnaireChoiceInput"/>,
/// which is a radio in a single-answer item and a checkbox in a <c>Multiple</c> one.
/// </summary>
[PublicAPI]
public class QuestionnaireChoice : PrimitivePart<QuestionnaireChoiceRenderProps>, IQuestionnaireAnswer
{
    private readonly string _answerId = Ids.Next("questionnaire-choice");
    private bool? _lastChecked;

    [CascadingParameter] private QuestionnaireItem? Item { get; set; }

    /// <summary>The value submitted under the item's name.</summary>
    [Parameter, EditorRequired] public string Value { get; set; } = "";

    /// <summary>Whether it's checked, when the parent controls it. Leave unset to let the item track it.</summary>
    [Parameter] public bool? Checked { get; set; }

    /// <summary>Runs when the user checks or unchecks it (the input's change event).</summary>
    [Parameter] public EventCallback<bool> CheckedChanged { get; set; }

    /// <summary>Checked initially, and after a reset.</summary>
    [Parameter] public bool DefaultChecked { get; set; }

    [Parameter] public bool Disabled { get; set; }

    protected override string Tag => "label";

    private QuestionnaireItem QuestionnaireItem => Item ?? throw new InvalidOperationException("QuestionnaireChoice must be inside a QuestionnaireItem.");

    internal string AnswerId => _answerId;

    internal bool IsDisabled => QuestionnaireItem.IsDisabled || Disabled;

    internal bool IsChecked => Checked is { } controlled
        ? QuestionnaireItem.Status != QuestionnaireItemStatus.Skipped && controlled
        : QuestionnaireItem.IsSelected(_answerId);

    internal string Type => QuestionnaireItem.Multiple ? "checkbox" : "radio";

    internal string? Shortcut => QuestionnaireItem.ShortcutFor(this);

    internal string ItemName => QuestionnaireItem.Name;

    internal bool IsSkipped => QuestionnaireItem.Status == QuestionnaireItemStatus.Skipped;

    internal bool IsRequired => QuestionnaireItem is { Required: true, Multiple: false, HasInputAnswer: false };

    internal bool IsInvalid => QuestionnaireItem.IsInvalid;

    string IQuestionnaireAnswer.AnswerId => _answerId;

    bool IQuestionnaireAnswer.IsInput => false;

    bool IQuestionnaireAnswer.IsDisabled => IsDisabled;

    string? IQuestionnaireAnswer.SubmitValue => IsChecked ? Value : null;

    void IQuestionnaireAnswer.AfterReset()
    {
        if (Checked is { } controlled)
        {
            QuestionnaireItem.SyncControlled(_answerId, controlled);
        }
    }

    protected override void OnInitialized() => QuestionnaireItem.RegisterAnswer(this, DefaultChecked);

    protected override void OnParametersSet()
    {
        QuestionnaireItem.SetAnswerDefault(_answerId, DefaultChecked);
        if (Checked is { } controlled && controlled != _lastChecked)
        {
            _lastChecked = controlled;
            QuestionnaireItem.SyncControlled(_answerId, controlled);
        }
    }

    internal async Task ChangeAsync(ChangeEventArgs args)
    {
        var isChecked = Type == "radio" || args.Value is true;
        await CheckedChanged.InvokeAsync(isChecked);
        if (Checked is null || (IsSkipped && Checked == isChecked))
        {
            QuestionnaireItem.SelectFromInteraction(_answerId, isChecked);
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

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Cascade(builder, this, BuildPart);

    internal AttributeBuilder AddState(AttributeBuilder attributes)
    {
        var isChecked = IsChecked;
        return attributes
            .Flag("data-checked", isChecked)
            .Flag("data-unchecked", !isChecked)
            .Flag("data-disabled", IsDisabled)
            .Flag("data-invalid", IsInvalid)
            .Set("data-shortcut", Shortcut)
            .Set("data-type", Type);
    }

    protected override void BuildAttributes(AttributeBuilder attributes) => AddState(attributes);

    internal QuestionnaireChoiceRenderProps CreateState(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, IsChecked, IsDisabled, IsInvalid, Shortcut, Type);

    protected override QuestionnaireChoiceRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => CreateState(attributes);
}

/// <summary>The choice's native radio or checkbox (<c>Questionnaire.ChoiceInput</c>). Style it, or hide it visually.</summary>
[PublicAPI]
public class QuestionnaireChoiceInput : PrimitivePart<QuestionnaireChoiceRenderProps>
{
    private EventCallback<ChangeEventArgs> _onChange;

    [CascadingParameter] private QuestionnaireChoice? Choice { get; set; }

    protected override string Tag => "input";

    protected override bool IsVoid => true;

    private QuestionnaireChoice Owner => Choice ?? throw new InvalidOperationException("QuestionnaireChoiceInput must be inside a QuestionnaireChoice.");

    protected override void OnInitialized() => _onChange = EventCallback.Factory.Create<ChangeEventArgs>(this, args => Owner.ChangeAsync(args));

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        var choice = Owner;
        var isChecked = choice.IsChecked;
        string?[] shortcuts = [choice.Shortcut, !choice.IsDisabled && isChecked ? "Enter" : null];
        var keyShortcuts = string.Join(' ', shortcuts.OfType<string>());
        attributes
            .Set("id", choice.AnswerId)
            .Set("type", choice.Type)
            .Set("name", choice.IsSkipped ? null : choice.ItemName)
            .Set("value", choice.Value)
            .Set("checked", isChecked)
            .Set("disabled", choice.IsDisabled)
            .Set("required", choice.IsRequired)
            .Set("aria-invalid", choice.IsInvalid ? "true" : null)
            .Set("aria-keyshortcuts", keyShortcuts.Length > 0 ? keyShortcuts : null)
            .Set("onchange", _onChange);
        choice.AddState(attributes);
    }

    protected override QuestionnaireChoiceRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => Owner.CreateState(attributes);
}

/// <summary>The choice's text (<c>Questionnaire.ChoiceLabel</c>): a &lt;span&gt;.</summary>
[PublicAPI]
public class QuestionnaireChoiceLabel : SimplePart
{
    protected override string Tag => "span";

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        // A plain span: no attributes or state of its own.
    }
}

/// <summary>
/// The choice's shortcut key, e.g. "A" (<c>Questionnaire.ChoiceShortcut</c>). Hidden from assistive
/// technology (the input announces it), and hidden altogether when the choice has no shortcut.
/// </summary>
[PublicAPI]
public class QuestionnaireChoiceShortcut : PrimitivePart<QuestionnaireChoiceShortcutRenderProps>
{
    [CascadingParameter] private QuestionnaireChoice? Choice { get; set; }

    protected override string Tag => "span";

    private string? Shortcut => Choice?.Shortcut;

    protected override void BuildAttributes(AttributeBuilder attributes) => attributes
        .Set("aria-hidden", "true")
        .Set("hidden", Shortcut is null)
        .Set("data-shortcut", Shortcut);

    protected override void BuildChildren(RenderTreeBuilder builder)
    {
        if (ChildContent is not null)
        {
            builder.AddContent(3, ChildContent);
        }
        else
        {
            builder.AddContent(4, Shortcut);
        }
    }

    protected override QuestionnaireChoiceShortcutRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) => new(attributes, Shortcut);
}
