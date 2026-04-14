using System.Collections;
using UnityEngine;

/// <summary>
/// Scripted explainer beat: flat potential sheet → ball drops → ball rolls while
/// LearningImprint digs grooves into the field (PotentialSurface mesh updates from the same field).
/// </summary>
[DefaultExecutionOrder(-200)]
public class ExplainerSequenceController : MonoBehaviour
{
    public PotentialSurface surface;
    public StatePointController ball;
    public LearningImprint imprint;
    public ExternalForceSource magnet;

    [Header("Setup")]
    [Tooltip("Clears learned wells when entering Play mode.")]
    public bool clearImprintOnPlay = true;
    [Tooltip("Turns off ball CSV logging for this take.")]
    public bool disableBallCsvForSequence = true;
    [Tooltip("Hides LearningImprint's large LEARNING ON/OFF HUD during Play.")]
    public bool hideLearningHud = true;

    [Header("Timing")]
    public float holdFlatDuration = 1.75f;
    [Tooltip("Lerp heightScale from 0 to scene value before the ball drops. Use when you have non-flat attractors and want them to fade in first.")]
    public bool revealLandscapeBeforeDrop = false;
    public float landscapeRevealDuration = 1.25f;
    public float dropHeightAboveSurface = 6f;
    public float ballDropDuration = 1.35f;
    public float delayAfterLanding = 0.75f;
    [Tooltip("How long the magnet icon waits after landing before appearing.")]
    public float magnetAppearDelay = 0.25f;
    [Tooltip("How long imprinting stays on while the ball moves.")]
    public float carvingDuration = 18f;
    [Tooltip("If revealLandscapeBeforeDrop is false, heightScale ramps from 0 during this part of the carve so grooves grow out of a flat sheet.")]
    public bool rampHeightScaleDuringCarving = true;
    public float rampDuringCarvingDuration = 4f;
    [Tooltip("Magnet icon fade-in duration (seconds). If 0, magnet icon snaps on.")]
    public float magnetVisualFadeInDuration = 0.8f;
    [Tooltip("After the magnet icon appears, magnetic pull ramps from 0 to full over this many seconds.")]
    public float magnetForceFadeInDuration = 2.25f;

    [Header("Motion")]
    public Vector3 velocityKickXZ = new Vector3(2.4f, 0f, 1.05f);
    [Tooltip("If true, hides the magnet icon until it appears.")]
    public bool hideMagnetIconAtStart = true;
    [Tooltip("If true, fades the magnet icon by material color alpha (requires a shader that respects alpha).")]
    public bool fadeMagnetIconAlpha = false;

    float _savedHeightScale;
    float _savedMagnetForce;
    Color _magnetVisualBaseColor = Color.white;
    bool _magnetVisualCapturesColor;
    Renderer _magnetVisualRenderer;
    SphereCollider _ballCollider;

    void Awake()
    {
        if (surface != null)
            _savedHeightScale = surface.heightScale;

        if (ball != null)
        {
            _ballCollider = ball.GetComponent<SphereCollider>();
            ball.enabled = false;
            if (disableBallCsvForSequence)
                ball.logToCSV = false;
        }

        if (imprint != null)
        {
            imprint.SetLearningEnabled(false);
            imprint.ClearLearningWindows();
            if (hideLearningHud)
                imprint.hideRuntimeHud = true;
            if (clearImprintOnPlay)
                imprint.ClearImprint();
        }

        if (surface != null)
        {
            if (revealLandscapeBeforeDrop || (rampHeightScaleDuringCarving && !revealLandscapeBeforeDrop))
                surface.heightScale = 0f;
        }

        if (magnet != null && magnet.visual != null)
        {
            _magnetVisualRenderer = magnet.visual.GetComponentInChildren<Renderer>();
            if (fadeMagnetIconAlpha && _magnetVisualRenderer != null)
            {
                _magnetVisualCapturesColor = true;
                _magnetVisualRenderer.material = new Material(_magnetVisualRenderer.sharedMaterial);
                _magnetVisualBaseColor = _magnetVisualRenderer.material.color;
                var c = _magnetVisualBaseColor;
                c.a = 0f;
                _magnetVisualRenderer.material.color = c;
            }

            if (hideMagnetIconAtStart)
                magnet.visual.gameObject.SetActive(false);
        }
    }

    void Start()
    {
        StartCoroutine(RunSequence());
    }

    IEnumerator RunSequence()
    {
        if (magnet != null)
        {
            _savedMagnetForce = magnet.forceStrength;
            magnet.SetForceStrengthOnly(0f);
        }

        yield return new WaitForSeconds(holdFlatDuration);

        if (revealLandscapeBeforeDrop && surface != null)
            yield return AnimateHeightScale(0f, _savedHeightScale, landscapeRevealDuration);

        if (ball != null && surface != null && _ballCollider != null)
        {
            Vector3 p = ball.transform.position;
            float rWorld = WorldSphereRadius(_ballCollider);
            float groundY = surface.SampleWorldHeight(new Vector3(p.x, 0f, p.z));
            float startY = groundY + rWorld + dropHeightAboveSurface;
            Vector3 start = new Vector3(p.x, startY, p.z);
            Vector3 end = new Vector3(p.x, groundY + rWorld, p.z);

            float t = 0f;
            while (t < ballDropDuration)
            {
                t += Time.deltaTime;
                float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / ballDropDuration));
                ball.transform.position = Vector3.Lerp(start, end, u);
                yield return null;
            }

            ball.transform.position = end;
        }

        // Pause on the landed ball before the magnet appears.
        yield return new WaitForSeconds(delayAfterLanding);

        // Magnet appears (icon only).
        if (magnet != null && magnet.visual != null)
        {
            yield return new WaitForSeconds(magnetAppearDelay);
            magnet.visual.gameObject.SetActive(true);

            if (fadeMagnetIconAlpha && magnetVisualFadeInDuration > 1e-4f)
                yield return FadeMagnetIconIn();
        }

        // Now start the dynamics that produce carving.
        if (ball != null)
            ball.enabled = true;

        yield return null;

        if (ball != null)
            ball.AddVelocityXZ(velocityKickXZ);

        if (magnet != null && magnetForceFadeInDuration > 1e-4f)
            yield return FadeMagnetForceIn();
        else if (magnet != null)
            magnet.SetForceStrengthOnly(_savedMagnetForce);

        if (imprint != null)
            imprint.SetLearningEnabled(true);

        float carveT = 0f;
        while (carveT < carvingDuration)
        {
            carveT += Time.deltaTime;
            if (!revealLandscapeBeforeDrop && rampHeightScaleDuringCarving && surface != null && rampDuringCarvingDuration > 1e-4f)
            {
                float u = Mathf.Clamp01(carveT / rampDuringCarvingDuration);
                u = Mathf.SmoothStep(0f, 1f, u);
                surface.heightScale = Mathf.Lerp(0f, _savedHeightScale, u);
            }

            yield return null;
        }

        if (imprint != null)
            imprint.SetLearningEnabled(false);

        if (!revealLandscapeBeforeDrop && rampHeightScaleDuringCarving && surface != null)
            surface.heightScale = _savedHeightScale;
    }

    static float WorldSphereRadius(SphereCollider c)
    {
        Vector3 s = c.transform.lossyScale;
        float m = Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.y), Mathf.Abs(s.z));
        return c.radius * m;
    }

    IEnumerator AnimateHeightScale(float from, float to, float duration)
    {
        if (surface == null || duration <= 1e-4f)
        {
            if (surface != null)
                surface.heightScale = to;
            yield break;
        }

        float t = 0f;
        while (t < duration)
        {
            t += Time.deltaTime;
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(t / duration));
            surface.heightScale = Mathf.Lerp(from, to, u);
            yield return null;
        }

        surface.heightScale = to;
    }

    IEnumerator FadeMagnetIn()
    {
        // Kept for backward compatibility with older scenes; no longer used.
        yield return FadeMagnetForceIn();
    }

    IEnumerator FadeMagnetForceIn()
    {
        float dur = magnetForceFadeInDuration;
        float elapsed = 0f;
        while (elapsed < dur)
        {
            elapsed += Time.deltaTime;
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / dur));
            magnet.SetForceStrengthOnly(Mathf.Lerp(0f, _savedMagnetForce, u));
            yield return null;
        }
        magnet.SetForceStrengthOnly(_savedMagnetForce);
    }

    IEnumerator FadeMagnetIconIn()
    {
        if (!_magnetVisualCapturesColor || _magnetVisualRenderer == null)
            yield break;

        float dur = magnetVisualFadeInDuration;
        float elapsed = 0f;
        while (elapsed < dur)
        {
            elapsed += Time.deltaTime;
            float u = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / dur));
            var c = _magnetVisualBaseColor;
            c.a = _magnetVisualBaseColor.a * u;
            _magnetVisualRenderer.material.color = c;
            yield return null;
        }
        _magnetVisualRenderer.material.color = _magnetVisualBaseColor;
    }
}
