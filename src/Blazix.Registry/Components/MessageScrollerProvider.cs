namespace Blazix.Registry;

#pragma warning disable S2094 // The primitives under the styled names.

/// <summary>
/// Owns a message scroller's behavior and cascades it to the parts inside; renders no element. Keep a <c>@ref</c>
/// to it for <c>ScrollToStartAsync</c>, <c>ScrollToEndAsync</c> and <c>ScrollToMessageAsync</c>.
/// </summary>
public class MessageScrollerProvider : Blazix.Primitives.MessageScrollerProvider;

/// <summary>Renders its content with whether there's more to scroll toward each edge.</summary>
public class MessageScrollerScrollable : Blazix.Primitives.MessageScrollerScrollable;

/// <summary>Renders its content with the messages in view and the current anchored turn.</summary>
public class MessageScrollerVisibility : Blazix.Primitives.MessageScrollerVisibility;
#pragma warning restore S2094
