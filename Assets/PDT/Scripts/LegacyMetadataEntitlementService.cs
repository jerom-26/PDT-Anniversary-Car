using System;
using System.Collections;
using System.Globalization;
using System.Numerics;
using System.Threading.Tasks;
using Reown.AppKit.Unity;
using UnityEngine;

[DisallowMultipleComponent]
public sealed class LegacyMetadataEntitlementService :
    MonoBehaviour,
    ITokenEntitlementService
{
    private const string LegacyChain = "eip155:80002";
    private const string LegacyCollection =
        "0x021Ae9C7E520B1EdFdE488A7Df3EEd9BfC5786F3";
    private const string LegacyDreamMobileAssetID = "HW-001";
    private const string TokenURIABI =
        "function tokenURI(uint256 tokenId) view returns (string)";

    [Header("Legacy development fixtures only")]
    [Tooltip(
        "Reads metadata only for existing Amoy tokens 0 and 1, then maps " +
        "their legacy Asset ID to a canonical entitlement key."
    )]
    [SerializeField] private NFTMetadataReader metadataReader;

    public IEnumerator ResolveVerifiedTokenEntitlement(
        TokenReference verifiedToken,
        Action<TokenEntitlement> onResolved,
        Action<string> onError
    )
    {
        if (!IsApprovedLegacyFixture(verifiedToken))
        {
            onError?.Invoke(
                "The verified token is not one of the approved legacy " +
                "development fixtures (tokens 0 and 1)."
            );
            yield break;
        }

        if (metadataReader == null)
        {
            onError?.Invoke(
                "Legacy entitlement resolution has no metadata reader."
            );
            yield break;
        }

        if (
            !BigInteger.TryParse(
                verifiedToken.TokenID,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out BigInteger tokenID
            )
        )
        {
            onError?.Invoke("The legacy fixture token ID is invalid.");
            yield break;
        }

        if (
            !TryStartTask(
                () => AppKit.Evm.ReadContractAsync<string>(
                    LegacyCollection,
                    TokenURIABI,
                    "tokenURI",
                    new object[] { tokenID }
                ),
                out Task<string> tokenURITask,
                out string tokenURIStartError
            )
        )
        {
            onError?.Invoke(
                "Legacy fixture tokenURI lookup failed: " +
                tokenURIStartError
            );
            yield break;
        }

        while (!tokenURITask.IsCompleted)
        {
            yield return null;
        }

        if (
            !TryGetTaskResult(
                tokenURITask,
                out string metadataURI,
                out string tokenURIError
            )
        )
        {
            onError?.Invoke(
                "Legacy fixture tokenURI lookup failed: " + tokenURIError
            );
            yield break;
        }

        NFTMetadata metadata = null;
        string metadataError = null;

        yield return metadataReader.LoadMetadata(
            metadataURI,
            loadedMetadata => metadata = loadedMetadata,
            error => metadataError = error
        );

        if (!string.IsNullOrWhiteSpace(metadataError))
        {
            onError?.Invoke(
                "Legacy fixture metadata failed: " + metadataError
            );
            yield break;
        }

        if (!TryGetLegacyAssetID(metadata, out string legacyAssetID))
        {
            onError?.Invoke(
                "Legacy fixture metadata has no Asset ID or modelID."
            );
            yield break;
        }

        if (
            !string.Equals(
                legacyAssetID,
                LegacyDreamMobileAssetID,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            onError?.Invoke(
                $"Legacy Asset ID '{legacyAssetID}' has no approved " +
                "canonical entitlement mapping."
            );
            yield break;
        }

        TokenEntitlement entitlement = new TokenEntitlement(
            verifiedToken,
            EntitlementKeys.DreamMobile80th
        );

        onResolved?.Invoke(entitlement);

        Debug.Log(
            $"Legacy fixture token {verifiedToken.TokenID} mapped " +
            $"metadata Asset ID {LegacyDreamMobileAssetID} to " +
            $"{entitlement.EntitlementKey}."
        );
    }

    private static bool IsApprovedLegacyFixture(TokenReference token)
    {
        if (token == null)
        {
            return false;
        }

        bool isApprovedSource =
            string.Equals(
                token.Chain,
                LegacyChain,
                StringComparison.OrdinalIgnoreCase
            ) &&
            string.Equals(
                token.Collection,
                LegacyCollection,
                StringComparison.OrdinalIgnoreCase
            );
        bool isFixtureToken =
            string.Equals(token.TokenID, "0", StringComparison.Ordinal) ||
            string.Equals(token.TokenID, "1", StringComparison.Ordinal);

        return isApprovedSource && isFixtureToken;
    }

    private static bool TryGetLegacyAssetID(
        NFTMetadata metadata,
        out string assetID
    )
    {
        assetID = null;

        if (metadata == null || metadata.attributes == null)
        {
            return false;
        }

        foreach (NFTAttribute attribute in metadata.attributes)
        {
            if (attribute == null)
            {
                continue;
            }

            bool isAssetID = string.Equals(
                attribute.trait_type?.Trim(),
                "Asset ID",
                StringComparison.OrdinalIgnoreCase
            );
            bool isLegacyModelID = string.Equals(
                attribute.trait_type?.Trim(),
                "modelID",
                StringComparison.OrdinalIgnoreCase
            );

            if (
                (isAssetID || isLegacyModelID) &&
                !string.IsNullOrWhiteSpace(attribute.value)
            )
            {
                assetID = attribute.value.Trim();
                return true;
            }
        }

        return false;
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
