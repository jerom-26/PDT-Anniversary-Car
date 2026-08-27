using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;

public class NFTMetadataReader : MonoBehaviour
{
    [SerializeField] private string ipfsGateway = "https://gateway.pinata.cloud/ipfs/";

    public IEnumerator LoadMetadata(
        string metadataURI,
        Action<NFTMetadata> onLoaded,
        Action<string> onError
    )
    {
        if (string.IsNullOrWhiteSpace(metadataURI))
        {
            onError?.Invoke("The NFT Metadata is empty.");
            yield break;
        }

        string metadataURL = ConvertToGatewayURL(metadataURI);
        using (UnityWebRequest request = UnityWebRequest.Get(metadataURL))
        {
            yield return request.SendWebRequest();

            if (request.result != UnityWebRequest.Result.Success)
            {
                onError?.Invoke(
                    $"Metadata download failed: {request.error}"
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

    private string ConvertToGatewayURL(string uri)
    {
        const string ipfsPrefix = "ipfs://";

        uri = uri.Trim();

        string gateway = ipfsGateway.EndsWith("/")
            ? ipfsGateway
            : ipfsGateway + "/";

        if (uri.StartsWith(ipfsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return gateway + uri.Substring(ipfsPrefix.Length).TrimStart('/');
        }

        if (IsLikelyIPFSCID(uri))
        {
            return gateway + uri;
        }

        return uri;
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
