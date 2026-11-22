namespace Blazix.Registry;

/// <summary>A trace line's status: none, running (shimmering), failed, or not started.</summary>
public enum TraceLineStatus
{
    None,
    Running,
    Error,
    Pending,
}
