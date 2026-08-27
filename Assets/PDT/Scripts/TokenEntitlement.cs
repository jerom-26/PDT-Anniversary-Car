using System;

[Serializable]
public sealed class TokenEntitlement
{
    public TokenReference TokenReference { get; }
    public string EntitlementKey { get; }

    public TokenEntitlement(
        TokenReference tokenReference,
        string entitlementKey
    )
    {
        TokenReference = tokenReference ?? throw new ArgumentNullException(
            nameof(tokenReference)
        );

        if (
            !EntitlementKeys.TryNormalize(
                entitlementKey,
                out string normalizedEntitlementKey
            )
        )
        {
            throw new ArgumentException(
                "A canonical bytes32-compatible entitlement key is required.",
                nameof(entitlementKey)
            );
        }

        EntitlementKey = normalizedEntitlementKey;
    }

    public override string ToString()
    {
        return $"{TokenReference} => {EntitlementKey}";
    }
}
