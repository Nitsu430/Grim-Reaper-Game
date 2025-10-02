using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PS1PostFeature : ScriptableRendererFeature
{
    [System.Serializable]
    public class Settings
    {
        public Shader shader;
        [Range(0.1f, 1f)] public float resolutionScale = 0.5f;
        [Min(1)] public Vector3 paletteSteps = new Vector3(5, 5, 5);
        [Range(0f, 1f)] public float ditherStrength = 0.4f;
        [Range(0f, 2f)] public float jitterStrength = 0.25f;
        [Range(0f, 2f)] public float depthJitter = 0.6f;

        public Color fogColor = new Color(0.1f, 0.1f, 0.15f, 1f);
        public float fogStart = 12f;
        public float fogEnd = 40f;

        public RenderPassEvent injectionPoint = RenderPassEvent.AfterRenderingPostProcessing;
    }

    class PS1PostPass : ScriptableRenderPass
    {
        private Material _mat;
        private string _profilerTag;
        private RTHandle _temp;
        private Settings _settings;

        public PS1PostPass(string tag, Settings settings)
        {
            _profilerTag = tag;
            _settings = settings;
        }

        public void Setup(Material mat)
        {
            _mat = mat;
        }

        public override void OnCameraSetup(CommandBuffer cmd, ref RenderingData renderingData)
        {
            RenderTextureDescriptor desc = renderingData.cameraData.cameraTargetDescriptor;
            desc.depthBufferBits = 0;
            RenderingUtils.ReAllocateHandleIfNeeded(ref _temp, desc, FilterMode.Point, name: "_PS1Temp");
        }

        public override void Execute(ScriptableRenderContext context, ref RenderingData renderingData)
        {
            if (_mat == null) return;

            var cmd = CommandBufferPool.Get(_profilerTag);

            // Push uniforms
            _mat.SetVector("_PaletteSteps", _settings.paletteSteps);
            _mat.SetFloat("_TargetScale", _settings.resolutionScale);
            _mat.SetFloat("_DitherStrength", _settings.ditherStrength);
            _mat.SetFloat("_JitterStrength", _settings.jitterStrength);
            _mat.SetFloat("_ZJitter", _settings.depthJitter);

            _mat.SetColor("_FogColor", _settings.fogColor);
            _mat.SetFloat("_FogStart", Mathf.Max(0.001f, _settings.fogStart));
            _mat.SetFloat("_FogEnd", Mathf.Max(_settings.fogStart + 0.001f, _settings.fogEnd));

            var src = renderingData.cameraData.renderer.cameraColorTargetHandle;
            Blitter.BlitCameraTexture(cmd, src, _temp, _mat, 0);
            Blitter.BlitCameraTexture(cmd, _temp, src);

            context.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);
        }

        public override void OnCameraCleanup(CommandBuffer cmd)
        {
            
        }
    }

    public Settings settings = new Settings();
    private PS1PostPass _pass;
    private Material _mat;

    public override void Create()
    {
        if (settings.shader == null)
            settings.shader = Shader.Find("Hidden/PSXPost");

        if (settings.shader != null && (_mat == null || _mat.shader != settings.shader))
            _mat = CoreUtils.CreateEngineMaterial(settings.shader);

        _pass = new PS1PostPass("PS1 Post", settings)
        {
            renderPassEvent = settings.injectionPoint
        };
    }

    public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
    {
        if (_mat == null) return;
        _pass.Setup(_mat);
        renderer.EnqueuePass(_pass);
    }

    protected override void Dispose(bool disposing)
    {
        CoreUtils.Destroy(_mat);
    }
}
