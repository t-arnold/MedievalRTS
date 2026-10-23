using UnityEngine;

public enum ProjectileMode
{
    /// <summary>Lobbed over heads; lands at the aim point (plus scatter) and hits whoever is there.</summary>
    Arc,
    /// <summary>Flat shot; hits the first thing in its path (crossbow bolt, ballista).</summary>
    Direct,
}

/// <summary>
/// How a projectile flies and what it can hit. Arrows today; the same data covers siege later
/// (a mangonel stone = Arc, big hitRadius, maxTargets 0; a ballista bolt = Direct, pierce several).
/// Create via Assets > Create > Combat > Projectile.
/// </summary>
[CreateAssetMenu(menuName = "Combat/Projectile", fileName = "NewProjectile")]
public class ProjectileData : ScriptableObject
{
    public ProjectileMode mode = ProjectileMode.Arc;
    [Tooltip("World units per second along the ground.")]
    public float speed = 15f;
    public float minRange = 3f;
    public float maxRange = 17f;

    [Header("Hitting")]
    [Tooltip("Arc: radius at the landing point. Direct: thickness of the flight path.")]
    public float hitRadius = 0.38f;
    [Tooltip("Arc only: landing scatter as a fraction of distance (0.05 = within 0.5 units at 10 units).")]
    public float scatter = 0.045f;
    [Tooltip("Units one projectile can hit. 1 = arrow. 0 = everyone in the radius (siege splash).")]
    public int maxTargets = 1;

    [Header("Look (placeholder)")]
    [Tooltip("Arc only: peak height drawn on screen per 10 units of distance.")]
    public float arcHeight = 1.4f;
    public float length = 0.45f;
    public float width = 0.05f;
    public Color color = new Color(0.92f, 0.86f, 0.7f);
    [Tooltip("Seconds a missed projectile stays stuck in the ground.")]
    public float stuckTime = 3f;
}
