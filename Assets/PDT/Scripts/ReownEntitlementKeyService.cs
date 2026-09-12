using System;
using System.Collections;
using System.Numerics;
using System.Text;
using Reown.AppKit.Unity;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ReownEntitlementKeyService : MonoBehaviour, ITokenEntitlementService
{
    private const string KeyABI = "function entitlementKeyOf(uint256 tokenId) view returns (bytes32)";
    private const string OwnerABI = "function ownerOf(uint256 tokenId) view returns (address)";
    [SerializeField] private ApprovedPDTCollection approvedSource;
    [SerializeField] private ReownWalletConnector walletConnector;
    public bool LastVerificationUnavailable { get; private set; }

    public IEnumerator ResolveVerifiedTokenEntitlement(TokenReference token,
        Action<TokenEntitlement> onResolved, Action<string> onError)
    {
        LastVerificationUnavailable = false;

        if (walletConnector == null)
        {
            LastVerificationUnavailable = true;
            onError?.Invoke("Entitlement verification has no wallet context.");
            yield break;
        }

        if (approvedSource == null || !approvedSource.Contains(token) ||
            !PDTVerificationPolicy.TryTokenId(token.TokenID, out BigInteger id))
        { onError?.Invoke("Rejected token outside the approved PDT source."); yield break; }

        var context = new PDTReadContext(walletConnector, approvedSource.Chain);
        var key = new PDTReadResult<byte[]>();
        yield return PDTChainRead.Run(() => AppKit.Evm.ReadContractAsync<byte[]>(
            token.Collection, KeyABI, "entitlementKeyOf", new object[] { id }), context, key, id);
        if (key.Status != PDTReadStatus.Success)
        {
            LastVerificationUnavailable = key.Status != PDTReadStatus.Nonexistent;
            onError?.Invoke(LastVerificationUnavailable ? "Entitlement verification unavailable." : "Token no longer exists.");
            yield break;
        }
        if (!TryDecodeBytes32(key.Value, out string decoded))
        { onError?.Invoke("Rejected invalid PDT entitlement encoding."); yield break; }

        // Confirm ownership again after the entitlement lookup.
        var owner = new PDTReadResult<string>();
        yield return PDTChainRead.Run(() => AppKit.Evm.ReadContractAsync<string>(
            token.Collection, OwnerABI, "ownerOf", new object[] { id }), context, owner, id);
        if (!context.IsCurrent || !approvedSource.Contains(token)) yield break;
        if (owner.Status != PDTReadStatus.Success)
        {
            LastVerificationUnavailable = owner.Status != PDTReadStatus.Nonexistent;
            onError?.Invoke(LastVerificationUnavailable ? "Ownership revalidation unavailable." : "Token no longer exists.");
            yield break;
        }
        if (!PDTVerificationPolicy.IsAddress(owner.Value))
        {
            LastVerificationUnavailable = true;
            onError?.Invoke("Ownership revalidation returned an invalid address.");
            yield break;
        }

        if (!string.Equals(owner.Value, context.Address, StringComparison.OrdinalIgnoreCase))
        { onError?.Invoke("Token is no longer owned by this wallet."); yield break; }
        onResolved?.Invoke(new TokenEntitlement(token, decoded));
        Debug.Log($"Resolved direct PDT entitlement {decoded} for token {id}.");
    }

    public static bool TryDecodeBytes32(byte[] bytes, out string key)
    {
        key = null;
        if (bytes == null || bytes.Length != 32) return false;
        int length = Array.IndexOf(bytes, (byte)0);
        if (length < 0) length = 32;
        for (int i = length; i < 32; i++) if (bytes[i] != 0) return false;
        for (int i = 0; i < length; i++) if (bytes[i] > 127) return false;
        return EntitlementKeys.TryNormalize(Encoding.ASCII.GetString(bytes, 0, length), out key);
    }
}
