using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Numerics;
using UnityEngine;
using UnityEngine.Networking;

[DisallowMultipleComponent]
public sealed class AlchemyNftDiscoveryService :
    MonoBehaviour,
    ITokenDiscoveryService
{
    private const string PolygonAmoyChain = "eip155:80002";
    private const string PolygonAmoyEndpoint =
        "https://polygon-amoy.g.alchemy.com/nft/v3/getNFTsForOwner";

    [Header("Local development credentials")]
    [Tooltip(
        "Name of the environment variable containing the Alchemy API key. " +
        "The key itself must not be stored in this scene or repository."
    )]
    [SerializeField] private string apiKeyEnvironmentVariable =
        "PDT_ALCHEMY_API_KEY";

    [Header("Request")]
    [Range(1, 100)]
    [SerializeField] private int pageSize = 100;

    public IEnumerator DiscoverOwnedTokens(
        string ownerAddress,
        string chain,
        string collection,
        Action<IReadOnlyList<TokenReference>> onDiscovered,
        Action<string> onError
    )
    {
        if (string.IsNullOrWhiteSpace(ownerAddress))
        {
            onError?.Invoke("Alchemy discovery requires a wallet address.");
            yield break;
        }

        if (
            !string.Equals(
                chain?.Trim(),
                PolygonAmoyChain,
                StringComparison.OrdinalIgnoreCase
            )
        )
        {
            onError?.Invoke(
                $"Alchemy discovery does not support chain '{chain}'."
            );
            yield break;
        }

        if (string.IsNullOrWhiteSpace(collection))
        {
            onError?.Invoke("Alchemy discovery requires a collection address.");
            yield break;
        }

        if (string.IsNullOrWhiteSpace(apiKeyEnvironmentVariable))
        {
            onError?.Invoke(
                "Alchemy discovery has no API key environment variable name."
            );
            yield break;
        }

        string apiKey = Environment.GetEnvironmentVariable(
            apiKeyEnvironmentVariable.Trim()
        );

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            onError?.Invoke(
                $"Set the {apiKeyEnvironmentVariable.Trim()} environment " +
                "variable before starting indexed NFT discovery."
            );
            yield break;
        }

        string normalizedChain = PolygonAmoyChain;
        string normalizedCollection = collection.Trim().ToLowerInvariant();
        List<TokenReference> discoveredTokens =
            new List<TokenReference>();
        HashSet<TokenReference> uniqueTokens =
            new HashSet<TokenReference>();
        HashSet<string> visitedPageKeys =
            new HashSet<string>(StringComparer.Ordinal);
        string pageKey = null;

        do
        {
            string requestURL = BuildRequestURL(
                ownerAddress.Trim(),
                normalizedCollection,
                pageKey
            );

            using (UnityWebRequest request = UnityWebRequest.Get(requestURL))
            {
                request.SetRequestHeader(
                    "Authorization",
                    $"Bearer {apiKey.Trim()}"
                );
                request.SetRequestHeader("Accept", "application/json");

                yield return request.SendWebRequest();

                if (request.result != UnityWebRequest.Result.Success)
                {
                    onError?.Invoke(
                        "Alchemy NFT discovery failed with HTTP status " +
                        $"{request.responseCode}: {request.error}"
                    );
                    yield break;
                }

                if (
                    !TryParseResponse(
                        request.downloadHandler.text,
                        out AlchemyOwnedNftsResponse response,
                        out string responseError
                    )
                )
                {
                    onError?.Invoke(responseError);
                    yield break;
                }

                if (response == null || response.ownedNfts == null)
                {
                    onError?.Invoke(
                        "Alchemy returned an invalid NFT ownership response."
                    );
                    yield break;
                }

                foreach (AlchemyOwnedNft ownedNft in response.ownedNfts)
                {
                    if (ownedNft == null)
                    {
                        continue;
                    }

                    string responseCollection = ownedNft.contract?.address;

                    if (string.IsNullOrWhiteSpace(responseCollection))
                    {
                        responseCollection = ownedNft.contractAddress;
                    }

                    if (
                        !string.Equals(
                            responseCollection?.Trim(),
                            normalizedCollection,
                            StringComparison.OrdinalIgnoreCase
                        )
                    )
                    {
                        continue;
                    }

                    if (
                        !TryNormalizeEvmTokenID(
                            ownedNft.tokenId,
                            out string normalizedTokenID
                        )
                    )
                    {
                        onError?.Invoke(
                            "Alchemy returned an invalid token ID for the " +
                            "approved PDT collection."
                        );
                        yield break;
                    }

                    TokenReference tokenReference = new TokenReference(
                        normalizedChain,
                        normalizedCollection,
                        normalizedTokenID
                    );

                    if (uniqueTokens.Add(tokenReference))
                    {
                        discoveredTokens.Add(tokenReference);
                    }
                }

                pageKey = string.IsNullOrWhiteSpace(response.pageKey)
                    ? null
                    : response.pageKey.Trim();
            }

            if (pageKey != null && !visitedPageKeys.Add(pageKey))
            {
                onError?.Invoke(
                    "Alchemy returned a repeated pagination key."
                );
                yield break;
            }
        }
        while (pageKey != null);

        onDiscovered?.Invoke(discoveredTokens);
    }

    private string BuildRequestURL(
        string ownerAddress,
        string collection,
        string pageKey
    )
    {
        string requestURL =
            PolygonAmoyEndpoint +
            "?owner=" + UnityWebRequest.EscapeURL(ownerAddress) +
            "&contractAddresses%5B%5D=" +
            UnityWebRequest.EscapeURL(collection) +
            "&withMetadata=false" +
            "&pageSize=" + Mathf.Clamp(pageSize, 1, 100);

        if (!string.IsNullOrWhiteSpace(pageKey))
        {
            requestURL +=
                "&pageKey=" + UnityWebRequest.EscapeURL(pageKey.Trim());
        }

        return requestURL;
    }

    private static bool TryNormalizeEvmTokenID(
        string tokenID,
        out string normalizedTokenID
    )
    {
        normalizedTokenID = null;

        if (string.IsNullOrWhiteSpace(tokenID))
        {
            return false;
        }

        string value = tokenID.Trim();
        BigInteger parsedTokenID;

        if (value.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
        {
            string hexadecimalValue = value.Substring(2);

            if (
                hexadecimalValue.Length == 0 ||
                !BigInteger.TryParse(
                    "0" + hexadecimalValue,
                    NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture,
                    out parsedTokenID
                )
            )
            {
                return false;
            }
        }
        else if (
            !BigInteger.TryParse(
                value,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out parsedTokenID
            )
        )
        {
            return false;
        }

        if (parsedTokenID < BigInteger.Zero)
        {
            return false;
        }

        normalizedTokenID = parsedTokenID.ToString(
            CultureInfo.InvariantCulture
        );
        return true;
    }

    private static bool TryParseResponse(
        string responseJSON,
        out AlchemyOwnedNftsResponse response,
        out string errorMessage
    )
    {
        try
        {
            response = JsonUtility.FromJson<AlchemyOwnedNftsResponse>(
                responseJSON
            );
            errorMessage = null;
            return true;
        }
        catch (Exception exception)
        {
            response = null;
            errorMessage =
                "Alchemy returned invalid JSON: " + exception.Message;
            return false;
        }
    }

    [Serializable]
    private sealed class AlchemyOwnedNftsResponse
    {
        public AlchemyOwnedNft[] ownedNfts;
        public string pageKey;
    }

    [Serializable]
    private sealed class AlchemyOwnedNft
    {
        public AlchemyContract contract;
        public string contractAddress;
        public string tokenId;
    }

    [Serializable]
    private sealed class AlchemyContract
    {
        public string address;
    }
}
