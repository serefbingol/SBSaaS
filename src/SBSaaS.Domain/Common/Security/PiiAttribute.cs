namespace SBSaaS.Domain.Common.Security;

[AttributeUsage(AttributeTargets.Property)]
public sealed class PiiAttribute : Attribute
{
    public string? Mask { get; init; } // örn: ****@domain.com
}