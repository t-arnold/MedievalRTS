using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Follow camera for the top-down view: smooth follow, scroll-wheel zoom, trauma-based screen shake
/// driven by the followed combatant's hits. Put on the Main Camera.
/// Later, swap for Pixel Perfect Camera + this script's follow logic.
/// </summary>
[RequireComponent(typeof(Camera))]
public class CameraRig2D : MonoBehaviour
{
    public Transform target;
    public float smoothTime = 0.12f;

    [Header("Zoom (orthographic size)")]
    public float zoom = 6f;
    public float minZoom = 3f;
    public float maxZoom = 22f;
    public float zoomStep = 0.8f;

    [Header("Shake")]
    public float maxShakeOffset = 0.35f;
    public float shakeDecay = 2.5f;
    [Range(0f, 1f), Tooltip("0 = no shake. Accessibility setting later.")]
    public float shakeScale = 1f;

    Camera cam;
    Vector3 followPos, followVel;
    float trauma;
    CombatStateMachine followed;

    void Awake()
    {
        cam = GetComponent<Camera>();
        cam.orthographic = true;
        followPos = transform.position;
    }

    public void Follow(CombatStateMachine csm)
    {
        if (followed)
        {
            followed.OnHitLanded -= OnLanded;
            followed.OnHitReceived -= OnReceived;
        }
        followed = csm;
        target = csm ? csm.transform : null;
        if (csm)
        {
            csm.OnHitLanded += OnLanded;
            csm.OnHitReceived += OnReceived;
            followPos = new Vector3(target.position.x, target.position.y, transform.position.z);
        }
    }

    void OnLanded(HitResult r) => AddShake(r.attack.screenShake);
    void OnReceived(HitResult r) => AddShake(r.attack.screenShake * 1.5f);

    public void AddShake(float amount) => trauma = Mathf.Clamp01(trauma + amount);

    void LateUpdate()
    {
        float dt = Time.unscaledDeltaTime;

        if (Mouse.current != null)
        {
            float scroll = Mouse.current.scroll.ReadValue().y;
            if (Mathf.Abs(scroll) > 0.01f) zoom = Mathf.Clamp(zoom - Mathf.Sign(scroll) * zoomStep, minZoom, maxZoom);
        }
        cam.orthographicSize = Mathf.Lerp(cam.orthographicSize, zoom, 1f - Mathf.Exp(-12f * dt));

        if (target)
        {
            Vector3 goal = new Vector3(target.position.x, target.position.y, transform.position.z);
            followPos = Vector3.SmoothDamp(followPos, goal, ref followVel, smoothTime, Mathf.Infinity, dt);
        }

        // Trauma squared feels better than linear: small hits barely shake, big ones really do.
        Vector2 offset = Random.insideUnitCircle * (trauma * trauma * maxShakeOffset * shakeScale);
        trauma = Mathf.Max(0f, trauma - shakeDecay * dt);
        transform.position = followPos + (Vector3)offset;
    }
}
