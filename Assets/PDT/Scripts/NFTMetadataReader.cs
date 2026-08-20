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

            NFTMetadata metadata = JsonUtility.FromJson<NFTMetadata>(request.downloadHandler.text);

            if (metadata == null || metadata.attributes == null)
            {
                onError?.Invoke("Downloaded NFT metadata is invalid.");
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
}
