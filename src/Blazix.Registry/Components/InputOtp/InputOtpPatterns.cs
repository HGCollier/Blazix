namespace Blazix.Registry;

/// <summary>Patterns for <see cref="InputOtp.Pattern"/>, as the input-otp library exports them.</summary>
public static class InputOtpPatterns
{
    /// <summary>Digits only (input-otp's <c>REGEXP_ONLY_DIGITS</c>).</summary>
    public const string OnlyDigits = @"^\d+$";

    /// <summary>Letters only (input-otp's <c>REGEXP_ONLY_CHARS</c>).</summary>
    public const string OnlyChars = "^[a-zA-Z]+$";

    /// <summary>Digits and letters (input-otp's <c>REGEXP_ONLY_DIGITS_AND_CHARS</c>).</summary>
    public const string OnlyDigitsAndChars = "^[a-zA-Z0-9]+$";
}
