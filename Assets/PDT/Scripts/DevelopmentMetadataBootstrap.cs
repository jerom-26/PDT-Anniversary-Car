using UnityEngine;

public class DevelopmentMetadataBootstrap : MonoBehaviour
{
    [Header("Development only - replace with the verified wallet flow")]
    [SerializeField] private bool runOnStart = true;
    [SerializeField] private string developmentMetadataURI;
    [SerializeField] private bool spawnUnlockedVehicle = true;

    [Header("V2 components")]
    [SerializeField] private NFTMetadataReader metadataReader;
    [SerializeField] private OwnedVehicleRegistry ownedVehicleRegistry;
    [SerializeField] private VehicleSpawner vehicleSpawner;

    private void Start()
    {
        if (!runOnStart)
        {
            return;
        }

        LoadDevelopmentMetadata();
    }

    public void LoadDevelopmentMetadata()
    {
        if (
            metadataReader == null ||
            ownedVehicleRegistry == null ||
            (spawnUnlockedVehicle && vehicleSpawner == null)
        )
        {
            Debug.LogError(
                "DevelopmentMetadataBootstrap is missing a V2 component reference."
            );
            return;
        }

        Debug.LogWarning(
            "Development metadata bootstrap bypasses wallet and ownerOf verification."
        );

        StartCoroutine(
            metadataReader.LoadMetadata(
                developmentMetadataURI,
                OnMetadataLoaded,
                OnMetadataLoadFailed
            )
        );
    }

    private void OnMetadataLoaded(NFTMetadata metadata)
    {
        if (
            ownedVehicleRegistry.TryRegisterVerifiedMetadata(
                metadata,
                out VehicleData vehicleData
            ) &&
            spawnUnlockedVehicle
        )
        {
            vehicleSpawner.TrySpawn(vehicleData);
        }
    }

    private void OnMetadataLoadFailed(string errorMessage)
    {
        Debug.LogError(errorMessage);
    }
}
