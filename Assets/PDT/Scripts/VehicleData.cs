using UnityEngine;

[CreateAssetMenu(
    fileName = "NewVehicleData",
    menuName = "PDT/Vehicle Data"
)]

public class VehicleData : ScriptableObject
{
    [SerializeField] private string assetID;
    [SerializeField] private string displayName;
    [SerializeField] private GameObject vehiclePrefab;

    public string AssetID => assetID;
    public string DisplayName => displayName;
    public GameObject VehiclePrefab => vehiclePrefab;

}
