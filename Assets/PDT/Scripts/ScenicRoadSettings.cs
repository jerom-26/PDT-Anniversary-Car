using UnityEngine;

// Authoring data only. The scene uses baked meshes and has no road generator at runtime.
[CreateAssetMenu(menuName = "PDT/Scenic Road Settings")]
public sealed class ScenicRoadSettings : ScriptableObject
{
    [Min(6f)] public float roadWidth = 10f;
    [Min(1f)] public float shoulderWidth = 2f;
    [Range(0f, 1.5f)] public float elevationScale = 1f;
    [Range(24, 80)] public int samplesPerSpan = 48;
    [Range(0, 400)] public int treeCount = 190;
    [Range(0, 150)] public int rockCount = 65;
    public int scenerySeed = 80;
    [Range(0f, 18f)] public float backgroundHillHeight = 12f;
    [Tooltip("Closed road centreline in metres. Keep the first three points on x=0, y=0 so the existing spawn stays on the arrival straight.")]
    public Vector3[] controlPoints =
    {
        new Vector3(0, 0, -55), new Vector3(0, 0, 0), new Vector3(0, 0, 65),
        new Vector3(35, 1, 125), new Vector3(110, 3, 145),
        new Vector3(190, 5, 100), new Vector3(230, 4, 15),
        new Vector3(210, 2, -70), new Vector3(145, 1, -125),
        new Vector3(60, 0, -130), new Vector3(5, 0, -100)
    };
}
