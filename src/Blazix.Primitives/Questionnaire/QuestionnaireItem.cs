using JetBrains.Annotations;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Rendering;

namespace Blazix.Primitives;

/// <summary>An answer control an item tracks: a <see cref="QuestionnaireChoice"/> or a <see cref="QuestionnaireInput"/>.</summary>
internal interface IQuestionnaireAnswer
{
    string AnswerId { get; }

    bool IsInput { get; }

    bool IsDisabled { get; }

    /// <summary>What the answer submits when it's selected, or null when it submits nothing.</summary>
    string? SubmitValue { get; }

    /// <summary>After the item reset its selection: controlled answers re-apply their parent's state.</summary>
    void AfterReset();
}

/// <summary>
/// One question (<c>Questionnaire.Item</c>): a &lt;fieldset&gt; that's shown only while it's active.
/// Holds a <see cref="QuestionnaireTitle"/>, an optional <see cref="QuestionnaireDescription"/>,
/// <see cref="QuestionnaireChoices"/> and/or a <see cref="QuestionnaireInput"/>, and a <see cref="QuestionnaireError"/>.
/// </summary>
[PublicAPI]
public class QuestionnaireItem : PrimitivePart<QuestionnaireItemRenderProps>
{
    private readonly List<IQuestionnaireAnswer> _answers = [];
    private readonly List<string> _selected = [];
    private readonly List<string> _defaultSelected = [];
    private readonly List<string> _descriptionIds = [];
    private readonly List<string> _errorIds = [];
    private QuestionnaireItemStatus _reportedStatus = QuestionnaireItemStatus.Unanswered;
    private bool _skipped;
    private bool _validationAttempted;
    private bool? _wasMultiple;
    private string _id = "";

    [CascadingParameter] private QuestionnaireRoot? Root { get; set; }

    /// <summary>The item's name: its answers submit under it, and <c>Items</c>/<c>Item</c> refer to it.</summary>
    [Parameter, EditorRequired] public string Name { get; set; } = "";

    /// <summary>Must be answered: it can't be skipped.</summary>
    [Parameter] public bool Required { get; set; }

    /// <summary>Allow more than one choice (checkboxes instead of radios).</summary>
    [Parameter] public bool Multiple { get; set; }

    /// <summary>Leave the item out of the flow.</summary>
    [Parameter] public bool Disabled { get; set; }

    /// <summary>Mark the item invalid from outside, e.g. after your own validation.</summary>
    [Parameter] public bool Invalid { get; set; }

    /// <summary>Runs when the item becomes answered, unanswered or skipped.</summary>
    [Parameter] public EventCallback<QuestionnaireItemStatus> OnStatusChange { get; set; }

    protected override string Tag => "fieldset";

    private QuestionnaireRoot Questionnaire => Root ?? throw new InvalidOperationException("QuestionnaireItem must be inside a QuestionnaireRoot.");

    internal string ElementId => _id;

    internal bool IsDisabled => Disabled;

    internal bool IsActive => !Disabled && Questionnaire.ActiveName == Name;

    private IEnumerable<IQuestionnaireAnswer> EnabledAnswers => _answers.Where(answer => !answer.IsDisabled);

    internal QuestionnaireItemStatus Status
    {
        get
        {
            if (_skipped)
            {
                return QuestionnaireItemStatus.Skipped;
            }
            return EnabledAnswers.Any(answer => _selected.Contains(answer.AnswerId)) ? QuestionnaireItemStatus.Answered : QuestionnaireItemStatus.Unanswered;
        }
    }

    private bool IntentionallySkipped => Status == QuestionnaireItemStatus.Skipped && !Required;

    private bool IsValid => Disabled || IntentionallySkipped || (!Invalid && Status == QuestionnaireItemStatus.Answered);

    internal bool IsInvalid => !Disabled && !IntentionallySkipped && (Invalid || (_validationAttempted && !IsValid));

    internal bool HasInputAnswer => EnabledAnswers.Any(answer => answer.IsInput);

    internal QuestionnaireShortcuts? Shortcuts => Questionnaire.ShortcutMode;

    internal bool IsSelected(string answerId) => _selected.Contains(answerId);

    protected override void OnInitialized() => Questionnaire.Register(this);

    protected override void OnParametersSet()
    {
        _id = ElementAttributes.IdOr(AdditionalAttributes, () => _id is { Length: > 0 } ? _id : Ids.Next("questionnaire-item"));
        if (_wasMultiple == true && !Multiple)
        {
            // Back to one answer: keep the first one selected.
            var kept = EnabledAnswers.FirstOrDefault(answer => _selected.Contains(answer.AnswerId));
            _selected.Clear();
            if (kept is not null)
            {
                _selected.Add(kept.AnswerId);
            }
        }
        _wasMultiple = Multiple;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        var status = Status;
        if (status != _reportedStatus)
        {
            _reportedStatus = status;
            await OnStatusChange.InvokeAsync(status);
        }
    }

    /// <summary>The shortcut key of a choice: by its value in the item's definition, else by its position.</summary>
    internal string? ShortcutFor(QuestionnaireChoice choice)
    {
        var keys = QuestionnaireKeys.For(Shortcuts);
        if (keys.Count == 0)
        {
            return null;
        }

        if (Questionnaire.HasDefinitions)
        {
            var enabled = (Questionnaire.Definition(Name)?.Choices ?? []).Where(definition => !definition.Disabled).Take(keys.Count).ToList();
            var index = enabled.FindIndex(definition => definition.Value == choice.Value);
            return index < 0 ? null : keys[index];
        }

        var position = EnabledAnswers.Where(answer => !answer.IsInput).ToList().IndexOf(choice);
        return position >= 0 && position < keys.Count ? keys[position] : null;
    }

    internal void RegisterAnswer(IQuestionnaireAnswer answer, bool defaultSelected)
    {
        if (!_answers.Contains(answer))
        {
            _answers.Add(answer);
        }
        if (defaultSelected)
        {
            SetAnswerDefault(answer.AnswerId, true);
            if (Multiple ? !_selected.Contains(answer.AnswerId) : _selected.Count == 0)
            {
                _selected.Add(answer.AnswerId);
            }
        }
        Questionnaire.Changed();
    }

    internal void UnregisterAnswer(IQuestionnaireAnswer answer)
    {
        _answers.Remove(answer);
        _defaultSelected.Remove(answer.AnswerId);
        _selected.Remove(answer.AnswerId);
        Questionnaire.Changed();
    }

    internal void SetAnswerDefault(string answerId, bool selected)
    {
        _defaultSelected.Remove(answerId);
        if (selected)
        {
            _defaultSelected.Add(answerId);
        }
    }

    private void UpdateSelected(string answerId, bool selected)
    {
        if (!selected)
        {
            _selected.Remove(answerId);
        }
        else if (!Multiple)
        {
            _selected.Clear();
            _selected.Add(answerId);
        }
        else if (!_selected.Contains(answerId))
        {
            _selected.Add(answerId);
        }
        Questionnaire.Changed();
    }

    /// <summary>The user picked or cleared an answer: that also takes back a skip.</summary>
    internal void SelectFromInteraction(string answerId, bool selected)
    {
        _skipped = false;
        UpdateSelected(answerId, selected);
    }

    /// <summary>A controlled answer's parent changed it.</summary>
    internal void SyncControlled(string answerId, bool selected)
    {
        if (selected)
        {
            _skipped = false;
        }
        UpdateSelected(answerId, selected);
    }

    internal void RegisterDescription(string id) => AddId(_descriptionIds, id);

    internal void UnregisterDescription(string id) => RemoveId(_descriptionIds, id);

    internal void RegisterError(string id) => AddId(_errorIds, id);

    internal void UnregisterError(string id) => RemoveId(_errorIds, id);

    private void AddId(List<string> ids, string id)
    {
        if (!ids.Contains(id))
        {
            ids.Add(id);
            Questionnaire.Changed();
        }
    }

    private void RemoveId(List<string> ids, string id)
    {
        if (ids.Remove(id))
        {
            Questionnaire.Changed();
        }
    }

    internal async Task<bool> ValidateAsync()
    {
        _validationAttempted = true;
        Questionnaire.Changed();
        if (!IsValid)
        {
            return false;
        }
        return !Questionnaire.NativeValidation || await Questionnaire.ReportValidityAsync(this);
    }

    internal void Skip()
    {
        if (Required)
        {
            return;
        }
        _selected.Clear();
        _skipped = true;
        Questionnaire.Changed();
    }

    internal void Reset()
    {
        _validationAttempted = false;
        _skipped = false;
        _selected.Clear();
        _selected.AddRange(Multiple ? _defaultSelected : _defaultSelected.Take(1));
        foreach (var answer in _answers.ToList())
        {
            answer.AfterReset();
        }
        Questionnaire.Changed();
    }

    /// <summary>Adds what the form would submit for this item: nothing once skipped.</summary>
    internal void CollectAnswers(QuestionnaireAnswers answers)
    {
        if (_skipped)
        {
            return;
        }
        foreach (var answer in EnabledAnswers)
        {
            if (answer.SubmitValue is { } value)
            {
                answers.Add(Name, value);
            }
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            Root?.Unregister(this);
        }
        base.Dispose(disposing);
    }

    protected override void BuildRenderTree(RenderTreeBuilder builder) => Cascade(builder, this, BuildPart);

    protected override void BuildAttributes(AttributeBuilder attributes)
    {
        var active = IsActive;
        var invalid = IsInvalid;
        var status = Status;
        var authoredDescribedBy = Authored("aria-describedby");
        var authoredShortcuts = Authored("aria-keyshortcuts");
        string?[] describedBy = [.._descriptionIds, ..(invalid ? _errorIds : []), authoredDescribedBy];
        string?[] shortcuts =
        [
            authoredShortcuts,
            active ? "Meta+Enter Control+Enter" : null,
            active && EnabledAnswers.Any() ? "ArrowUp ArrowDown" : null,
            active && !Questionnaire.First ? "ArrowLeft" : null,
            active && !Questionnaire.Last && status != QuestionnaireItemStatus.Unanswered ? "ArrowRight" : null,
        ];

        attributes
            .Set("id", _id)
            .Set("tabindex", "-1")
            .Set("hidden", !active)
            .Set("inert", !active)
            .Set("disabled", Disabled)
            .Set("aria-describedby", Join(describedBy))
            .Set("aria-invalid", invalid ? "true" : null)
            .Set("aria-keyshortcuts", Join(shortcuts))
            .Flag("data-active", active)
            .Flag("data-disabled", Disabled)
            .Flag("data-invalid", invalid)
            .Flag("data-multiple", Multiple)
            .Flag("data-required", Required)
            .Set("data-status", QuestionnaireKeys.Name(status));
    }

    private string? Authored(string name) =>
        AdditionalAttributes is not null && AdditionalAttributes.TryGetValue(name, out var value) ? value.ToString() : null;

    private static string? Join(IEnumerable<string?> values)
    {
        var joined = string.Join(' ', values.Where(value => !string.IsNullOrEmpty(value)));
        return joined.Length > 0 ? joined : null;
    }

    protected override QuestionnaireItemRenderProps CreateRenderProps(IReadOnlyDictionary<string, object> attributes) =>
        new(attributes, IsActive, Disabled, IsInvalid, Multiple, Required, Status);
}
