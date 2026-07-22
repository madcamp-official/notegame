using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using Random = UnityEngine.Random;

/// <summary>
/// Fair, gravity-free D20 roll for a textured icosahedron rendered in front of the camera.
///
/// Core idea ("result first, animation second"):
///   1. The outcome is drawn uniformly from the 20 faces BEFORE any animation starts,
///      so fairness is guaranteed by the RNG, not by physics.
///   2. The spin is then animated *backwards from the target*: we compute the exact
///      rotation that presents the chosen face to the camera, add several full
///      revolutions on top of it, and play the whole trajectory with an exponentially
///      decaying angular velocity  ω(t) = ω0 · e^(−k·t)  — i.e. smooth viscous
///      friction — so the die visibly slows down and terminates *exactly* on the
///      chosen face, with no snap and no gravity involved.
/// </summary>
[RequireComponent(typeof(MeshFilter))]
public class IcosahedronDice : MonoBehaviour
{
    private const float VisualSpinSpeedMultiplier = 0.2f;

    [Header("Roll dynamics")]
    [Tooltip("Initial angular speed at power = 0, degrees/second.")]
    [SerializeField] private float minStartSpeed = 1080f;
    [Tooltip("Initial angular speed at power = 1, degrees/second.")]
    [SerializeField] private float maxStartSpeed = 2400f;
    [Tooltip("Friction coefficient k in ω(t) = ω0·e^(−k·t). Higher = stops sooner.")]
    [SerializeField] private float friction = 2.2f;
    [Tooltip("Remaining angle (deg) below which the roll is considered finished.")]
    [SerializeField] private float stopEpsilon = 0.25f;
    [Tooltip("0 = perfectly clean single-axis spin, ~0.3 = slight tumbling wobble that fades out with the spin.")]
    [Range(0f, 1f)]
    [SerializeField] private float wobble = 0.3f;
    [Tooltip("Time used to settle on the authoritative result after a pending server response arrives.")]
    [SerializeField] private float responseSettleDuration = 0.7f;

    [Header("Facing")]
    [Tooltip("Camera the winning face should end up pointing at. Defaults to Camera.main.")]
    [SerializeField] private Camera targetCamera;
    [Tooltip("Randomize the final twist around the view axis so the die doesn't always land in the same pose.")]
    [SerializeField] private bool randomFinalTwist = true;

    /// <summary>Raised with the rolled value (1..20) when the die comes to rest.</summary>
    public event Action<int> RollFinished;

    public bool IsRolling { get; private set; }
    public int LastResult { get; private set; }

    // Local-space outward unit normal of each face. Index i corresponds to value i+1.
    // IMPORTANT: reorder (or expose) this array so index matches the number painted
    // on your texture for that face — see ExtractFaceNormals().
    private Vector3[] faceNormals;
    private Coroutine rollCoroutine;
    private int pendingResult;

    private bool HasValidFaceNormals => faceNormals != null && faceNormals.Length == 20;

    private void Awake()
    {
        ExtractFaceNormals();
    }

    /// <summary>Resolves the facing camera lazily so a late-appearing MainCamera is still picked up.</summary>
    private Camera ActiveCamera
    {
        get
        {
            if (targetCamera == null) targetCamera = Camera.main;
            if (targetCamera == null)
                throw new InvalidOperationException(
                    "IcosahedronDice needs a Target Camera assigned (or a Camera tagged MainCamera in the scene).");
            return targetCamera;
        }
    }

    /// <summary>Overrides the camera used to orient the authoritative result face.</summary>
    public void SetTargetCamera(Camera camera)
    {
        targetCamera = camera;
    }

    /// <summary>
    /// Builds the list of 20 face normals directly from the mesh, merging coplanar
    /// triangles (some icosahedron meshes are unwrapped with more than one triangle
    /// or duplicated vertices per face).
    /// </summary>
    private void ExtractFaceNormals()
    {
        MeshFilter filter = GetComponent<MeshFilter>();
        if (filter == null || filter.sharedMesh == null)
        {
            MeshFilter[] filters = GetComponentsInChildren<MeshFilter>(true);
            for (int i = 0; i < filters.Length; i++)
                if (filters[i] != null && filters[i].sharedMesh != null)
                {
                    filter = filters[i];
                    break;
                }
        }
        if (filter == null || filter.sharedMesh == null)
            throw new InvalidOperationException("IcosahedronDice needs an icosahedron mesh on itself or a child.");

        Mesh mesh = filter.sharedMesh;
        if (!mesh.isReadable)
        {
            faceNormals = Array.Empty<Vector3>();
            Debug.LogError(
                $"IcosahedronDice cannot read mesh '{mesh.name}'. Enable Read/Write on the model importer.",
                this);
            return;
        }

        Vector3[] verts = mesh.vertices;
        int[] tris = mesh.triangles;

        var normals = new List<Vector3>(20);
        for (int t = 0; t < tris.Length; t += 3)
        {
            Vector3 n = Vector3.Cross(
                verts[tris[t + 1]] - verts[tris[t]],
                verts[tris[t + 2]] - verts[tris[t]]).normalized;

            bool known = false;
            for (int i = 0; i < normals.Count; i++)
            {
                if (Vector3.Dot(normals[i], n) > 0.999f) { known = true; break; }
            }
            if (!known) normals.Add(n);
        }

        faceNormals = normals.ToArray();
        if (!HasValidFaceNormals)
            Debug.LogError(
                $"IcosahedronDice: expected 20 distinct faces, found {faceNormals.Length}. " +
                "Check the mesh (it must be a flat-shaded icosahedron).",
                this);
    }

    private bool EnsureFaceNormals()
    {
        if (!HasValidFaceNormals)
            ExtractFaceNormals();
        return HasValidFaceNormals;
    }

    /// <summary>
    /// Rolls the die. <paramref name="power01"/> in [0,1] scales the initial spin
    /// speed (e.g. from swipe velocity). Returns the value (1..20) that WILL be shown,
    /// decided uniformly at random before the animation begins.
    /// </summary>
    public int Roll(float power01 = 1f)
    {
        if (!EnsureFaceNormals()) return 0;
        // --- Fairness lives entirely on this line: uniform over 20 faces. ---
        int faceIndex = Random.Range(0, faceNormals.Length);
        return RollTo(faceIndex + 1, power01);
    }

    /// <summary>
    /// Plays the spin animation and lands on <paramref name="result"/> (1..20) instead of
    /// drawing a new random value. Use this to visually present a roll whose outcome was
    /// already decided elsewhere (e.g. by the action-resolution script's own D20 source),
    /// so the die and the mechanical result never disagree.
    /// </summary>
    public int RollTo(int result, float power01 = 1f)
    {
        if (!EnsureFaceNormals()) return 0;
        if (IsRolling) return LastResult;
        if (result < 1 || result > faceNormals.Length)
            throw new ArgumentOutOfRangeException(nameof(result), result,
                $"Expected a value between 1 and {faceNormals.Length}.");

        int faceIndex = result - 1;
        LastResult = result;
        rollCoroutine = StartCoroutine(RollRoutine(faceIndex, Mathf.Clamp01(power01)));
        return LastResult;
    }

    /// <summary>
    /// Starts an unresolved visual roll. This is used while an authoritative request is
    /// waiting for its narrative response; no mechanical value is drawn on the client.
    /// Call <see cref="ResolveTo"/> when the server-provided D20 arrives.
    /// </summary>
    public void BeginPendingRoll(float power01 = 1f)
    {
        CancelRoll();
        if (!EnsureFaceNormals()) return;
        pendingResult = 0;
        LastResult = 0;
        rollCoroutine = StartCoroutine(PendingRollRoutine(Mathf.Clamp01(power01)));
    }

    /// <summary>Finishes a pending visual roll on the already-authoritative result.</summary>
    public void ResolveTo(int result)
    {
        if (!EnsureFaceNormals())
        {
            CancelRoll();
            return;
        }
        if (result < 1 || result > faceNormals.Length)
            throw new ArgumentOutOfRangeException(nameof(result), result,
                $"Expected a value between 1 and {faceNormals.Length}.");
        LastResult = result;
        pendingResult = result;
        if (!IsRolling)
            RollTo(result);
    }

    public void CancelRoll()
    {
        if (rollCoroutine != null)
            StopCoroutine(rollCoroutine);
        rollCoroutine = null;
        pendingResult = 0;
        IsRolling = false;
    }

    /// <summary>World rotation that presents faceNormals[faceIndex] to the camera.</summary>
    private Quaternion TargetRotationFor(int faceIndex)
    {
        Vector3 toCamera = -ActiveCamera.transform.forward; // face the screen head-on
        Quaternion align = Quaternion.FromToRotation(faceNormals[faceIndex], toCamera);

        if (randomFinalTwist)
            align = Quaternion.AngleAxis(Random.Range(0f, 360f), toCamera) * align;

        return align;
    }

    private IEnumerator RollRoutine(int faceIndex, float power01)
    {
        IsRolling = true;

        Quaternion startRot = transform.rotation;
        Quaternion targetRot = TargetRotationFor(faceIndex);

        // Rotation that takes us from the current pose to the target pose.
        Quaternion delta = targetRot * Quaternion.Inverse(startRot);
        delta.ToAngleAxis(out float baseAngle, out Vector3 axis);
        if (float.IsNaN(axis.x) || axis.sqrMagnitude < 1e-6f)
        {
            // Already aligned: spin around an arbitrary axis instead.
            axis = Random.onUnitSphere;
            baseAngle = 0f;
        }
        axis.Normalize();

        // With viscous friction, total travelled angle is Θ = ω0 / k.
        // Add whole extra revolutions so Θ matches the desired spin energy while the
        // trajectory still ends exactly on the target orientation.
        float w0 = Mathf.Lerp(minStartSpeed, maxStartSpeed, power01);
        float desiredTotal = w0 / friction;
        int extraTurns = Mathf.Max(1, Mathf.RoundToInt((desiredTotal - baseAngle) / 360f));
        float totalAngle = baseAngle + 360f * extraTurns;

        // Recompute k so ω(0) = w0 exactly for the chosen total angle.
        float k = w0 / totalAngle;

        // A perpendicular axis for the decaying wobble (purely cosmetic).
        Vector3 wobbleAxis = Vector3.Cross(axis, Random.onUnitSphere).normalized;

        float remaining = totalAngle;
        float time = 0f;

        while (remaining > stopEpsilon)
        {
            time += Time.deltaTime;

            // remaining(t) = Θ · e^(−k·t)  →  angular speed decays smoothly to zero.
            remaining = totalAngle * Mathf.Exp(-k * time);
            float travelled = totalAngle - remaining;

            Quaternion spin = Quaternion.AngleAxis(travelled, axis);

            // Wobble amplitude is proportional to remaining spin, so it vanishes
            // together with the main rotation and never disturbs the landing.
            float wobbleAmp = wobble * 15f * (remaining / totalAngle);
            Quaternion tumble = Quaternion.AngleAxis(
                wobbleAmp * Mathf.Sin(travelled * Mathf.Deg2Rad * 3f), wobbleAxis);

            transform.rotation = tumble * spin * startRot;
            yield return null;
        }

        // Sub-epsilon correction: imperceptible, guarantees a mathematically exact pose.
        transform.rotation = targetRot;

        IsRolling = false;
        rollCoroutine = null;
        RollFinished?.Invoke(LastResult);
    }

    private IEnumerator PendingRollRoutine(float power01)
    {
        IsRolling = true;
        float speed = Mathf.Lerp(minStartSpeed, maxStartSpeed, power01) * VisualSpinSpeedMultiplier;
        Vector3 primaryAxis = new Vector3(0.72f, 1f, 0.38f).normalized;
        Vector3 secondaryAxis = new Vector3(-0.42f, 0.26f, 1f).normalized;
        float elapsed = 0f;

        while (pendingResult == 0)
        {
            float delta = Time.unscaledDeltaTime;
            elapsed += delta;
            transform.Rotate(primaryAxis, speed * delta, Space.World);
            transform.Rotate(secondaryAxis, speed * wobble * 0.22f *
                (0.65f + 0.35f * Mathf.Sin(elapsed * 5f)) * delta, Space.World);
            yield return null;
        }

        Quaternion start = transform.rotation;
        Quaternion target = TargetRotationFor(pendingResult - 1);
        Vector3 settleAxis = new Vector3(0.37f, 0.81f, -0.45f).normalized;
        float duration = Mathf.Max(0.1f, responseSettleDuration);
        elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            Quaternion extraTurns = Quaternion.AngleAxis(
                (1f - eased) * 720f * VisualSpinSpeedMultiplier,
                settleAxis);
            transform.rotation = extraTurns * Quaternion.Slerp(start, target, eased);
            yield return null;
        }

        transform.rotation = target;
        pendingResult = 0;
        IsRolling = false;
        rollCoroutine = null;
        RollFinished?.Invoke(LastResult);
    }

    private void OnDisable()
    {
        CancelRoll();
    }

    /// <summary>
    /// Utility: which face currently points most directly at the camera.
    /// Handy to verify your texture's number-to-face mapping in the editor.
    /// </summary>
    public int GetFaceTowardCamera()
    {
        if (!EnsureFaceNormals()) return 0;
        Vector3 toCamera = -ActiveCamera.transform.forward;
        int best = 0;
        float bestDot = float.NegativeInfinity;
        for (int i = 0; i < faceNormals.Length; i++)
        {
            float d = Vector3.Dot(transform.rotation * faceNormals[i], toCamera);
            if (d > bestDot) { bestDot = d; best = i; }
        }
        return best + 1;
    }
}
