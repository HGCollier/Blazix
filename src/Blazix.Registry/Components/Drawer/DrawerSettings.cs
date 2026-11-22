namespace Blazix.Registry;

/// <summary>What a Drawer tells its DrawerContent.</summary>
public sealed record DrawerSettings(bool HasSnapPoints, bool Modal, bool ShowSwipeHandle, DrawerSwipeDirection SwipeDirection);
