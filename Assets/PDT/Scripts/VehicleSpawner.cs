using GLTFast.Schema;
using Unity.VisualScripting;
using UnityEngine;

public class VehicleSpawner : MonoBehaviour
{
    [SerializeField] private TextAsset nftMetaDataFile;
    [SerializeField] private VehicleData vehicleData;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private CameraFollow cameraFollow;

    void Start()
    {
        SpawnVehicle();
    }

    private void SpawnVehicle()
    {
        string ownedAssetID = ReadAssetIDFromMetadata();

        GameObject spawnedVehicle = Instantiate(
            vehicleData.VehiclePrefab,
            spawnPoint.position,
            spawnPoint.rotation
        );

        Transform cameraTarget =
            spawnedVehicle.transform.Find("CameraTarget");

        cameraFollow.SetTarget(cameraTarget);
    }

    private string ReadAssetIDFromMetadata()
    {
        NFTMetadata metadata = JsonUtility.FromJson<NFTMetadata>(nftMetaDataFile.text);

        foreach (NFTAttribute attribute in metadata.attributes)
        { if (attribute != null && attribute.trait_types == "modelID")
            {
                return attribute.value;
            }
        }
        return null;
    }
}
