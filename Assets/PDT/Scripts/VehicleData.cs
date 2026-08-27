using UnityEngine;
using UnityEngine.Serialization;

[CreateAssetMenu(
    fileName = "NewVehicleData",
    menuName = "PDT/Vehicle Data"
)]

public class VehicleData : ScriptableObject
{
    [FormerlySerializedAs("assetID")]
    [SerializeField] private string entitlementKey;
    [SerializeField] private string displayName;
    [SerializeField] private GameObject vehiclePrefab;

    public string EntitlementKey => entitlementKey;
    public string DisplayName => displayName;
    public GameObject VehiclePrefab => vehiclePrefab;
}
