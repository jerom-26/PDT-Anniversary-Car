using Unity.VisualScripting;
using UnityEngine;

public class VehicleSpawner : MonoBehaviour
{
    [SerializeField] private GameObject vehiclePrefab;
    [SerializeField] private Transform spawnPoint;
    [SerializeField] private CameraFollow cameraFollow;

   void Start()
    {
        SpawnVehicle();
    }

    private void SpawnVehicle()
    {
        GameObject spawnedVehicle = Instantiate(vehiclePrefab, spawnPoint.position, spawnPoint.rotation);

        Transform cameraTarget = spawnedVehicle.transform.Find("CameraTarget");

        cameraFollow.SetTarget(cameraTarget);
    }

    void Update()
    {
        
    }
}
