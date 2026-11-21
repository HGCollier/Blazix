// Rider and ReSharper recognize their annotations by name, so this one is defined here instead of
// adding the JetBrains.Annotations package.
namespace JetBrains.Annotations;

/// <summary>
/// Public API: members apps and the registry use, which inspections of this solution can't see, so
/// they're not reported as never used or as able to be made private.
/// </summary>
#pragma warning disable S101 // The name Rider and ReSharper look for.
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Method | AttributeTargets.Property | AttributeTargets.Constructor, Inherited = false)]
// ReSharper disable once InconsistentNaming
internal sealed class PublicAPIAttribute : Attribute;
#pragma warning restore S101
