using UnityEngine;
using UnityEngine.InputSystem;

[RequireComponent(typeof(Rigidbody2D))]
public class PlayerController2D : MonoBehaviour, PlayerControls.IPlayerActions
{
    // ───────── Inspector ─────────
    [Header("Run")]
    [SerializeField] float maxSpeed = 8f;
    [SerializeField] float accelGround = 80f;
    [SerializeField] float decelGround = 60f;
    [SerializeField] float accelAirMul = 0.4f;
    [SerializeField] float decelAirMul = 0.2f;

    [Header("Jump")]
    [SerializeField] float jumpForce = 16f;
    [SerializeField] float coyoteTime = 0.1f;
    [SerializeField] float jumpCut = 0.5f;
    [SerializeField] uint extraJumps = 0;

    [Header("Dash")]
    [SerializeField] float dashSpeed = 18f;
    [SerializeField] float dashTime = 0.20f;
    [SerializeField] float dashCooldown = 0.50f;

    [Header("Ground Check")]
    [SerializeField] Transform groundProbe;
    [SerializeField] Vector2 probeSize = new(0.9f, 0.1f);
    [SerializeField] LayerMask groundMask;

    [Header("Physics")]
    [SerializeField] float baseGravity = 3f;
    [SerializeField] float jumpCutGravity = 6f;
    float normalGravity;

    // ───────── State ─────────
    Rigidbody2D rb;
    PlayerControls input;

    Vector2 moveInput;
    bool jumpPressed, jumpHeld;
    bool dashPressed;

    bool grounded;
    float lastGrounded;
    uint airJumpsLeft;
    bool jumping;

    bool dashReady = true;
    bool dashing;
    float dashEnd;
    Vector3 originScale;

    void Awake()
    {
        rb = GetComponent<Rigidbody2D>();
        input = new PlayerControls();
        input.Player.SetCallbacks(this);
        normalGravity = baseGravity;
        rb.gravityScale = baseGravity;
        originScale = transform.localScale;
    }

    void OnEnable() => input.Enable();
    void OnDisable() => input.Disable();

    // ───────── Input Callbacks ─────────
    public void OnMove(InputAction.CallbackContext ctx)
        => moveInput = ctx.ReadValue<Vector2>();

    public void OnJump(InputAction.CallbackContext ctx)
    {
        if (ctx.started) { jumpPressed = true; jumpHeld = true; }
        else if (ctx.canceled) jumpHeld = false;
    }

    public void OnDash(InputAction.CallbackContext ctx)
    {
        if (ctx.started) dashPressed = true;
    }

    public void OnFire(InputAction.CallbackContext ctx)
    {

    }

    void Update()
    {

        grounded = Physics2D.OverlapBox(groundProbe.position, probeSize, 0, groundMask);
        if (grounded) { lastGrounded = Time.time; airJumpsLeft = extraJumps; }


        float inputX = moveInput.x;

        if (inputX > 0)
        {
            transform.localScale = originScale;
        }
        else if (inputX < 0)
        {
            transform.localScale = new Vector3(-originScale.x, originScale.y, originScale.z);
        }

        float targetSpd = inputX * maxSpeed;
        float accel = (Mathf.Abs(targetSpd) > 0.01f ? accelGround : decelGround);
        if (!grounded) accel *= inputX != 0 ? accelAirMul : decelAirMul;
        float newVelX = Mathf.MoveTowards(rb.linearVelocity.x, targetSpd, accel * Time.deltaTime);


        bool withinCoy = Time.time - lastGrounded <= coyoteTime;
        if (jumpPressed)
        {
            bool canJump = grounded || withinCoy || airJumpsLeft > 0;
            if (canJump)
            {
                rb.linearVelocity = new(newVelX, jumpForce);
                jumping = true;
                if (!grounded && !withinCoy) airJumpsLeft--;
            }
        }

        if (!jumpHeld && rb.linearVelocity.y > 0f)
            rb.gravityScale = jumpCutGravity;
        else if (!dashing)
            rb.gravityScale = normalGravity;

        // 4) Dash logic
        if (dashPressed && dashReady && !dashing)
        {
            dashing = true;
            dashReady = false;
            dashEnd = Time.time + dashTime;
            rb.linearVelocity = new(Mathf.Sign(transform.localScale.x) * dashSpeed, 0);
            rb.gravityScale = 0;
        }
        if (dashing && Time.time >= dashEnd)
        {
            dashing = false;
            rb.gravityScale = 1;
        }
        if (!dashReady && Time.time >= dashEnd + dashCooldown) dashReady = true;


        if (!dashing) rb.linearVelocity = new(newVelX, rb.linearVelocity.y);

        jumpPressed = dashPressed = false;
    }

#if UNITY_EDITOR
    void OnDrawGizmosSelected()
    {
        if (groundProbe)
        {
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(groundProbe.position, probeSize);
        }
    }
#endif
}
