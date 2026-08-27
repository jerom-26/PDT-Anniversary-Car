using System;

[Serializable]
public sealed class TokenReference : IEquatable<TokenReference>
{
    public string Chain { get; }
    public string Collection { get; }
    public string AssetID { get; }

    public TokenReference(string chain, string collection, string assetID)
    {
        if (string.IsNullOrWhiteSpace(chain))
        {
            throw new ArgumentException("A token chain is required.", nameof(chain));
        }

        if (string.IsNullOrWhiteSpace(collection))
        {
            throw new ArgumentException(
                "A token collection is required.",
                nameof(collection)
            );
        }

        if (string.IsNullOrWhiteSpace(assetID))
        {
            throw new ArgumentException(
                "A token asset ID is required.",
                nameof(assetID)
            );
        }

        Chain = chain.Trim();
        Collection = collection.Trim();
        AssetID = assetID.Trim();
    }

    public bool Equals(TokenReference other)
    {
        return
            other != null &&
            string.Equals(Chain, other.Chain, StringComparison.Ordinal) &&
            string.Equals(
                Collection,
                other.Collection,
                StringComparison.Ordinal
            ) &&
            string.Equals(AssetID, other.AssetID, StringComparison.Ordinal);
    }

    public override bool Equals(object obj)
    {
        return Equals(obj as TokenReference);
    }

    public override int GetHashCode()
    {
        unchecked
        {
            int hash = 17;
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(Chain);
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(Collection);
            hash = hash * 31 + StringComparer.Ordinal.GetHashCode(AssetID);
            return hash;
        }
    }

    public override string ToString()
    {
        return $"{Chain}/{Collection}/{AssetID}";
    }
}
