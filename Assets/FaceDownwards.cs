using UnityEngine;
using UnityEngine.Rendering.Universal;

[ExecuteAlways]
public class FaceDownwards : MonoBehaviour
{
    public DecalProjector projector;

    private void OnEnable()
    {
        if (Vector3.Dot(transform.up, transform.parent.up) < 0.7)
        {
            ApplyRotation();
            projector.enabled = true;
        }
        else
            projector.enabled = false;
    }

    private void LateUpdate()
    {
        if(Vector3.Dot(transform.up, transform.parent.up) < 0.7)
        {
            ApplyRotation();
            projector.enabled = true;
        }
        else
            projector.enabled = false;
    }

    private void ApplyRotation()
    {
        transform.rotation =
            Quaternion.LookRotation(Vector3.down, Vector3.forward);
    }
}