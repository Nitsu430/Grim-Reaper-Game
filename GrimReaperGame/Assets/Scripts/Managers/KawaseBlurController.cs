using UnityEngine;
using UnityEngine.UI;

public class UIKawaseBlurController : MonoBehaviour
{
    [SerializeField] RawImage target;              // the BlurImage RawImage
    [SerializeField] string radiusProp = "_BlurRadius";
    [SerializeField] string itersProp = "_Iterations";
    [Range(0, 8)] public float radius = 0f;
    [Range(1, 6)] public int iterations = 3;

    Material _mat;

    void Awake()
    {
        if (!target) target = GetComponent<RawImage>();
        if (target && target.material) _mat = target.material;
        Apply();
    }

    public void SetRadius(float r) { radius = r; Apply(); }
    public void SetIterations(float it) { iterations = Mathf.Clamp(Mathf.RoundToInt(it), 1, 6); Apply(); }

    public void Apply()
    {
        if (_mat == null) return;
        _mat.SetFloat(radiusProp, radius);
        _mat.SetFloat(itersProp, iterations);
    }

    // Optional: quick coroutine tween
    public void TweenRadius(float to, float duration)
    {
        StopAllCoroutines();
        StartCoroutine(TweenCR(to, duration));
    }

    System.Collections.IEnumerator TweenCR(float to, float dur)
    {
        float from = radius;
        float t = 0f;
        while (t < dur)
        {
            Debug.Log(Time.unscaledDeltaTime);
            t += Time.unscaledDeltaTime;
            SetRadius(Mathf.Lerp(from, to, Mathf.SmoothStep(0, 1, t / dur)));
            yield return null;
        }
        SetRadius(to);
    }
}
