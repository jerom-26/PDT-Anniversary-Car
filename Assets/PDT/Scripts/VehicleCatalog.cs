using System;
using System.Collections.Generic;
using UnityEngine;

[CreateAssetMenu(
    fileName = "NewVehicleCatalog",
    menuName = "PDT/Vehicle Catalog"
)]
public class VehicleCatalog : ScriptableObject
{
    [SerializeField] private VehicleData[] vehicles = Array.Empty<VehicleData>();

    public IReadOnlyList<VehicleData> Vehicles => vehicles;

    public bool TryGetByEntitlementKey(
        string entitlementKey,
        out VehicleData vehicleData
    )
    {
        vehicleData = null;

        if (
            !EntitlementKeys.TryNormalize(
                entitlementKey,
                out string normalizedEntitlementKey
            ) ||
            vehicles == null
        )
        {
            return false;
        }

        foreach (VehicleData vehicle in vehicles)
        {
            if (
                vehicle != null &&
                string.Equals(
                    vehicle.EntitlementKey,
                    normalizedEntitlementKey,
                    StringComparison.Ordinal
                )
            )
            {
                vehicleData = vehicle;
                return true;
            }
        }

        return false;
    }
}
