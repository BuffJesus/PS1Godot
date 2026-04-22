using Godot;

namespace PS1Godot.Effects;

// Editor-viewport "render at 320×240" preview via Godot's CompositorEffect
// API. Attaches to a Camera3D's Compositor. On each frame the viewport color
// buffer is downsampled into a scratch texture at PS1 resolution, then
// nearest-upsampled back — so the visible image behaves as if it was
// rendered at 320×240, blocky pixels and all.
//
// Experimental on 4.7-dev.5: CompositorEffect API is still evolving.
// If scene rendering glitches, delete the effect from the camera's
// Compositor to disable without uninstalling the plugin.
[Tool]
[GlobalClass]
[Icon("res://addons/ps1godot/icons/ps1_pixelize_effect.svg")]
public partial class PS1PixelizeEffect : CompositorEffect
{
    [Export]
    public Vector2I TargetResolution { get; set; } = new Vector2I(320, 240);

    private const string ShaderPath = "res://addons/ps1godot/effects/ps1_pixelize.glsl";

    private RenderingDevice? _rd;
    private Rid _shader;
    private Rid _pipeline;
    private Rid _scratch;
    private Vector2I _scratchSize;
    private bool _initFailed;

    public PS1PixelizeEffect()
    {
        EffectCallbackType = EffectCallbackTypeEnum.PostTransparent;
        AccessResolvedColor = true;
    }

    private bool EnsureInitialized()
    {
        if (_initFailed) return false;
        if (_pipeline.IsValid) return true;

        _rd = RenderingServer.GetRenderingDevice();
        if (_rd == null) { _initFailed = true; return false; }

        var shaderFile = ResourceLoader.Load<RDShaderFile>(ShaderPath);
        if (shaderFile == null)
        {
            GD.PushError($"[PS1Godot] Could not load {ShaderPath}");
            _initFailed = true;
            return false;
        }

        var spirv = shaderFile.GetSpirV();
        _shader = _rd.ShaderCreateFromSpirV(spirv);
        if (!_shader.IsValid) { _initFailed = true; return false; }

        _pipeline = _rd.ComputePipelineCreate(_shader);
        return _pipeline.IsValid;
    }

    private Rid GetOrCreateScratch()
    {
        if (_rd == null) return default;
        if (_scratch.IsValid && _scratchSize == TargetResolution) return _scratch;
        if (_scratch.IsValid) _rd.FreeRid(_scratch);

        var fmt = new RDTextureFormat
        {
            Format = RenderingDevice.DataFormat.R16G16B16A16Sfloat,
            Width = (uint)TargetResolution.X,
            Height = (uint)TargetResolution.Y,
            Depth = 1,
            ArrayLayers = 1,
            Mipmaps = 1,
            TextureType = RenderingDevice.TextureType.Type2D,
            Samples = RenderingDevice.TextureSamples.Samples1,
            UsageBits = RenderingDevice.TextureUsageBits.StorageBit
                      | RenderingDevice.TextureUsageBits.CanCopyFromBit
                      | RenderingDevice.TextureUsageBits.CanCopyToBit,
        };
        _scratch = _rd.TextureCreate(fmt, new RDTextureView());
        _scratchSize = TargetResolution;
        return _scratch;
    }

    public override void _RenderCallback(int effectCallbackType, RenderData renderData)
    {
        if (!EnsureInitialized() || _rd == null) return;
        if (renderData.GetRenderSceneBuffers() is not RenderSceneBuffersRD sceneBuffers) return;

        var viewportSize = (Vector2I)sceneBuffers.GetInternalSize();
        if (viewportSize.X <= 0 || viewportSize.Y <= 0) return;

        var scratch = GetOrCreateScratch();
        if (!scratch.IsValid) return;

        uint viewCount = sceneBuffers.GetViewCount();
        for (uint view = 0; view < viewCount; view++)
        {
            var colorTex = sceneBuffers.GetColorLayer(view);
            // viewport → scratch (downsample)
            Dispatch(colorTex, scratch, viewportSize, TargetResolution);
            // scratch → viewport (nearest upsample)
            Dispatch(scratch, colorTex, TargetResolution, viewportSize);
        }
    }

    private void Dispatch(Rid src, Rid dst, Vector2I srcSize, Vector2I dstSize)
    {
        if (_rd == null) return;

        var srcUniform = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.Image,
            Binding = 0,
        };
        srcUniform.AddId(src);

        var dstUniform = new RDUniform
        {
            UniformType = RenderingDevice.UniformType.Image,
            Binding = 1,
        };
        dstUniform.AddId(dst);

        var uniformSet = UniformSetCacheRD.GetCache(_shader, 0,
            new Godot.Collections.Array<RDUniform> { srcUniform, dstUniform });

        // Push constant layout: (src_w, src_h, dst_w, dst_h) as 4×u32 = 16 bytes
        byte[] push = new byte[16];
        System.BitConverter.GetBytes((uint)srcSize.X).CopyTo(push, 0);
        System.BitConverter.GetBytes((uint)srcSize.Y).CopyTo(push, 4);
        System.BitConverter.GetBytes((uint)dstSize.X).CopyTo(push, 8);
        System.BitConverter.GetBytes((uint)dstSize.Y).CopyTo(push, 12);

        uint groupsX = (uint)((dstSize.X + 7) / 8);
        uint groupsY = (uint)((dstSize.Y + 7) / 8);

        var cl = _rd.ComputeListBegin();
        _rd.ComputeListBindComputePipeline(cl, _pipeline);
        _rd.ComputeListBindUniformSet(cl, uniformSet, 0);
        _rd.ComputeListSetPushConstant(cl, push, (uint)push.Length);
        _rd.ComputeListDispatch(cl, groupsX, groupsY, 1);
        _rd.ComputeListEnd();
    }

    public override void _Notification(int what)
    {
        if (what == NotificationPredelete && _rd != null)
        {
            if (_scratch.IsValid) _rd.FreeRid(_scratch);
            if (_pipeline.IsValid) _rd.FreeRid(_pipeline);
            if (_shader.IsValid) _rd.FreeRid(_shader);
        }
    }
}
