using System.Text.RegularExpressions;

namespace Blazix.Docs.Examples;

/// <summary>A chat message: "user" or "assistant".</summary>
public sealed record ChatMessage(string Id, string Role, string Text)
{
    public bool IsUser => Role == "user";
}

/// <summary>
/// A scripted conversation for the message scroller examples:
/// chain turns, read them back with <see cref="Get"/>, or play them with a <see cref="ChatSession"/>.
/// </summary>
public sealed partial class ScriptedChat
{
    private readonly List<ChatMessage> _messages = [];

    /// <summary>The conversation the demo, streaming and previous-context examples share.</summary>
    public static ScriptedChat ScrollBehavior { get; } = new ScriptedChat()
        .User("I'm building a chat for our app and the scroll behavior is driving me nuts. Every time the AI streams a reply, the whole thread jumps around.")
        .Assistant("That's the classic streaming scroll problem. Wrap your message list in `MessageScroller` and turn on `autoScroll` — the viewport pins to the bottom as tokens arrive, so users always see the latest text land in place.\n\nThe important part: it only auto-scrolls while the reader is already at the bottom. The moment they scroll up to read something earlier, auto-scroll backs off and their position is preserved. You get smooth streaming without fighting the user's intent.")
        .User("Okay, but when someone sends a new message the view still feels jarring — like the whole conversation reloads from the top.")
        .Assistant("MessageScrollerItem fixes that with turn anchoring. Set `scrollAnchor` on the turn that should settle near the top instead of blindly snapping to the document bottom.\n\nIt also leaves a small peek of the previous exchange visible above the anchor, so context isn't lost. The reply starts in view without that disorienting jump you get from a plain overflow container.")
        .User("And if they've scrolled up to re-read an older answer? I don't want to yank them back down.")
        .Assistant("You won't. Auto-scroll only runs when the viewport is already pinned to the bottom, so scrolling up is a deliberate opt-out — their place in the thread stays put even as new tokens keep arriving below.\n\nWhen there is content they haven't seen yet, `MessageScrollerButton` appears at the bottom of the viewport. One tap jumps them back to the newest message and re-engages auto-scroll. Same pattern as Slack or iMessage: quiet when you're caught up, helpful when you're not.")
        .User("Last one — does this work with assistive tech?")
        .Assistant("`MessageScrollerContent` sets `role=\"log\"` and `aria-relevant=\"additions\"` by default, so screen readers announce new messages as they stream in.\n\nThe scroll button is a real `<button>` with an sr-only label, and it's removed from the tab order when you're already at the bottom — no ghost focus stops.");

    public ScriptedChat User(string text, string? id = null) => Add("user", text, id);

    public ScriptedChat Assistant(string text) => Add("assistant", text, null);

    /// <summary>The first <paramref name="count"/> messages, or all of them.</summary>
    public IReadOnlyList<ChatMessage> Get(int? count = null) => _messages[..(count ?? _messages.Count)];

    /// <summary>The scripted message after <paramref name="messages"/>, if it's the user's.</summary>
    public ChatMessage? Next(IReadOnlyList<ChatMessage> messages) =>
        _messages.ElementAtOrDefault(messages.Count) is { IsUser: true } next ? next : null;

    internal ChatMessage? At(int index) => _messages.ElementAtOrDefault(index);

    /// <summary>The paragraphs of a message, as the examples render them.</summary>
    public static IEnumerable<string> Paragraphs(string text) =>
        ParagraphBreak().Split(text).Select(p => p.Trim()).Where(p => p.Length > 0);

    [GeneratedRegex(@"\n\s*\n")]
    private static partial Regex ParagraphBreak();

    private ScriptedChat Add(string role, string text, string? id)
    {
        _messages.Add(new ChatMessage(id ?? $"msg-{_messages.Count + 1}", role, text));
        return this;
    }
}

/// <summary>
/// Plays a <see cref="ScriptedChat"/> like a chat with a scripted transport: sending appends the user
/// message, waits a second, then streams the assistant's reply word by word.
/// </summary>
public sealed partial class ChatSession(ScriptedChat chat, int initialCount, int delayMs)
{
    public IReadOnlyList<ChatMessage> Messages { get; private set; } = chat.Get(initialCount);

    /// <summary>"ready", "submitted" or "streaming", like <c>useChat</c>'s status.</summary>
    public string Status { get; private set; } = "ready";

    public bool IsBusy => Status != "ready";

    public ChatMessage? NextMessage => chat.Next(Messages);

    public void Reset() => Messages = chat.Get(initialCount);

    /// <summary>Sends the next user message and streams the reply, calling <paramref name="changed"/> after each step.</summary>
    public async Task SendAsync(Func<Task> changed)
    {
        if (NextMessage is not { } user || IsBusy)
        {
            return;
        }
        Messages = [.. Messages, user];
        Status = "submitted";
        await changed();
        await Task.Delay(1000);
        if (chat.At(Messages.Count) is { IsUser: false } reply)
        {
            Status = "streaming";
            foreach (Match word in Word().Matches(reply.Text))
            {
                Messages = [.. Messages.Where(m => m.Id != reply.Id), reply with { Text = reply.Text[..(word.Index + word.Length)] }];
                await changed();
                await Task.Delay(delayMs);
            }
        }
        Status = "ready";
        await changed();
    }

    [GeneratedRegex(@"\S+\s*")]
    private static partial Regex Word();
}
