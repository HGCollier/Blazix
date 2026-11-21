namespace Blazix.Primitives;

/// <summary>
/// Open state of a popup, with a presence model: closing keeps the popup mounted (marked
/// closed and inert) until its exit animation finishes, then <see cref="CompleteExit"/> unmounts it.
/// Reopening mid-exit cancels the exit and keeps the same element.
/// </summary>
internal sealed class PopupState
{
    private bool _seeded;
    private bool _lastParameter;
    private bool _exiting;
    private bool _enterPending;

    public bool IsOpen { get; private set; }

    /// <summary>True while the popup plays its exit animation. <see cref="IsOpen"/> is already false.</summary>
    public bool IsExiting => !IsOpen && _exiting;

    /// <summary>In the DOM: while open, and while exiting.</summary>
    public bool IsMounted => IsOpen || _exiting;

    public OpenChangeReason Reason { get; private set; } = OpenChangeReason.None;

    /// <summary>
    /// Whether showing the popup plays its enter transition (from <c>data-starting-style</c>). Not
    /// when it's open on the root's first render: content that mounts open, such as a
    /// drawer that takes a dialog's place at a breakpoint, appears as it is. Opening it later animates.
    /// </summary>
    public bool AnimatesEnter { get; private set; } = true;

    /// <summary>Opened and about to play its enter transition, which hasn't started yet.</summary>
    public bool IsStarting => IsOpen && _enterPending && AnimatesEnter;

    /// <summary>
    /// Adopts the parent's value when it changes. Returns true when the open state changed.
    /// <paramref name="animateInitialOpen"/> decides whether a popup that's open on the first call
    /// still animates in.
    /// </summary>
    public bool SyncParameter(bool open, bool animateInitialOpen = false)
    {
        if (_seeded && _lastParameter == open)
        {
            return false;
        }

        var initial = !_seeded;
        _seeded = true;
        _lastParameter = open;
        if (IsOpen == open)
        {
            return false;
        }

        Apply(open, OpenChangeReason.ImperativeAction);
        if (initial)
        {
            AnimatesEnter = animateInitialOpen;
        }
        return true;
    }

    /// <summary>Whether adopting this parameter value would close an open popup (so it can prepare first).</summary>
    public bool ParameterCloses(bool open) => (!_seeded || _lastParameter != open) && !open && IsOpen;

    public void Apply(bool open, OpenChangeReason reason)
    {
        // Reopening during the exit animation isn't an enter: it transitions back from where it is.
        _enterPending = open && (_enterPending || !IsMounted);
        _exiting = !open && (IsOpen || _exiting);
        IsOpen = open;
        Reason = reason;
        if (!open)
        {
            AnimatesEnter = true;
        }
    }

    /// <summary>The popup was shown: its enter transition, if any, has started.</summary>
    public void Shown() => _enterPending = false;

    /// <summary>Unmounts after the exit animation. False if it reopened meanwhile.</summary>
    public bool CompleteExit()
    {
        if (IsOpen || !_exiting)
        {
            return false;
        }
        _exiting = false;
        return true;
    }

    /// <summary>
    /// Keyboard, item, close-button and outside-press closes hand focus back to the trigger;
    /// the browser side only does so when focus was in the popup or dropped to the page, so a
    /// press on another control keeps its focus. Focus leaving and hover don't: the user has already
    /// put their attention somewhere else.
    /// </summary>
    public bool RestoresFocus => Reason is not (OpenChangeReason.FocusOut or OpenChangeReason.TriggerHover
        or OpenChangeReason.SiblingOpen);

    public static OpenChangeReason ParseReason(string reason) => reason switch
    {
        "escape-key" => OpenChangeReason.EscapeKey,
        "outside-press" => OpenChangeReason.OutsidePress,
        "focus-out" => OpenChangeReason.FocusOut,
        "trigger-press" => OpenChangeReason.TriggerPress,
        "trigger-hover" => OpenChangeReason.TriggerHover,
        "trigger-focus" => OpenChangeReason.TriggerFocus,
        "item-press" => OpenChangeReason.ItemPress,
        "close-press" => OpenChangeReason.ClosePress,
        "list-navigation" => OpenChangeReason.ListNavigation,
        "sibling-open" => OpenChangeReason.SiblingOpen,
        "cancel-open" => OpenChangeReason.CancelOpen,
        "swipe" => OpenChangeReason.Swipe,
        _ => OpenChangeReason.None,
    };
}

/// <summary>
/// Drives a popup part's show/hide from what was actually rendered. A popup part snapshots open
/// state while rendering; OnAfterRenderAsync acts on that snapshot rather than live state. (On
/// Blazor Server an earlier batch can be acknowledged after Open has already flipped; reading live
/// state there started the exit animation against stale DOM.)
/// </summary>
internal sealed class PopupPresence
{
    private enum Phase { Hidden, Shown, Exiting }

    private Phase _phase;
    private bool _renderedOpen;
    private string? _renderedKey;
    private string? _shownKey;

    /// <summary>Call while rendering. <paramref name="key"/> changes when options that JS reads change.</summary>
    public void Snapshot(bool open, string? key = null)
    {
        _renderedOpen = open;
        _renderedKey = key;
    }

    public bool IsShown => _phase == Phase.Shown;

    public async Task AfterRenderAsync(Func<Task> show, Func<Task<bool>> hide, Func<bool> isOpen, Func<bool> isExiting, Action completeExit)
    {
        switch (_renderedOpen, _phase)
        {
            case (true, not Phase.Shown):
                // Also covers reopening mid-exit: JS cancels the exit and carries on from there.
                _phase = Phase.Shown;
                _shownKey = _renderedKey;
                await show();
                break;

            case (true, Phase.Shown) when _renderedKey != _shownKey:
                // Placement or behavior options changed while open.
                _shownKey = _renderedKey;
                await show();
                break;

            case (false, Phase.Shown):
                // The DOM now shows the closed state, so the exit animation has started and JS can wait for it.
                _phase = Phase.Exiting;
                var finished = await hide();
                if (finished && !isOpen() && _phase == Phase.Exiting)
                {
                    _phase = Phase.Hidden;
                    completeExit();
                }
                break;

            case (false, Phase.Hidden) when isExiting():
                // Closed again before it was ever shown: nothing to animate.
                completeExit();
                break;
        }
    }
}
