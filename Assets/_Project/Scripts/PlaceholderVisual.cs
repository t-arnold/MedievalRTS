using UnityEngine;

/// <summary>Generates simple sprites and an unlit material at runtime, so testing needs no art.</summary>
public static class PlaceholderArt
{
    static Sprite circle, square;
    static Material unlit;

    public static Sprite Circle => circle ? circle : circle = MakeSprite(64, true);
    public static Sprite Square => square ? square : square = MakeSprite(8, false);

    public static Material Unlit
    {
        get
        {
            if (unlit) return unlit;
            var shader = Shader.Find("Universal Render Pipeline/2D/Sprite-Unlit-Default");
            if (!shader) shader = Shader.Find("Sprites/Default");
            unlit = new Material(shader);
            return unlit;
        }
    }

    static Sprite MakeSprite(int size, bool round)
    {
        var tex = new Texture2D(size, size, TextureFormat.RGBA32, false) { filterMode = FilterMode.Point };
        float r = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                bool inside = !round || new Vector2(x + 0.5f - r, y + 0.5f - r).magnitude <= r;
                tex.SetPixel(x, y, inside ? Color.white : Color.clear);
            }
        tex.Apply();
        // pixelsPerUnit = size, so the sprite is exactly 1 world unit wide.
        return Sprite.Create(tex, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f), size);
    }

    public static SpriteRenderer MakeRenderer(Transform parent, string name, Sprite sprite, Color color, Vector2 scale)
    {
        var go = new GameObject(name);
        go.transform.SetParent(parent, false);
        go.transform.localScale = new Vector3(scale.x, scale.y, 1f);
        var sr = go.AddComponent<SpriteRenderer>();
        sr.sprite = sprite;
        sr.color = color;
        sr.sharedMaterial = Unlit;
        return sr;
    }
}

/// <summary>
/// Stand-in visuals for a combatant: team-coloured body (on a horse for cavalry), weapon shape,
/// state tint, attack-arc preview (telegraph), arrow landing circle, swing smear, hit flash,
/// unblockable warning, shield, rank pips.
/// Delete this component once real sprites exist; it only reads combat state.
/// </summary>
[RequireComponent(typeof(CombatStateMachine))]
public class PlaceholderVisual : MonoBehaviour
{
    public Color teamColor = new Color(0.3f, 0.5f, 1f);
    [Tooltip("Seconds a corpse stays before being removed (never removes the player).")]
    public float corpseLifetime = 6f;

    CombatStateMachine csm;
    SpriteRenderer body, weapon, shield, warning, horse;
    readonly SpriteRenderer[] pips = new SpriteRenderer[5];
    LineRenderer arc;
    float flashTimer, deadTimer, promoteTimer;
    UnitBrain ai;
    CavalryAI rider;
    UnitLoadout loadout;
    WeaponData shownWeapon;
    bool looked;
    int lastPips = -1;
    float weaponLength = 0.45f;

    static readonly Color WindupTint = new Color(1f, 0.85f, 0.3f);
    static readonly Color RecoveryTint = new Color(0.45f, 0.45f, 0.45f);
    static readonly Color StaggerTint = new Color(0.7f, 0.3f, 0.9f);
    static readonly Color DeadTint = new Color(0.25f, 0.25f, 0.25f);
    static readonly Color HorseColor = new Color(0.42f, 0.3f, 0.2f);

    void Awake()
    {
        csm = GetComponent<CombatStateMachine>();

        body = PlaceholderArt.MakeRenderer(transform, "Body", PlaceholderArt.Circle, teamColor, new Vector2(0.7f, 0.7f));
        body.transform.localPosition = new Vector3(0f, 0.35f, 0f); // pivot at feet, body above

        weapon = PlaceholderArt.MakeRenderer(transform, "Weapon", PlaceholderArt.Square, Color.white, new Vector2(0.12f, 0.45f));
        shield = PlaceholderArt.MakeRenderer(transform, "Shield", PlaceholderArt.Square, new Color(0.8f, 0.8f, 0.9f), new Vector2(0.7f, 0.12f));
        shield.enabled = false;

        warning = PlaceholderArt.MakeRenderer(transform, "UnblockableFlag", PlaceholderArt.Circle, new Color(1f, 0.1f, 0.1f), new Vector2(0.25f, 0.25f));
        warning.transform.localPosition = new Vector3(0f, 1.05f, 0f);
        warning.enabled = false;

        // Rank pips above the head: one gold square per experience tier above Levy.
        for (int i = 0; i < pips.Length; i++)
        {
            pips[i] = PlaceholderArt.MakeRenderer(transform, "Pip" + i, PlaceholderArt.Square, new Color(1f, 0.82f, 0.25f), new Vector2(0.09f, 0.09f));
            pips[i].transform.localPosition = new Vector3((i - 2) * 0.13f, 0.85f, 0f);
            pips[i].enabled = false;
        }

        var arcGo = new GameObject("AttackArc");
        arcGo.transform.SetParent(transform, false);
        arc = arcGo.AddComponent<LineRenderer>();
        arc.sharedMaterial = PlaceholderArt.Unlit;
        arc.useWorldSpace = true;
        arc.loop = true;
        arc.positionCount = 0;
        arc.numCapVertices = 2;
    }

    void OnEnable()
    {
        csm.OnHitReceived += HandleHit;
        csm.OnStateChanged += HandleState;
    }

    void OnDisable()
    {
        csm.OnHitReceived -= HandleHit;
        csm.OnStateChanged -= HandleState;
    }

    public void SetTeamColor(Color c)
    {
        teamColor = c;
        if (body) body.color = c;
    }

    /// <summary>Components are added after this one, so look them up on the first frame.</summary>
    void LookUp()
    {
        looked = true;
        ai = GetComponent<UnitBrain>();
        rider = GetComponent<CavalryAI>();
        loadout = GetComponent<UnitLoadout>();

        if (csm.isMounted)
        {
            horse = PlaceholderArt.MakeRenderer(transform, "Horse", PlaceholderArt.Circle, HorseColor, new Vector2(1.25f, 0.7f));
            horse.transform.localPosition = new Vector3(0f, 0.25f, 0f);
            body.transform.localScale = new Vector3(0.5f, 0.5f, 1f);
            body.transform.localPosition = new Vector3(0f, 0.55f, 0f);
            for (int i = 0; i < pips.Length; i++) pips[i].transform.localPosition = new Vector3((i - 2) * 0.13f, 1.0f, 0f);
            warning.transform.localPosition = new Vector3(0f, 1.2f, 0f);
        }
    }

    void RefreshWeaponShape()
    {
        shownWeapon = loadout ? loadout.weapon : null;
        var look = shownWeapon ? shownWeapon.look : WeaponLook.Sword;
        Vector2 size;
        Color col = Color.white;
        switch (look)
        {
            case WeaponLook.Axe:   size = new Vector2(0.24f, 0.4f); col = new Color(0.85f, 0.85f, 0.8f); break;
            case WeaponLook.Spear: size = new Vector2(0.06f, 1.0f); col = new Color(0.9f, 0.85f, 0.7f); break;
            case WeaponLook.Bow:   size = new Vector2(0.5f, 0.07f); col = new Color(0.75f, 0.55f, 0.3f); break;
            case WeaponLook.Mace:  size = new Vector2(0.18f, 0.4f); break;
            case WeaponLook.Lance: size = new Vector2(0.07f, 1.3f); col = new Color(0.95f, 0.9f, 0.75f); break;
            default:               size = new Vector2(0.1f, 0.5f); break;
        }
        weapon.transform.localScale = new Vector3(size.x, size.y, 1f);
        weapon.color = col;
        weaponLength = look == WeaponLook.Bow ? 0.15f : size.y;
    }

    void HandleHit(HitResult r)
    {
        if (r.type == HitType.Hit || r.type == HitType.CounterHit || r.type == HitType.GuardBroken)
            flashTimer = 0.08f;
    }

    void HandleState(CombatState from, CombatState to)
    {
        warning.enabled = to == CombatState.Windup && csm.CurrentAttack && csm.CurrentAttack.telegraph == TelegraphType.Unblockable;

        if (to == CombatState.Windup && csm.CurrentAttack && csm.CurrentAttack.telegraphSound)
            AudioSource.PlayClipAtPoint(csm.CurrentAttack.telegraphSound, transform.position);

        if (to == CombatState.Dead)
        {
            foreach (var c in GetComponentsInChildren<Collider2D>()) c.enabled = false;
            var rb = GetComponent<Rigidbody2D>();
            if (rb) rb.simulated = false;
        }
    }

    void LateUpdate()
    {
        if (!looked) LookUp();
        if ((loadout ? loadout.weapon : null) != shownWeapon || weaponLength <= 0f) RefreshWeaponShape();

        Vector2 f = csm.Facing;
        float angle = Mathf.Atan2(f.y, f.x) * Mathf.Rad2Deg - 90f;
        float bodyY = body.transform.localPosition.y;

        // Weapon held out in front, pointing where you aim.
        float reachOut = 0.25f + weaponLength * 0.5f;
        weapon.transform.localPosition = new Vector3(f.x * reachOut, bodyY + f.y * reachOut, 0f);
        weapon.transform.localRotation = Quaternion.Euler(0, 0, angle);

        bool hasShield = !loadout || !loadout.weapon || loadout.weapon.hasShield;
        shield.enabled = csm.State == CombatState.Blocking;
        shield.transform.localScale = hasShield ? new Vector3(0.7f, 0.12f, 1f) : new Vector3(0.4f, 0.06f, 1f);
        shield.transform.localPosition = new Vector3(f.x * 0.5f, bodyY + f.y * 0.5f, 0f);
        shield.transform.localRotation = Quaternion.Euler(0, 0, angle);
        shield.color = csm.StateTime <= csm.parryWindow ? Color.cyan : new Color(0.8f, 0.8f, 0.9f); // cyan = parry window

        if (horse)
        {
            Vector2 h = rider ? rider.Heading : f;
            horse.transform.localRotation = Quaternion.Euler(0, 0, Mathf.Atan2(h.y, h.x) * Mathf.Rad2Deg);
            horse.color = csm.State == CombatState.Dead ? DeadTint : HorseColor;
        }

        // ---- body tint by state ----
        Color c = csm.State switch
        {
            CombatState.Windup => Color.Lerp(teamColor, WindupTint, 0.6f),
            CombatState.Active => Color.Lerp(teamColor, Color.white, 0.5f),
            CombatState.Recovery => Color.Lerp(teamColor, RecoveryTint, 0.6f),
            CombatState.Staggered => StaggerTint,
            CombatState.Dead => DeadTint,
            CombatState.Dodging => new Color(teamColor.r, teamColor.g, teamColor.b, 0.45f),
            _ => teamColor,
        };
        if (flashTimer > 0f) { flashTimer -= Time.deltaTime; c = Color.white; }

        // ---- rank pips + gold flash on promotion ----
        int rank = ai && ai.skill && csm.State != CombatState.Dead ? ai.skill.rankPips : 0;
        if (lastPips >= 0 && rank > lastPips) promoteTimer = 0.5f;
        if (csm.State != CombatState.Dead) lastPips = rank;
        for (int i = 0; i < pips.Length; i++) pips[i].enabled = i < rank;
        if (promoteTimer > 0f) { promoteTimer -= Time.deltaTime; c = new Color(1f, 0.85f, 0.2f); }

        body.color = c;

        DrawTelegraph();

        // ---- y-sorting: lower on screen = drawn in front ----
        int order = -Mathf.RoundToInt(transform.position.y * 100f);
        if (horse) horse.sortingOrder = order - 1;
        body.sortingOrder = order;
        weapon.sortingOrder = shield.sortingOrder = order + 1;
        warning.sortingOrder = order + 2;
        foreach (var p in pips) p.sortingOrder = order + 2;
        arc.sortingOrder = order - 2;

        if (csm.State == CombatState.Dead && !csm.isPlayer)
        {
            deadTimer += Time.deltaTime;
            if (deadTimer > corpseLifetime) Destroy(gameObject);
        }
    }

    void DrawTelegraph()
    {
        var atk = csm.CurrentAttack;
        bool show = atk && (csm.State == CombatState.Windup || csm.State == CombatState.Active);
        if (!show) { arc.positionCount = 0; return; }

        if (atk.projectile)
        {
            // Ranged: a circle where the arrows will land, while drawing the bow.
            if (csm.State != CombatState.Windup || atk.projectile.mode != ProjectileMode.Arc) { arc.positionCount = 0; return; }
            var p = atk.projectile;
            float d = Mathf.Clamp(csm.AimDistance, p.minRange, p.maxRange);
            Vector2 centre = (Vector2)transform.position + csm.Facing * d;
            float radius = p.hitRadius + d * p.scatter + (atk.projectileCount > 1 ? d * Mathf.Tan(atk.spreadAngle * 0.5f * Mathf.Deg2Rad) : 0f);
            arc.startColor = arc.endColor = new Color(1f, 0.6f, 0.2f, 0.4f);
            arc.widthMultiplier = 0.035f;
            const int seg = 20;
            arc.positionCount = seg;
            for (int i = 0; i < seg; i++)
            {
                float a = i / (float)seg * Mathf.PI * 2f;
                arc.SetPosition(i, centre + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            }
            return;
        }

        bool active = csm.State == CombatState.Active;
        Color col = atk.telegraph == TelegraphType.Unblockable ? new Color(1f, 0.15f, 0.15f) : new Color(1f, 0.8f, 0.3f);
        col = active ? Color.white : new Color(col.r, col.g, col.b, 0.35f);
        arc.startColor = arc.endColor = col;
        arc.widthMultiplier = active ? 0.12f : 0.04f; // active arc doubles as the swing smear

        const int segments = 14;
        arc.positionCount = segments + 2;
        Vector3 o = transform.position;
        arc.SetPosition(0, o);
        float half = atk.arcAngle * 0.5f;
        float baseAngle = Mathf.Atan2(csm.Facing.y, csm.Facing.x) * Mathf.Rad2Deg;
        for (int i = 0; i <= segments; i++)
        {
            float a = (baseAngle - half + atk.arcAngle * i / segments) * Mathf.Deg2Rad;
            arc.SetPosition(i + 1, o + new Vector3(Mathf.Cos(a), Mathf.Sin(a)) * atk.reach);
        }
    }
}
