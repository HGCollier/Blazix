namespace Blazix.Primitives;

/// <summary>
/// Connects triggers to a popup root that isn't around them, and
/// lets several triggers share one popup: give the same handle to the root's <c>Handle</c> and to
/// each trigger's <c>Handle</c>. It can also open and close the popup from code.
/// </summary>
public class PopupHandle
{
    private event Action? Changed;

    /// <summary>The root using this handle, once it has rendered.</summary>
    public PopupRootBase? Root { get; private set; }

    public bool IsOpen => Root?.IsOpen ?? false;

    /// <summary>The payload of the trigger the popup is open against.</summary>
    public object? Payload => Root?.Payload;

    internal void Attach(PopupRootBase root)
    {
        if (ReferenceEquals(Root, root))
        {
            return;
        }
        Root = root;
        Changed?.Invoke();
    }

    internal void Subscribe(Action listener) => Changed += listener;

    internal void Unsubscribe(Action listener) => Changed -= listener;

    /// <summary>Opens the popup, against the trigger with this id if given.</summary>
    public Task OpenAsync(string? triggerId = null)
    {
        if (Root is null)
        {
            return Task.CompletedTask;
        }
        if (triggerId is not null)
        {
            Root.Activate(triggerId);
        }
        return Root.SetOpenAsync(true);
    }

    public Task CloseAsync() => Root?.SetOpenAsync(false) ?? Task.CompletedTask;
}
