using UnityEngine;

public class DevelopmentMetadataBootstrap : MonoBehaviour
{
    [Header("Development only - bypasses wallet verification")]
    [SerializeField] private bool runOnStart = true;
    [SerializeField] private string developmentEntitlementKey =
        EntitlementKeys.DreamMobile80th;
    [SerializeField] private bool spawnUnlockedVehicle = true;

    [Header("Components")]
    [SerializeField] private OwnedVehicleRegistry ownedVehicleRegistry;
    [SerializeField] private VehicleSpawner vehicleSpawner;

    private void Start()
    {
        if (!runOnStart)
        {
            return;
        }

        UnlockDevelopmentEntitlement();
    }

    public void UnlockDevelopmentEntitlement()
    {
        if (
            ownedVehicleRegistry == null ||
            (spawnUnlockedVehicle && vehicleSpawner == null)
        )
        {
            Debug.LogError(
                "DevelopmentMetadataBootstrap is missing a component " +
                "reference."
            );
            return;
        }

        Debug.LogWarning(
            "Development entitlement bootstrap bypasses wallet, ownerOf " +
            "and entitlementKeyOf verification."
        );

        if (
            ownedVehicleRegistry.TryRegisterDevelopmentEntitlementKey(
                developmentEntitlementKey,
                out VehicleData vehicleData
            ) &&
            spawnUnlockedVehicle
        )
        {
            vehicleSpawner.TrySpawn(vehicleData);
        }
    }
}
