using System;
using System.Collections;
using UnityEngine;
using UnityEngine.Networking;

public class NFTMetadataReader : MonoBehaviour
{
    [SerializeField] private string ipfsGateway = "https://gateway.pinata.cloud/ipfs/";
    [SerializeField] private int requestTimeoutSeconds = 30;

    public IEnumerator LoadMetadata(
        string metadataURI,
        Action<NFTMetadata> onLoaded,
        Action<string> onError
    )
    {
        if (string.IsNullOrWhiteSpace(metadataURI))
        {
            onError?.Invoke("The NFT metadata URI is empty.");
            yield break;
        }

        if (!TryConvertToGatewayURL(metadataURI, out string metadataURL))
        {
            onError?.Invoke("The NFT metadata URI uses an unsupported or invalid scheme.");
            yield break;
        }

        using (UnityWebRequest request = UnityWebRequest.Get(metadataURL))
        {
            request.timeout = requestTimeoutSeconds > 0
                ? requestTimeoutSeconds
                : 30;
            request.SetRequestHeader("Accept", "application/json");
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(
                    $"Metadata download failed with HTTP status " +
                    $"{request.responseCode}: {request.error}"
                );
                yield break;
            }

            if (
                !TryParseMetadata(
                    request.downloadHandler.text,
                    out NFTMetadata metadata,
                    out string parseError
                )
            )
            {
                onError?.Invoke(parseError);
                yield break;
            }

            onLoaded?.Invoke(metadata);
        }
    }

    private bool TryConvertToGatewayURL(string uri, out string url)
    {
        const string ipfsPrefix = "ipfs://";
        url = null;
        uri = uri.Trim();

        if (string.IsNullOrWhiteSpace(ipfsGateway))
        {
            return false;
        }

        string gateway = ipfsGateway.Trim();
        if (!gateway.EndsWith("/", StringComparison.Ordinal))
        {
            gateway += "/";
        }

        if (uri.StartsWith(ipfsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            string path = uri.Substring(ipfsPrefix.Length).TrimStart('/');
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            url = gateway + path;
            return IsHttpURL(url);
        }

        if (IsLikelyIPFSCID(uri))
        {
            url = gateway + uri;
            return IsHttpURL(url);
        }

        if (!IsHttpURL(uri))
        {
            return false;
        }

        url = uri;
        return true;
    }

    private static bool IsHttpURL(string value)
    {
        return Uri.TryCreate(value, UriKind.Absolute, out Uri parsed) &&
            (parsed.Scheme == Uri.UriSchemeHttps || parsed.Scheme == Uri.UriSchemeHttp);
    }

    private static bool IsLikelyIPFSCID(string value)
    {
        bool isCIDv0 =
            value.Length == 46 &&
            value.StartsWith("Qm", StringComparison.Ordinal);
        bool isCIDv1 =
            value.Length > 4 &&
            value.StartsWith("baf", StringComparison.OrdinalIgnoreCase);

        return isCIDv0 || isCIDv1;
    }

    private static bool TryParseMetadata(
        string metadataJSON,
        out NFTMetadata metadata,
        out string errorMessage
    )
    {
        if (string.IsNullOrWhiteSpace(metadataJSON))
        {
            metadata = null;
            errorMessage = "Downloaded NFT metadata is empty.";
            return false;
        }

        try
        {
            metadata = JsonUtility.FromJson<NFTMetadata>(metadataJSON);
        }
        catch (Exception exception)
        {
            metadata = null;
            errorMessage =
                "Downloaded NFT metadata is invalid: " + exception.Message;
            return false;
        }

        if (metadata == null || metadata.attributes == null)
        {
            errorMessage = "Downloaded NFT metadata is invalid.";
            return false;
        }

        errorMessage = null;
        return true;
    }
}
