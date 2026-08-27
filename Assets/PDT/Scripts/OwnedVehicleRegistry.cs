using System;
using System.Collections.Generic;
using UnityEngine;

public class OwnedVehicleRegistry : MonoBehaviour
{
    [SerializeField] private VehicleCatalog vehicleCatalog;

    private readonly List<VehicleData> unlockedVehicles = new List<VehicleData>();

    public IReadOnlyList<VehicleData> UnlockedVehicles => unlockedVehicles;

    public event Action<VehicleData> VehicleUnlocked;
    public event Action RegistryCleared;

    public bool TryRegisterResolvedEntitlement(
        TokenEntitlement entitlement,
        out VehicleData vehicleData
    )
    {
        vehicleData = null;

        if (vehicleCatalog == null)
        {
            Debug.LogError("OwnedVehicleRegistry has no VehicleCatalog assigned.");
            return false;
        }

        if (entitlement == null)
        {
            Debug.LogError("Cannot register a null token entitlement.");
            return false;
        }

        return TryRegisterEntitlementKey(
            entitlement.EntitlementKey,
            out vehicleData
        );
    }

    public bool TryRegisterDevelopmentEntitlementKey(
        string entitlementKey,
        out VehicleData vehicleData
    )
    {
        if (!Debug.isDebugBuild)
        {
            vehicleData = null;
            Debug.LogError(
                "Development entitlement registration is disabled in " +
                "non-development builds."
            );
            return false;
        }

        if (vehicleCatalog == null)
        {
            vehicleData = null;
            Debug.LogError(
                "OwnedVehicleRegistry has no VehicleCatalog assigned."
            );
            return false;
        }

        return TryRegisterEntitlementKey(
            entitlementKey,
            out vehicleData
        );
    }

    private bool TryRegisterEntitlementKey(
        string entitlementKey,
        out VehicleData vehicleData
    )
    {
        if (
            !vehicleCatalog.TryGetByEntitlementKey(
                entitlementKey,
                out vehicleData
            )
        )
        {
            Debug.LogWarning(
                $"Entitlement key '{entitlementKey}' is not supported by " +
                "this game."
            );
            return false;
        }

        if (unlockedVehicles.Contains(vehicleData))
        {
            return true;
        }

        unlockedVehicles.Add(vehicleData);
        VehicleUnlocked?.Invoke(vehicleData);

        Debug.Log(
            $"Unlocked vehicle: {vehicleData.DisplayName} " +
            $"({vehicleData.EntitlementKey})"
        );

        return true;
    }

    public void Clear()
    {
        if (unlockedVehicles.Count == 0)
        {
            return;
        }

        unlockedVehicles.Clear();
        RegistryCleared?.Invoke();
    }
}
