using System;
using System.Globalization;
using System.Numerics;

// Pure validation policy shared by the Unity readers and standalone tests.
public static class PDTVerificationPolicy
{
    public static bool IsAddress(string address)
    {
        if (address == null || address.Length != 42 || !address.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) return false;
        bool nonzero = false;
        for (int i = 2; i < address.Length; i++)
        {
            char c = address[i];
            if (!Uri.IsHexDigit(c)) return false;
            nonzero |= c != '0';
        }
        return nonzero;
    }

    public static bool TryTokenId(string value, out BigInteger id) =>
        BigInteger.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out id) &&
        id >= 0 && id < (BigInteger.One << 256);

    public static bool IsNonexistentToken(string revertData, BigInteger tokenId)
    {
        // Only the approved contract's exact ERC721NonexistentToken(uint256)
        // error for this token proves absence. Arbitrary RPC errors do not.
        return revertData != null && revertData.Length == 74 &&
            revertData.StartsWith("0x7e273289", StringComparison.OrdinalIgnoreCase) &&
            BigInteger.TryParse("0" + revertData.Substring(10), NumberStyles.AllowHexSpecifier,
                CultureInfo.InvariantCulture, out BigInteger rejectedId) && rejectedId == tokenId;
    }
}
