using UnityEngine;

public class CameraFollow : MonoBehaviour
{
    [SerializeField] private Transform target;
    [SerializeField] private Vector3 offSet = new Vector3 (0, 4, -6);

    void LateUpdate()
    {
        if (target == null)
        {
            return;
        }
         Vector3 desiredPosition = target.TransformPoint (offSet);

        transform.position = desiredPosition;
        transform.LookAt(target.position);
        
    }

    public void SetTarget(Transform newTarget)
    {
        target = newTarget;
    }
}
