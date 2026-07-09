using UnityEngine;

public class VehicleSpawner : MonoBehaviour
{
    [SerializeField] private VehicleData vehicleData;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private CameraFollow cameraFollow;
    [SerializeField] private NFTMetadataReader metadataReader;

    private void Start()
    {
        if (metadataReader == null)
        {
            metadataReader = GetComponent<NFTMetadataReader>();
        }

        if (metadataReader == null)
        {
            Debug.LogError("VehicleSpawner has no NFTMetadataReader assigned.");
            return;
        }
        StartCoroutine(
           metadataReader.LoadMetadata(
               OnMetadataLoaded,
               OnMetadataLoadFailed
           )
       );
    }

    private void OnMetadataLoaded(NFTMetadata metadata)
    {
        string ownedAssetID = GetAssetIDFromMetadata(metadata);

        if (string.IsNullOrEmpty(ownedAssetID))
        {
            return;
        }

        TrySpawnVehicle(ownedAssetID);
    }

    private void OnMetadataLoadFailed(string errorMessage)
    {
        Debug.LogError(errorMessage);
    }

    private string GetAssetIDFromMetadata(NFTMetadata metadata)
    {
        if (metadata == null || metadata.attributes == null)
        {
            Debug.LogError(
                "NFT metadata is invalid or does not contain attributes."
            );

            return null;
        }

        foreach(NFTAttribute attribute in metadata.attributes)
        {
            if(attribute != null && attribute.trait_type == "Asset ID")
            {
                return attribute.value;
            }
        }
        return null;
    }
    private void TrySpawnVehicle(string ownedAssetID)
    {
        GameObject spawnedVehicle = Instantiate(vehicleData.VehiclePrefab, spawnPoint.position, spawnPoint.rotation);

        Transform cameraTarget = spawnedVehicle.transform.Find("CameraTarget");

        cameraFollow.SetTarget(cameraTarget);
        Debug.Log(
            $"Ownership confirmed from remote NFT metadata. " +
            $"Spawned vehicle: {vehicleData.DisplayName}"
        );
    }
   
}