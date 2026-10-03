using UnityEngine;
using UnityEngine.Rendering.Universal;

[ExecuteAlways]
public class FaceDownwards : MonoBehaviour
{
    public DecalProjector projector;

    private void OnEnable()
    {
        if (Vector3.Dot(Vector3.up, transform.parent.up) < 0.7)
        {
            projector.enabled = false;
        }
        else
            projector.enabled = true;

        ApplyRotation();
    }

    private void LateUpdate()
    {
        if(Vector3.Dot(Vector3.up, transform.parent.up) < 0.7)
        {
            projector.enabled = false;
        }
        else
            projector.enabled = true;

        ApplyRotation();
    }

    private void ApplyRotation()
    {
        transform.rotation =
            Quaternion.LookRotation(Vector3.down, Vector3.forward);
    }
}