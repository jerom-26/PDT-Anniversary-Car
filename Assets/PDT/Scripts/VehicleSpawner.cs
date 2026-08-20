using UnityEngine;

public class VehicleSpawner : MonoBehaviour
{
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private CameraFollow cameraFollow;

    private GameObject spawnedVehicle;

    public GameObject SpawnedVehicle => spawnedVehicle;

    public bool TrySpawn(VehicleData vehicleData)
    {
        if (vehicleData == null)
        {
            Debug.LogError("Vehicle Data is not assigned.");
            return false;
        }

        if (vehicleData.VehiclePrefab == null)
        {
            Debug.LogError($"{vehicleData.DisplayName} has no vehicle prefab assigned.");
            return false;
        }

        if (spawnPoint == null || cameraFollow == null)
        {
            Debug.LogError("VehicleSpawner is missing its spawn point or camera follow reference.");
            return false;
        }

        GameObject nextVehicle = Instantiate(
            vehicleData.VehiclePrefab,
            spawnPoint.position,
            spawnPoint.rotation
        );

        Transform cameraTarget = FindCameraTarget(nextVehicle.transform);

        if (cameraTarget == null)
        {
            Debug.LogError(
                $"{vehicleData.DisplayName} does not contain a CameraTarget transform."
            );
            Destroy(nextVehicle);
            return false;
        }

        if (spawnedVehicle != null)
        {
            Destroy(spawnedVehicle);
        }

        spawnedVehicle = nextVehicle;
        cameraFollow.SetTarget(cameraTarget);

        Debug.Log(
            $"Spawned unlocked vehicle: {vehicleData.DisplayName} " +
            $"({vehicleData.AssetID})"
        );

        return true;
    }

    public void Despawn()
    {
        if (spawnedVehicle == null)
        {
            return;
        }

        Destroy(spawnedVehicle);
        spawnedVehicle = null;
        cameraFollow.SetTarget(null);
    }

    private static Transform FindCameraTarget(Transform vehicleRoot)
    {
        foreach (Transform child in vehicleRoot.GetComponentsInChildren<Transform>(true))
        {
            if (child.name == "CameraTarget")
            {
                return child;
            }
        }

        return null;
    }
}
