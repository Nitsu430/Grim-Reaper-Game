// FirstPersonRigidbodyController_WithFootsteps.cs
// Unity 6 (6000.x) — Heavy, smoothed head-bob + code-driven footsteps using your AudioManager(FMOD)

using UnityEngine;
using System.Collections.Generic;
using FMODUnity;

[RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
public class FirstPersonRigidbodyController_WithFootsteps : MonoBehaviour
{
    [Header("Move")]
    public float walkSpeed = 4.5f;
    public float acceleration = 20f;
    public float airControl = 0.3f;
    public float maxGroundAngle = 60f;
    public float gravityExtra = 10f;

    [Header("Jump (optional)")]
    public bool enableJump = false;
    public float jumpForce = 5.5f;
    public float coyoteTime = 0.1f;

    [Header("Look")]
    public Transform cam;
    public float mouseSensitivity = 1.0f;
    public float maxPitch = 85f;
    public bool lockCursor = true;

    [Header("Ground Check")]
    public float groundCheckRadius = 0.2f;
    public float groundCheckOffset = 0.05f;
    public LayerMask groundMask = ~0;

    // ===== SMOOTHING =====
    [Header("Smoothing")]
    [Tooltip("Half-life (seconds) for velocity smoothing EMA. Lower = snappier, Higher = smoother.")]
    public float velocityHalfLife = 0.08f;
    [Tooltip("How quickly camera position follows target (SmoothDamp).")]
    public float camPosSmoothTime = 0.05f;
    [Tooltip("How quickly camera rotation reaches target (bigger = faster).")]
    public float camRotLerp = 16f;
    [Tooltip("Clamp max camera local move per frame to avoid micro-jitter.")]
    public float camMaxMovePerFrame = 0.06f;

    // ===== HEADBOB =====
    [Header("Headbob - Core")]
    public float bobAmplitude = 0.09f;    // meters
    public float bobFrequency = 1.8f;     // base Hz at normalized speed 1
    public float bobHorizontal = 0.025f;

    [Header("Headbob - Weight/Style")]
    public float heavyBias = 0.025f;      // extra downward bias
    public float strafeTiltDegrees = 3.0f;
    public float pitchTiltDegrees = 1.7f;

    [Header("Headbob - Idle/Breathing")]
    public bool enableIdleSway = true;
    public float idleAmplitude = 0.01f;
    public float idleFrequency = 0.6f;

    [Header("Headbob - Noise")]
    public float noiseAmplitude = 0.0025f;
    public float noiseFrequency = 9.0f;

    [Header("Headbob - Landing Visual")]
    public float landingKickAmount = 0.12f;
    public float landingKickDuration = 0.12f;
    public float landingMinSpeedVisual = 2.0f;

    [Header("FOOTSTEPS (Audio)")]
    [Tooltip("FMOD event for footsteps (one-shot). Will be fired twice per bob cycle (L/R).")]
    public EventReference footstepEvent;
    [Tooltip("Optional landing thud one-shot.")]
    public EventReference landEvent;

    [Tooltip("Minimum normalized speed (0..1) to produce steps.")]
    public float footstepSpeedThreshold = 0.08f;
    [Tooltip("Volume/intensity parameter name (optional). Set empty to skip.")]
    public string speedParamName = "Speed"; // float 0..1 (optional)
    [Tooltip("Surface parameter name (optional). Will pass a float value looked up from tag map.")]
    public string surfaceParamName = "Surface"; // float, optional

    [Header("Surface Mapping (tag -> value)")]
    public List<TagToSurface> surfaceMap = new List<TagToSurface>
    {
        new TagToSurface("Untagged", 0f),
        new TagToSurface("Concrete", 0f),
        new TagToSurface("Wood", 1f),
        new TagToSurface("Metal", 2f),
        new TagToSurface("Carpet", 3f),
        new TagToSurface("Grass", 4f)
    };

    [System.Serializable]
    public struct TagToSurface
    {
        public string tag;
        public float value;
        public TagToSurface(string t, float v) { tag = t; value = v; }
    }

    Rigidbody rb;
    CapsuleCollider capsule;

    float yaw, pitch;
    bool grounded;
    float timeSinceGrounded;

    // smoothed velocity
    Vector3 velEMA;

    // headbob state
    Vector3 camBaseLocalPos;
    Vector3 landingOffset;
    float bobPhase;
    float bobWeight;
    float lastYVel;
    Vector3 camPosVel;
    Quaternion camTargetRot;

    // step detection
    float lastSin;          // sign flip detection for L/R
    bool nextIsLeft = true; // alternate feet

    void Awake()
    {
        rb = GetComponent<Rigidbody>();
        capsule = GetComponent<CapsuleCollider>();

        if (!cam)
        {
            Camera c = GetComponentInChildren<Camera>();
            if (c) cam = c.transform;
        }

        rb.interpolation = RigidbodyInterpolation.Interpolate;
        rb.constraints = RigidbodyConstraints.FreezeRotation;
        rb.collisionDetectionMode = CollisionDetectionMode.Continuous;

        Vector3 e = transform.eulerAngles;
        yaw = e.y;
        if (cam)
        {
            pitch = cam.localEulerAngles.x;
            if (pitch > 180f) pitch -= 360f;
            camBaseLocalPos = cam.localPosition;
            camTargetRot = Quaternion.Euler(pitch, 0f, 0f);
        }

        if (lockCursor)
        {
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        velEMA = Vector3.zero;
        lastSin = 0f;
    }

    void Update()
    {
        // Look (logic only; visual apply in LateUpdate)
        Vector2 look = ReadLookInput();
        yaw += look.x * mouseSensitivity;
        pitch -= look.y * mouseSensitivity;
        pitch = Mathf.Clamp(pitch, -maxPitch, maxPitch);
        transform.rotation = Quaternion.Euler(0f, yaw, 0f);

        timeSinceGrounded += Time.deltaTime;

        // Jump
        if (enableJump && grounded && timeSinceGrounded <= coyoteTime && ReadJumpDown())
        {
            var v = rb.linearVelocity; v.y = 0f;
            rb.linearVelocity = v;
            rb.AddForce(Vector3.up * jumpForce, ForceMode.VelocityChange);
            grounded = false;
        }
    }

    void FixedUpdate()
    {
        // Ground check
        grounded = CheckGrounded(out Vector3 groundNormal);
        if (grounded) timeSinceGrounded = 0f;

        // Movement
        Vector2 move = ReadMoveInput();
        Vector3 wishDir = (transform.right * move.x + transform.forward * move.y);
        if (wishDir.sqrMagnitude > 1f) wishDir.Normalize();

        float control = grounded ? 1f : airControl;
        Vector3 targetVel = wishDir * walkSpeed;

        Vector3 currentVel = rb.linearVelocity;
        Vector3 horizontalVel = new Vector3(currentVel.x, 0f, currentVel.z);
        Vector3 desiredChange = (targetVel - horizontalVel) * control;
        Vector3 accel = Vector3.ClampMagnitude(desiredChange * acceleration, acceleration);
        rb.AddForce(new Vector3(accel.x, 0f, accel.z), ForceMode.Acceleration);

        // Stick to ground / extra gravity
        if (grounded) rb.AddForce(-groundNormal * gravityExtra, ForceMode.Acceleration);
        else rb.AddForce(Physics.gravity * 0.5f, ForceMode.Acceleration);

        // Smooth velocity (EMA)
        float dt = Time.fixedDeltaTime;
        float k = Mathf.Exp(-Mathf.Log(2f) * dt / Mathf.Max(0.0001f, velocityHalfLife));
        velEMA = Vector3.Lerp(rb.linearVelocity, velEMA, k);
    }

    void LateUpdate()
    {
        if (!cam) return;

        float dt = Mathf.Max(Time.deltaTime, 0.0001f);

        // ===== BOB using smoothed planar speed =====
        Vector2 vXZ = new Vector2(velEMA.x, velEMA.z);
        float speed = vXZ.magnitude;
        float norm = Mathf.Clamp01(speed / Mathf.Max(0.1f, walkSpeed));

        // blend bob in/out
        float targetWeight = grounded ? norm : 0f;
        if (enableIdleSway && targetWeight < 0.05f) targetWeight = 0.05f;
        bobWeight = Mathf.Lerp(bobWeight, targetWeight, 1f - Mathf.Exp(-10f * dt));

        // cadence scales with norm (slower when creeping)
        float freq = Mathf.Lerp(0.85f, bobFrequency, norm);
        bobPhase += (freq * 2f * Mathf.PI) * dt * Mathf.Lerp(0.25f, 1.1f, norm);

        float sin = Mathf.Sin(bobPhase);
        float cos = Mathf.Cos(bobPhase);

        // --- STEP EVENTS (audio) ---
        // We trigger a step each time the sign of sin flips (two per cycle), but only when moving and grounded.
        if (grounded && norm >= footstepSpeedThreshold && bobWeight > 0.04f)
        {
            // sign change?
            if (Mathf.Sign(sin) != Mathf.Sign(lastSin))
            {
                bool isLeft = nextIsLeft;
                nextIsLeft = !nextIsLeft; // alternate

                FireFootstep(isLeft, norm);
            }
        }
        lastSin = sin;

        // Vertical bob with heavy downward bias
        float yBob = sin * bobAmplitude * bobWeight - Mathf.Abs(sin) * heavyBias * bobWeight;
        float xBob = cos * bobHorizontal * bobWeight;

        // Idle breathing
        if (enableIdleSway && bobWeight <= 0.06f)
        {
            float idleT = Time.time * (idleFrequency * 2f * Mathf.PI);
            yBob += Mathf.Sin(idleT) * idleAmplitude;
            xBob += Mathf.Cos(idleT) * (idleAmplitude * 0.6f);
        }

        // Micro noise
        float n = Mathf.PerlinNoise(Time.time * noiseFrequency, 0.37f) - 0.5f;
        yBob += n * noiseAmplitude;
        xBob += n * (noiseAmplitude * 0.6f);

        // Apply position
        Vector3 targetLocalPos = camBaseLocalPos + new Vector3(xBob, yBob, 0f) + landingOffset;
        Vector3 delta = targetLocalPos - cam.localPosition;
        if (delta.magnitude > camMaxMovePerFrame)
            targetLocalPos = cam.localPosition + delta.normalized * camMaxMovePerFrame;

        cam.localPosition = Vector3.SmoothDamp(cam.localPosition, targetLocalPos, ref camPosVel, camPosSmoothTime);

        // Tilt
        float strafe = (speed > 0.0001f) ? Vector3.Dot(velEMA.normalized, transform.right) : 0f;
        float forward = (speed > 0.0001f) ? Vector3.Dot(velEMA.normalized, transform.forward) : 0f;

        float targetRoll = -strafe * strafeTiltDegrees * bobWeight;
        float targetPitch = -Mathf.Sign(forward) * pitchTiltDegrees * bobWeight;

        camTargetRot = Quaternion.Euler(pitch, 0f, 0f) * Quaternion.Euler(targetPitch, 0f, targetRoll);
        cam.localRotation = Quaternion.Slerp(cam.localRotation, camTargetRot, 1f - Mathf.Exp(-camRotLerp * dt));

        // Visual landing dip + landing sound
        if (!grounded && IsAboutToLand())
        {
            float impact = Mathf.Max(0f, -lastYVel) / Mathf.Max(0.01f, Physics.gravity.magnitude);
            if (impact * walkSpeed > landingMinSpeedVisual)
            {
                StartLandingKick(Mathf.Clamp01(impact) * landingKickAmount);
                FireLanding(impact);
            }
        }

        lastYVel = rb.linearVelocity.y; // sample after interpolation
    }

    // === Audio ===
    void FireFootstep(bool left, float normSpeed)
    {
        if (!RuntimeManager.IsInitialized || !footstepEvent.IsNull) { }
        if (AudioManager.instance == null || footstepEvent.IsNull) return;

        // ground probe for 3D position + surface
        Vector3 pos = GetFootprintPosition(out Collider colHit);

        // Collect parameters
        var paramList = new List<(string name, float value)>();

        // Speed/intensity 0..1
        if (!string.IsNullOrEmpty(speedParamName))
            paramList.Add((speedParamName, Mathf.Clamp01(normSpeed)));

        // Surface from tag
        if (!string.IsNullOrEmpty(surfaceParamName))
        {
            float surfaceValue = 0f;
            if (colHit != null) surfaceValue = LookupSurfaceFromTag(colHit.tag);
            paramList.Add((surfaceParamName, surfaceValue));
        }

        // Optional: 0 for Right, 1 for Left (if you use a "Foot" param). Commented out by default.
        // paramList.Add(("Foot", left ? 1f : 0f));

        AudioManager.instance.PlayOneShotWithParameters(footstepEvent, pos, paramList.ToArray());
    }

    void FireLanding(float impact)
    {
        if (AudioManager.instance == null || landEvent.IsNull) return;

        Vector3 pos = GetFootprintPosition(out _);
        var list = new List<(string name, float value)>();
        if (!string.IsNullOrEmpty(speedParamName))
            list.Add((speedParamName, Mathf.Clamp01(impact)));
        AudioManager.instance.PlayOneShotWithParameters(landEvent, pos, list.ToArray());
    }

    Vector3 GetFootprintPosition(out Collider hitCol)
    {
        hitCol = null;
        // Cast from mid-body downward to the ground
        Vector3 origin = transform.position + Vector3.up * (capsule.height * 0.5f);
        float maxDist = (capsule.height * 0.5f) + 0.6f;
        if (Physics.SphereCast(origin, capsule.radius * 0.9f, Vector3.down, out RaycastHit hit, maxDist, groundMask, QueryTriggerInteraction.Ignore))
        {
            hitCol = hit.collider;
            return hit.point + Vector3.up * 0.02f; // tiny lift to avoid z-fight with ground
        }
        // Fallback near feet
        return transform.position + Vector3.down * ((capsule.height * 0.5f) - capsule.radius + 0.05f);
    }

    float LookupSurfaceFromTag(string tag)
    {
        for (int i = 0; i < surfaceMap.Count; i++)
            if (surfaceMap[i].tag == tag) return surfaceMap[i].value;
        return 0f; // default
    }

    // === Grounding / landing visuals ===
    bool CheckGrounded(out Vector3 groundNormal)
    {
        groundNormal = Vector3.up;

        float radius = Mathf.Max(0.01f, capsule.radius - 0.01f);
        Vector3 origin = transform.position + Vector3.up * (capsule.radius);
        float castDist = (capsule.height * 0.5f - capsule.radius) + groundCheckOffset;

        if (Physics.SphereCast(origin, radius, Vector3.down, out RaycastHit hit, castDist + groundCheckRadius, groundMask, QueryTriggerInteraction.Ignore))
        {
            float angle = Vector3.Angle(hit.normal, Vector3.up);
            groundNormal = hit.normal;
            return angle <= maxGroundAngle;
        }

        Vector3 foot = transform.position + Vector3.down * ((capsule.height * 0.5f) - capsule.radius + groundCheckOffset);
        return Physics.CheckSphere(foot, groundCheckRadius, groundMask, QueryTriggerInteraction.Ignore);
    }

    bool IsAboutToLand()
    {
        if (grounded) return false;
        Ray ray = new Ray(transform.position + Vector3.up * (capsule.height * 0.5f), Vector3.down);
        float maxDist = (capsule.height * 0.5f) + 0.2f;
        if (Physics.SphereCast(ray, capsule.radius * 0.95f, out RaycastHit hit, maxDist, groundMask, QueryTriggerInteraction.Ignore))
        {
            return rb.linearVelocity.y < -0.1f && hit.distance < (capsule.height * 0.5f + 0.08f);
        }
        return false;
    }

    void StartLandingKick(float amount)
    {
        StopAllCoroutines();
        StartCoroutine(LandingKickCoroutine(amount));
    }

    System.Collections.IEnumerator LandingKickCoroutine(float amt)
    {
        float t = 0f;
        while (t < landingKickDuration)
        {
            t += Time.deltaTime;
            float a = 1f - (t / landingKickDuration);
            landingOffset = Vector3.down * (amt * a * a);
            yield return null;
        }
        landingOffset = Vector3.zero;
    }

    // === Input helpers ===
    Vector2 ReadMoveInput()
    {
#if ENABLE_INPUT_SYSTEM
        Vector2 v = Vector2.zero;
        try
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            if (kb != null)
            {
                if (kb.aKey.isPressed) v.x -= 1f;
                if (kb.dKey.isPressed) v.x += 1f;
                if (kb.wKey.isPressed) v.y += 1f;
                if (kb.sKey.isPressed) v.y -= 1f;
            }
            if (UnityEngine.InputSystem.Gamepad.current != null)
            {
                Vector2 stick = UnityEngine.InputSystem.Gamepad.current.leftStick.ReadValue();
                if (stick.sqrMagnitude > v.sqrMagnitude) v = stick;
            }
        }
        catch { }
        if (v.sqrMagnitude > 1f) v.Normalize();
        return v;
#else
        return new Vector2(Input.GetAxisRaw("Horizontal"), Input.GetAxisRaw("Vertical")).normalized;
#endif
    }

    Vector2 ReadLookInput()
    {
#if ENABLE_INPUT_SYSTEM
        Vector2 look = Vector2.zero;
        try
        {
            if (UnityEngine.InputSystem.Mouse.current != null)
                look = UnityEngine.InputSystem.Mouse.current.delta.ReadValue();
            if (UnityEngine.InputSystem.Gamepad.current != null)
                look += UnityEngine.InputSystem.Gamepad.current.rightStick.ReadValue() * 10f;
        }
        catch { }
        return look;
#else
        return new Vector2(Input.GetAxisRaw("Mouse X"), Input.GetAxisRaw("Mouse Y"));
#endif
    }

    bool ReadJumpDown()
    {
#if ENABLE_INPUT_SYSTEM
        try
        {
            if (UnityEngine.InputSystem.Keyboard.current != null)
                return UnityEngine.InputSystem.Keyboard.current.spaceKey.wasPressedThisFrame;
            if (UnityEngine.InputSystem.Gamepad.current != null)
                return UnityEngine.InputSystem.Gamepad.current.buttonSouth.wasPressedThisFrame;
        }
        catch { }
        return false;
#else
        return Input.GetButtonDown("Jump");
#endif
    }

    // Utilities
    public void ReanchorCameraBase()
    {
        if (cam) camBaseLocalPos = cam.localPosition;
    }

    public void SetCursorLocked(bool locked)
    {
        lockCursor = locked;
        Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
        Cursor.visible = !locked;
    }
}
