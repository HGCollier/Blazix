namespace Blazix.Primitives;

/// <summary>
/// A dialog that requires a user response to proceed: always
/// modal, <c>role="alertdialog"</c>, and pressing outside doesn't close it.
/// </summary>
public class AlertDialogRoot() : DialogRoot("alert-dialog")
{
    internal override string Role => "alertdialog";

    internal override PopupModal EffectiveModal => PopupModal.True;

    private protected override bool PointerDismissible => false;
}

#pragma warning disable S2094 // Aliases: the AlertDialog parts are Dialog's parts under the AlertDialog name.
/// <summary>A button that opens the alert dialog.</summary>
public class AlertDialogTrigger : DialogTrigger;

/// <summary>An overlay beneath the alert dialog.</summary>
public class AlertDialogBackdrop : DialogBackdrop;

/// <summary>A container for the alert dialog's popup.</summary>
public class AlertDialogViewport : DialogViewport;

/// <summary>The alert dialog itself.</summary>
public class AlertDialogPopup : DialogPopup;

/// <summary>A heading that labels the alert dialog.</summary>
public class AlertDialogTitle : DialogTitle;

/// <summary>A paragraph describing the alert dialog.</summary>
public class AlertDialogDescription : DialogDescription;

/// <summary>A button that closes the alert dialog.</summary>
public class AlertDialogClose : DialogClose;
/// <summary>A portal for the popup.</summary>
public class AlertDialogPortal : DialogPortal;
#pragma warning restore S2094

/// <summary>
/// Connects triggers outside the <see cref="AlertDialogRoot"/> to it:
/// create one, pass it to the root's and the triggers' <c>Handle</c>,
/// and open it from code with <see cref="PopupHandle.OpenAsync"/>.
/// </summary>
#pragma warning disable S2094 // A typed name for the shared handle.
public class AlertDialogHandle : PopupHandle;
#pragma warning restore S2094
