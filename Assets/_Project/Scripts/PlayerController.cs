using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Top-down, Magicka-style controller: WASD moves, the character always faces the mouse.
/// Works alone for learning step 1 (leave Combat empty), then plug in CombatStateMachine.
///
/// Controls: WASD move, Shift sprint, aim with mouse, LMB light attack, E heavy attack,
///           RMB block, Space dodge, Q switch weapon. Gamepad: left stick move, right stick aim.
/// With the bow, arrows land where the mouse is.
///
/// Setup: Rigidbody2D (added automatically, configured in Awake), a small Collider2D at the feet,
/// sprite pivot at the feet, Animator floats "Speed", "DirX", "DirY".
/// </summary>
[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController : MonoBehaviour
{
    [Header("References")]
    public Camera cam;
    public Animator animator;
    [Tooltip("Optional until you reach the combat step.")]
    public CombatStateMachine combat;
    public AttackData lightAttack;
    public AttackData heavyAttack;

    [Header("Weapons (Q cycles)")]
    public WeaponData[] weapons;

    [Header("Movement (world units/sec)")]
    public float walkSpeed = 1.5f;
    public float runSpeed = 3.5f;
    public float sprintSpeed = 5f;
    [Tooltip("Units/sec². Lower = heavier, more momentum.")]
    public float acceleration = 30f;

    [Header("Aim")]
    [Tooltip("Right-stick deflection needed before the gamepad overrides the mouse.")]
    public float stickDeadzone = 0.3f;
    [Tooltip("Arc distance used when aiming with the right stick.")]
    public float stickAimDistance = 9f;

    public int WeaponIndex { get; private set; }
    public WeaponData CurrentWeapon => weapons != null && weapons.Length > 0 ? weapons[WeaponIndex] : null;

    Rigidbody2D rb;
    InputAction move, aimStick, sprint, attack, heavy, block, dodge, switchWeapon;
    Vector2 velocity;
    Vector2 facing = Vector2.down; // used only when there is no CombatStateMachine

    static readonly int SpeedHash = Animator.StringToHash("Speed");
    static readonly int DirXHash = Animator.StringToHash("DirX");
    static readonly int DirYHash = Animator.StringToHash("DirY");
    static readonly int AttackHash = Animator.StringToHash("Attack");

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        rb.gravityScale = 0f;
        rb.freezeRotation = true;
        rb.interpolation = RigidbodyInterpolation2D.Interpolate;
        rb.collisionDetectionMode = CollisionDetectionMode2D.Continuous;

        if (!animator) animator = GetComponentInChildren<Animator>();
        if (!combat) combat = GetComponent<CombatStateMachine>();
        if (!cam) cam = Camera.main;

        move = new InputAction("Move", InputActionType.Value);
        move.AddCompositeBinding("2DVector")
            .With("Up", "<Keyboard>/w").With("Down", "<Keyboard>/s")
            .With("Left", "<Keyboard>/a").With("Right", "<Keyboard>/d");
        move.AddBinding("<Gamepad>/leftStick");

        aimStick = new InputAction("AimStick", InputActionType.Value, "<Gamepad>/rightStick");

        sprint = Button("Sprint", "<Keyboard>/leftShift", "<Gamepad>/leftStickPress");
        attack = Button("Attack", "<Mouse>/leftButton",   "<Gamepad>/rightShoulder");
        heavy  = Button("Heavy",  "<Keyboard>/e",         "<Gamepad>/rightTrigger");
        block  = Button("Block",  "<Mouse>/rightButton",  "<Gamepad>/leftTrigger");
        dodge  = Button("Dodge",  "<Keyboard>/space",     "<Gamepad>/buttonEast");
        switchWeapon = Button("SwitchWeapon", "<Keyboard>/q", "<Gamepad>/buttonNorth");
    }

    static InputAction Button(string name, string kb, string pad)
    {
        var a = new InputAction(name, InputActionType.Button, kb);
        a.AddBinding(pad);
        return a;
    }

    InputAction[] All => new[] { move, aimStick, sprint, attack, heavy, block, dodge, switchWeapon };
    void OnEnable()  { foreach (var a in All) a.Enable(); }
    void OnDisable() { foreach (var a in All) a.Disable(); }

    /// <summary>Switch to weapon i (wraps around). Applies its attacks and shield stats.</summary>
    public void Equip(int i)
    {
        if (weapons == null || weapons.Length == 0) return;
        int n = weapons.Length;
        WeaponIndex = ((i % n) + n) % n;
        var w = weapons[WeaponIndex];
        if (!w) return;

        lightAttack = w.playerLight ? w.playerLight : (w.aiAttacks != null && w.aiAttacks.Length > 0 ? w.aiAttacks[0] : lightAttack);
        heavyAttack = w.playerHeavy ? w.playerHeavy : lightAttack;

        var lo = GetComponent<UnitLoadout>();
        if (lo) lo.Equip(w, unlimitedAmmo: true);
        else UnitLoadout.ApplyWeapon(combat, w);
    }

    void Update()
    {
        float dt = Time.deltaTime;
        Vector2 input = Vector2.ClampMagnitude(move.ReadValue<Vector2>(), 1f);

        // ---- aim: right stick if held, otherwise toward the mouse ----
        Vector2 aim = aimStick.ReadValue<Vector2>();
        if (aim.magnitude >= stickDeadzone) aim = aim.normalized * stickAimDistance;
        else if (Mouse.current != null && cam)
        {
            Vector2 mouseWorld = cam.ScreenToWorldPoint(Mouse.current.position.ReadValue());
            aim = mouseWorld - (Vector2)transform.position;
        }

        if (combat)
        {
            bool free = combat.State == CombatState.Idle || combat.State == CombatState.Blocking;
            if (switchWeapon.WasPressedThisFrame() && free) Equip(WeaponIndex + 1);

            combat.SetAim(aim);
            combat.SetBlock(block.IsPressed());
            if (attack.WasPressedThisFrame()) combat.RequestAttack(lightAttack);
            if (heavy.WasPressedThisFrame()) combat.RequestAttack(heavyAttack);
            if (dodge.WasPressedThisFrame()) combat.RequestDodge(input); // no input = backstep
        }
        else
        {
            // Learning step 1: face the mouse and play a swing.
            if (aim.sqrMagnitude > 0.0001f) facing = aim.normalized;
            if (attack.WasPressedThisFrame() && animator) animator.SetTrigger(AttackHash);
            if (animator)
            {
                animator.SetFloat(DirXHash, facing.x);
                animator.SetFloat(DirYHash, facing.y);
            }
        }

        // ---- movement ----
        bool canMove = !combat || combat.CanMove;
        bool blocking = combat && combat.State == CombatState.Blocking;
        float targetSpeed = !canMove ? 0f
                          : blocking ? walkSpeed
                          : sprint.IsPressed() ? sprintSpeed
                          : runSpeed;
        velocity = Vector2.MoveTowards(velocity, input * targetSpeed, acceleration * dt);

        if (animator) animator.SetFloat(SpeedHash, velocity.magnitude, 0.05f, dt);
    }

    void FixedUpdate()
    {
        Vector2 forced = combat ? combat.ForcedVelocity : Vector2.zero;
        rb.linearVelocity = velocity + forced;
    }
}
