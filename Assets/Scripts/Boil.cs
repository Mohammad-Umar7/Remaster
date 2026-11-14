using UnityEngine;

/// <summary>Redraws a UI element a few times a second with a tiny random tilt, like hand-drawn animation "boiling".</summary>
public class Boil : MonoBehaviour
{
    public float degrees = 0.5f;
    public float scale = 0.006f;
    public float fps = 7f;
    float next;
    Quaternion baseRot;
    Vector3 baseScale;
    bool init;

    void OnEnable()
    {
        if (!init) { baseRot = transform.localRotation; baseScale = transform.localScale; init = true; }
    }

    public void Rebase() { baseRot = transform.localRotation; baseScale = transform.localScale; init = true; }

    void Update()
    {
        if (Time.unscaledTime < next) return;
        next = Time.unscaledTime + 1f / fps;
        transform.localRotation = baseRot * Quaternion.Euler(0, 0, Random.Range(-degrees, degrees));
        float s = 1f + Random.Range(-scale, scale);
        transform.localScale = baseScale * s;
    }
}
