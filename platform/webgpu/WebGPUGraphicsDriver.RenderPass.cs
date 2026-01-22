//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Text;
using Silk.NET.WebGPU;
using WGPUTexture = Silk.NET.WebGPU.Texture;
using WGPUTextureFormat = Silk.NET.WebGPU.TextureFormat;

namespace NoZ.Platform.WebGPU;

public unsafe partial class WebGPUGraphicsDriver
{
    public void ResizeOffscreenTarget(int width, int height, int msaaSamples)
    {
        // Destroy existing offscreen resources
        DestroyOffscreenTarget();

        _offscreenWidth = width;
        _offscreenHeight = height;
        _msaaSamples = 1; // Force disable MSAA for now

        // Create single color texture (no MSAA, no depth for simplicity)
        var colorDesc = new TextureDescriptor
        {
            Size = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 },
            MipLevelCount = 1,
            SampleCount = 1,
            Dimension = TextureDimension.Dimension2D,
            Format = _surfaceFormat,
            Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
        };
        _offscreenMsaaTexture = _wgpu.DeviceCreateTexture(_device, &colorDesc);

        if (_offscreenMsaaTexture == null)
            throw new Exception("Failed to create offscreen color texture");

        _offscreenMsaaTextureView = _wgpu.TextureCreateView(_offscreenMsaaTexture, null);

        if (_offscreenMsaaTextureView == null)
            throw new Exception("Failed to create offscreen color texture view");

        Log.Info($"Created offscreen texture: {width}x{height}, format: {_surfaceFormat}");
        Log.Info($"  Texture: {(nint)_offscreenMsaaTexture:X}");
        Log.Info($"  View: {(nint)_offscreenMsaaTextureView:X}");

        // Use same texture as resolve target for composite
        _offscreenResolveTexture = _offscreenMsaaTexture;
        _offscreenResolveTextureView = _offscreenMsaaTextureView;

        // No depth buffer for now
        _offscreenDepthTexture = null;
        _offscreenDepthTextureView = null;
    }

    public void BeginScenePass(Color clearColor)
    {
        if (_currentRenderPass != null)
            throw new InvalidOperationException("BeginScenePass called while already in a render pass");

        // Validate state
        if (_commandEncoder == null)
            throw new InvalidOperationException("Command encoder is null - BeginFrame not called?");

        if (_currentSurfaceTexture == null)
            throw new InvalidOperationException("Surface texture is null - BeginFrame not called?");

        // Create view for current surface texture
        var surfaceView = _wgpu.TextureCreateView(_currentSurfaceTexture, null);

        if (surfaceView == null)
            throw new Exception("Failed to create surface texture view");

        Log.Info($"Got surface texture view: {(nint)surfaceView:X}");

        var colorAttachment = new RenderPassColorAttachment
        {
            View = surfaceView,
            ResolveTarget = null,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Silk.NET.WebGPU.Color
            {
                R = clearColor.R,
                G = clearColor.G,
                B = clearColor.B,
                A = clearColor.A
            }
        };

        // No depth attachment for now
        var desc = new RenderPassDescriptor
        {
            ColorAttachments = &colorAttachment,
            ColorAttachmentCount = 1,
            DepthStencilAttachment = null
        };

        Log.Info("About to call CommandEncoderBeginRenderPass...");
        _currentRenderPass = _wgpu.CommandEncoderBeginRenderPass(_commandEncoder, in desc);
        Log.Info($"[LIFECYCLE] BeginScenePass: RenderPass created: {(nint)_currentRenderPass:X}");

        _inRenderPass = true;

        // Set default viewport and scissor
        Log.Info($"Setting viewport: {_surfaceWidth}x{_surfaceHeight}");
        _wgpu.RenderPassEncoderSetViewport(_currentRenderPass, 0, 0, _surfaceWidth, _surfaceHeight, 0, 1);

        Log.Info("Setting scissor rect");
        _wgpu.RenderPassEncoderSetScissorRect(_currentRenderPass, 0, 0, (uint)_surfaceWidth, (uint)_surfaceHeight);

        Log.Info("BeginScenePass completed successfully!");

        // DON'T release the view yet - it's still being used by the render pass
        // We'll release it in EndScenePass
        // Store it so we can release it later
        _currentSurfaceView = surfaceView;
    }

    public void EndScenePass()
    {
        Log.Info("EndScenePass called");

        if (_currentRenderPass == null)
            throw new InvalidOperationException("EndScenePass called without matching BeginScenePass");

        Log.Info($"[LIFECYCLE] EndScenePass: About to end render pass: {(nint)_currentRenderPass:X}");
        _wgpu.RenderPassEncoderEnd(_currentRenderPass);

        Log.Info($"[LIFECYCLE] EndScenePass: Releasing render pass encoder: {(nint)_currentRenderPass:X}");
        _wgpu.RenderPassEncoderRelease(_currentRenderPass);
        Log.Info($"[LIFECYCLE] EndScenePass: Setting _currentRenderPass to null");
        _currentRenderPass = null;
        _inRenderPass = false;

        // Release the surface view
        if (_currentSurfaceView != null)
        {
            Log.Info("Releasing surface view...");
            _wgpu.TextureViewRelease(_currentSurfaceView);
            _currentSurfaceView = null;
        }

        // Release deferred bind groups
        if (_bindGroupsToRelease.Count > 0)
        {
            Log.Debug($"Releasing {_bindGroupsToRelease.Count} deferred bind groups");
            foreach (var bindGroup in _bindGroupsToRelease)
            {
                _wgpu.BindGroupRelease((BindGroup*)bindGroup);
            }
            _bindGroupsToRelease.Clear();
        }

        Log.Info("EndScenePass completed successfully!");
    }

    public void Composite(nuint compositeShader)
    {
        if (_currentRenderPass != null)
            throw new InvalidOperationException("Composite called while in a render pass");

        if (_currentSurfaceTexture == null)
            throw new InvalidOperationException("Surface texture is null - BeginFrame not called?");

        // Create view for current surface texture
        var swapChainView = _wgpu.TextureCreateView(_currentSurfaceTexture, null);

        if (swapChainView == null)
            throw new Exception("Failed to create surface texture view for composite");

        // Begin render pass to swap chain
        var colorAttachment = new RenderPassColorAttachment
        {
            View = swapChainView,
            LoadOp = LoadOp.Clear,
            StoreOp = StoreOp.Store,
            ClearValue = new Silk.NET.WebGPU.Color { R = 0, G = 0, B = 0, A = 1 }
        };

        var desc = new RenderPassDescriptor
        {
            ColorAttachmentCount = 1,
            ColorAttachments = &colorAttachment,
        };

        _currentRenderPass = _wgpu.CommandEncoderBeginRenderPass(_commandEncoder, &desc);
        Log.Info($"[LIFECYCLE] Composite: RenderPass created: {(nint)_currentRenderPass:X}");

        // Set viewport to full swap chain
        _wgpu.RenderPassEncoderSetViewport(_currentRenderPass, 0, 0, _surfaceWidth, _surfaceHeight, 0, 1);
        _wgpu.RenderPassEncoderSetScissorRect(_currentRenderPass, 0, 0, (uint)_surfaceWidth, (uint)_surfaceHeight);

        // Bind composite shader and render fullscreen quad
        BindShader(compositeShader);

        // Create temporary texture handle for offscreen resolve texture
        // This is a bit of a hack - we need to bind the resolve texture but it's not in our texture array
        // For now, we'll need to create a bind group manually for the composite pass
        CreateAndBindCompositeBindGroup(compositeShader);

        // Draw fullscreen quad (will need to be created separately)
        // For now, just end the pass - this will be completed when we add the fullscreen quad mesh

        Log.Info($"[LIFECYCLE] Composite: About to end render pass: {(nint)_currentRenderPass:X}");
        _wgpu.RenderPassEncoderEnd(_currentRenderPass);
        Log.Info($"[LIFECYCLE] Composite: Releasing render pass: {(nint)_currentRenderPass:X}");
        _wgpu.RenderPassEncoderRelease(_currentRenderPass);
        Log.Info($"[LIFECYCLE] Composite: Setting _currentRenderPass to null");
        _currentRenderPass = null;

        // Release deferred bind groups
        if (_bindGroupsToRelease.Count > 0)
        {
            Log.Debug($"Releasing {_bindGroupsToRelease.Count} deferred bind groups");
            foreach (var bindGroup in _bindGroupsToRelease)
            {
                _wgpu.BindGroupRelease((BindGroup*)bindGroup);
            }
            _bindGroupsToRelease.Clear();
        }

        // Release swap chain view
        _wgpu.TextureViewRelease(swapChainView);
    }

    private void CreateAndBindCompositeBindGroup(nuint shaderHandle)
    {
        ref var shader = ref _shaders[(int)shaderHandle];

        // Create sampler for composite texture
        var samplerDesc = new SamplerDescriptor
        {
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Linear,
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            LodMinClamp = 0.0f,
            LodMaxClamp = 32.0f,
            Compare = CompareFunction.Undefined,
            MaxAnisotropy = 1,
        };
        var sampler = _wgpu.DeviceCreateSampler(_device, &samplerDesc);

        // Build bind group entries for composite shader
        var entries = stackalloc BindGroupEntry[2];

        // Binding 0: Scene texture
        entries[0] = new BindGroupEntry
        {
            Binding = 0,
            TextureView = _offscreenResolveTextureView,
        };

        // Binding 1: Sampler
        entries[1] = new BindGroupEntry
        {
            Binding = 1,
            Sampler = sampler,
        };

        // Release old bind group if exists
        if (_currentBindGroup != null)
        {
            _wgpu.BindGroupRelease(_currentBindGroup);
            _currentBindGroup = null;
        }

        // Create bind group
        var desc = new BindGroupDescriptor
        {
            Layout = shader.BindGroupLayout0,
            EntryCount = 2,
            Entries = entries,
        };
        _currentBindGroup = _wgpu.DeviceCreateBindGroup(_device, &desc);

        // Bind to render pass
        _wgpu.RenderPassEncoderSetBindGroup(_currentRenderPass, 0, _currentBindGroup, 0, null);

        // Clean up sampler
        _wgpu.SamplerRelease(sampler);
    }
}
