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

    public bool TryGetByAssetID(string assetID, out VehicleData vehicleData)
    {
        vehicleData = null;

        if (string.IsNullOrWhiteSpace(assetID) || vehicles == null)
        {
            return false;
        }

        string normalizedAssetID = assetID.Trim();

        foreach (VehicleData vehicle in vehicles)
        {
            if (
                vehicle != null &&
                string.Equals(
                    vehicle.AssetID?.Trim(),
                    normalizedAssetID,
                    StringComparison.OrdinalIgnoreCase
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
