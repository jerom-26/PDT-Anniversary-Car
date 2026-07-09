using UnityEngine;
using UnityEngine.Networking;
using System;
using System.Collections;
public class NFTMetadataReader : MonoBehaviour
{
    [SerializeField] private string metadataURI = "ipfs://bafkreihjyr3xtxoc32t6roxu3ahhs47x3knschdf7bjrnsnke6a7du3otq";

    [SerializeField] private string ipfsGateway = "https://ipfs.io/ipfs/";

    public IEnumerator LoadMetadata(Action<NFTMetadata> onLoaded, Action<string> onError)
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

        if (uri.StartsWith(ipfsPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return ipfsGateway + uri.Substring(ipfsPrefix.Length);
        }

        return uri;
    }

}
