namespace Blazix.Cli;

/// <summary>An expected failure. Its message is shown to the user as-is, without a stack trace.</summary>
public sealed class CliException(string message) : Exception(message);
