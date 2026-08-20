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

    public bool TryRegisterVerifiedMetadata(
        NFTMetadata metadata,
        out VehicleData vehicleData
    )
    {
        vehicleData = null;

        if (vehicleCatalog == null)
        {
            Debug.LogError("OwnedVehicleRegistry has no VehicleCatalog assigned.");
            return false;
        }

        if (!TryGetAssetID(metadata, out string assetID))
        {
            Debug.LogError(
                "Verified NFT metadata does not contain an Asset ID or modelID."
            );
            return false;
        }

        if (!vehicleCatalog.TryGetByAssetID(assetID, out vehicleData))
        {
            Debug.LogWarning(
                $"Owned NFT Asset ID '{assetID}' is not supported by this game."
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
            $"Unlocked vehicle: {vehicleData.DisplayName} ({vehicleData.AssetID})"
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

    private static bool TryGetAssetID(NFTMetadata metadata, out string assetID)
    {
        assetID = null;

        if (metadata == null || metadata.attributes == null)
        {
            return false;
        }

        foreach (NFTAttribute attribute in metadata.attributes)
        {
            if (attribute == null)
            {
                continue;
            }

            bool isAssetID = string.Equals(
                attribute.trait_type?.Trim(),
                "Asset ID",
                StringComparison.OrdinalIgnoreCase
            );
            bool isLegacyModelID = string.Equals(
                attribute.trait_type?.Trim(),
                "modelID",
                StringComparison.OrdinalIgnoreCase
            );

            if (
                (isAssetID || isLegacyModelID) &&
                !string.IsNullOrWhiteSpace(attribute.value)
            )
            {
                assetID = attribute.value.Trim();
                return true;
            }
        }

        return false;
    }
}
