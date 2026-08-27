using System;
using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Reown.AppKit.Unity;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class ReownEntitlementKeyService :
    MonoBehaviour,
    ITokenEntitlementService
{
    private const string EntitlementKeyOfABI =
        "function entitlementKeyOf(uint256 tokenId) view returns (bytes32)";

    [Header("Future entitlement contract")]
    [Tooltip("CAIP-2 chain identifier approved by this game build.")]
    [SerializeField] private string approvedChain = "eip155:80002";
    [Tooltip(
        "Future collection implementing entitlementKeyOf. Leave this " +
        "component unwired until that contract is deployed and approved."
    )]
    [SerializeField] private string approvedCollection;

    public IEnumerator ResolveVerifiedTokenEntitlement(
        TokenReference verifiedToken,
        Action<TokenEntitlement> onResolved,
        Action<string> onError
    )
    {
        if (!IsApprovedToken(verifiedToken))
        {
            onError?.Invoke(
                "The verified token is outside the approved entitlement " +
                "chain or collection."
            );
            yield break;
        }

        if (
            !BigInteger.TryParse(
                verifiedToken.TokenID,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out BigInteger tokenID
            ) ||
            tokenID < BigInteger.Zero
        )
        {
            onError?.Invoke("The verified token ID is invalid.");
            yield break;
        }

        if (
            !TryStartTask(
                () => AppKit.Evm.ReadContractAsync<byte[]>(
                    approvedCollection.Trim(),
                    EntitlementKeyOfABI,
                    "entitlementKeyOf",
                    new object[] { tokenID }
                ),
                out Task<byte[]> entitlementTask,
                out string entitlementStartError
            )
        )
        {
            onError?.Invoke(
                "On-chain entitlementKeyOf lookup failed: " +
                entitlementStartError
            );
            yield break;
        }

        while (!entitlementTask.IsCompleted)
        {
            yield return null;
        }

        if (
            !TryGetTaskResult(
                entitlementTask,
                out byte[] encodedEntitlementKey,
                out string entitlementError
            )
        )
        {
            onError?.Invoke(
                "On-chain entitlementKeyOf lookup failed: " +
                entitlementError
            );
            yield break;
        }

        if (
            !TryDecodeBytes32(
                encodedEntitlementKey,
                out string entitlementKey
            )
        )
        {
            onError?.Invoke(
                "The contract returned an invalid canonical entitlement key."
            );
            yield break;
        }

        TokenEntitlement entitlement = new TokenEntitlement(
            verifiedToken,
            entitlementKey
        );

        onResolved?.Invoke(entitlement);

        Debug.Log(
            $"Resolved on-chain entitlement {entitlement.EntitlementKey} " +
            $"for token {verifiedToken.TokenID}."
        );
    }

    private bool IsApprovedToken(TokenReference token)
    {
        return
            token != null &&
            !string.IsNullOrWhiteSpace(approvedChain) &&
            !string.IsNullOrWhiteSpace(approvedCollection) &&
            string.Equals(
                token.Chain,
                approvedChain.Trim(),
                StringComparison.OrdinalIgnoreCase
            ) &&
            string.Equals(
                token.Collection,
                approvedCollection.Trim(),
                StringComparison.OrdinalIgnoreCase
            );
    }

    private static bool TryDecodeBytes32(
        byte[] encodedValue,
        out string entitlementKey
    )
    {
        entitlementKey = null;

        if (encodedValue == null || encodedValue.Length != 32)
        {
            return false;
        }

        int valueLength = Array.IndexOf(encodedValue, (byte)0);

        if (valueLength < 0)
        {
            valueLength = encodedValue.Length;
        }

        for (int index = valueLength; index < encodedValue.Length; index++)
        {
            if (encodedValue[index] != 0)
            {
                return false;
            }
        }

        string decodedValue = Encoding.ASCII.GetString(
            encodedValue,
            0,
            valueLength
        );

        return EntitlementKeys.TryNormalize(
            decodedValue,
            out entitlementKey
        );
    }

    private static bool TryGetTaskResult<T>(
        Task<T> task,
        out T result,
        out string errorMessage
    )
    {
        if (task.IsCanceled)
        {
            result = default;
            errorMessage = "The blockchain request was cancelled.";
            return false;
        }

        if (task.IsFaulted)
        {
            result = default;
            errorMessage = task.Exception
                ?.GetBaseException()
                .Message ?? "The blockchain request failed.";
            return false;
        }

        result = task.Result;
        errorMessage = null;
        return true;
    }

    private static bool TryStartTask<T>(
        Func<Task<T>> taskFactory,
        out Task<T> task,
        out string errorMessage
    )
    {
        try
        {
            task = taskFactory();
            errorMessage = null;
            return true;
        }
        catch (Exception exception)
        {
            task = null;
            errorMessage = exception.Message;
            return false;
        }
    }
}
