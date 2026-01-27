//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using Silk.NET.WebGPU;

namespace NoZ.Platform.WebGPU;

public unsafe partial class WebGPUGraphicsDriver
{
    public void ResizeOffscreenTarget(Vector2Int size, int msaaSamples)
    {
        if (_offscreenSize == size && _msaaSamples == msaaSamples && _offscreenMsaaTexture != null)
            return;

        DestroyOffscreenTarget();

        var width = size.X;
        var height = size.Y;
        _offscreenSize = size;
        _msaaSamples = Math.Max(1, msaaSamples);

        var viewDesc = new TextureViewDescriptor
        {
            Format = _surfaceFormat,
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All
        };

        if (_msaaSamples > 1)
        {
            // MSAA enabled: create separate MSAA and resolve textures
            var msaaDesc = new TextureDescriptor
            {
                Size = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 },
                MipLevelCount = 1,
                SampleCount = (uint)_msaaSamples,
                Dimension = TextureDimension.Dimension2D,
                Format = _surfaceFormat,
                Usage = TextureUsage.RenderAttachment,
            };
            _offscreenMsaaTexture = _wgpu.DeviceCreateTexture(_device, &msaaDesc);

            if (_offscreenMsaaTexture == null)
                throw new Exception("Failed to create MSAA texture");

            _offscreenMsaaTextureView = _wgpu.TextureCreateView(_offscreenMsaaTexture, &viewDesc);

            if (_offscreenMsaaTextureView == null)
                throw new Exception("Failed to create MSAA texture view");

            // Create resolve texture (single sample, for reading after resolve)
            var resolveDesc = new TextureDescriptor
            {
                Size = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 },
                MipLevelCount = 1,
                SampleCount = 1,
                Dimension = TextureDimension.Dimension2D,
                Format = _surfaceFormat,
                Usage = TextureUsage.RenderAttachment | TextureUsage.TextureBinding,
            };
            _offscreenResolveTexture = _wgpu.DeviceCreateTexture(_device, &resolveDesc);

            if (_offscreenResolveTexture == null)
                throw new Exception("Failed to create resolve texture");

            _offscreenResolveTextureView = _wgpu.TextureCreateView(_offscreenResolveTexture, &viewDesc);

            if (_offscreenResolveTextureView == null)
                throw new Exception("Failed to create resolve texture view");
        }
        else
        {
            // No MSAA: single texture for both render and read
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

            _offscreenMsaaTextureView = _wgpu.TextureCreateView(_offscreenMsaaTexture, &viewDesc);

            if (_offscreenMsaaTextureView == null)
                throw new Exception("Failed to create offscreen color texture view");

            _offscreenResolveTexture = _offscreenMsaaTexture;
            _offscreenResolveTextureView = _offscreenMsaaTextureView;
        }

        _offscreenDepthTexture = null;
        _offscreenDepthTextureView = null;
    }

    public void BeginScenePass(Color clearColor)
    {
        if (_currentRenderPass != null)
            throw new InvalidOperationException("BeginScenePass called while already in a render pass");

        if (_commandEncoder == null)
            throw new InvalidOperationException("Command encoder is null - BeginFrame not called?");

        if (_offscreenMsaaTextureView == null)
            throw new InvalidOperationException("Offscreen texture not initialized - call ResizeOffscreenTarget first");

        _state.CurrentPassSampleCount = _msaaSamples;

        var colorAttachment = new RenderPassColorAttachment
        {
            View = _offscreenMsaaTextureView,
            ResolveTarget = _msaaSamples > 1 ? _offscreenResolveTextureView : null,
            LoadOp = LoadOp.Clear,
            StoreOp = _msaaSamples > 1 ? StoreOp.Discard : StoreOp.Store,
            ClearValue = new Silk.NET.WebGPU.Color
            {
                R = clearColor.R,
                G = clearColor.G,
                B = clearColor.B,
                A = clearColor.A
            }
        };

        var desc = new RenderPassDescriptor
        {
            ColorAttachments = &colorAttachment,
            ColorAttachmentCount = 1,
            DepthStencilAttachment = null
        };

        _currentRenderPass = _wgpu.CommandEncoderBeginRenderPass(_commandEncoder, in desc);

        // Debug group for RenderDoc
        fixed (byte* label = "ScenePass\0"u8)
        {
            _wgpu.RenderPassEncoderPushDebugGroup(_currentRenderPass, label);
        }

        _wgpu.RenderPassEncoderSetViewport(_currentRenderPass, 0, 0, _surfaceWidth, _surfaceHeight, 0, 1);
        _wgpu.RenderPassEncoderSetScissorRect(_currentRenderPass, 0, 0, (uint)_surfaceWidth, (uint)_surfaceHeight);
    }

    public void EndScenePass()
    {
        if (_currentRenderPass == null)
            throw new InvalidOperationException("EndScenePass called without matching BeginScenePass");

        _wgpu.RenderPassEncoderPopDebugGroup(_currentRenderPass);
        _wgpu.RenderPassEncoderEnd(_currentRenderPass);
        _wgpu.RenderPassEncoderRelease(_currentRenderPass);
        _currentRenderPass = null;

        if (_bindGroupsToRelease.Count > 0)
        {
            foreach (var bindGroup in _bindGroupsToRelease)
                _wgpu.BindGroupRelease((BindGroup*)bindGroup);
            _bindGroupsToRelease.Clear();
        }

        _currentBindGroup = null;
    }

    public void Composite(nuint compositeShader)
    {
        if (_currentRenderPass != null)
            throw new InvalidOperationException("Composite called while in a render pass");

        if (_currentSurfaceTexture == null)
            throw new InvalidOperationException("Surface texture is null - BeginFrame not called?");

        _state.CurrentPassSampleCount = 1;

        var swapChainView = _wgpu.TextureCreateView(_currentSurfaceTexture, null);

        if (swapChainView == null)
            throw new Exception("Failed to create surface texture view for composite");

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

        // Debug group for RenderDoc
        fixed (byte* label = "Composite\0"u8)
        {
            _wgpu.RenderPassEncoderPushDebugGroup(_currentRenderPass, label);
        }

        _wgpu.RenderPassEncoderSetViewport(_currentRenderPass, 0, 0, _surfaceWidth, _surfaceHeight, 0, 1);
        _wgpu.RenderPassEncoderSetScissorRect(_currentRenderPass, 0, 0, (uint)_surfaceWidth, (uint)_surfaceHeight);

        BindShader(compositeShader);
        CreateAndBindCompositeBindGroup(compositeShader);
        BindMesh(_fullscreenQuadMesh);
        DrawElements(0, 6, 0);

        _wgpu.RenderPassEncoderPopDebugGroup(_currentRenderPass);
        _wgpu.RenderPassEncoderEnd(_currentRenderPass);
        _wgpu.RenderPassEncoderRelease(_currentRenderPass);
        _currentRenderPass = null;

        if (_bindGroupsToRelease.Count > 0)
        {
            foreach (var bindGroup in _bindGroupsToRelease)
                _wgpu.BindGroupRelease((BindGroup*)bindGroup);
            _bindGroupsToRelease.Clear();
        }

        _currentBindGroup = null;
        _wgpu.TextureViewRelease(swapChainView);
    }

    private void CreateAndBindCompositeBindGroup(nuint shaderHandle)
    {
        ref var shader = ref _shaders[(int)shaderHandle];

        if (_offscreenResolveTextureView == null)
            throw new InvalidOperationException("Offscreen resolve texture view is null");

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

        var entries = stackalloc BindGroupEntry[2];

        entries[0] = new BindGroupEntry
        {
            Binding = 0,
            TextureView = _offscreenResolveTextureView,
        };

        entries[1] = new BindGroupEntry
        {
            Binding = 1,
            Sampler = sampler,
        };

        if (_currentBindGroup != null)
        {
            _bindGroupsToRelease.Add((nint)_currentBindGroup);
            _currentBindGroup = null;
        }

        var desc = new BindGroupDescriptor
        {
            Layout = shader.BindGroupLayout0,
            EntryCount = 2,
            Entries = entries,
        };
        _currentBindGroup = _wgpu.DeviceCreateBindGroup(_device, &desc);

        if (_currentBindGroup == null)
        {
            Log.Error("Failed to create composite bind group - layout/entries mismatch?");
            _wgpu.SamplerRelease(sampler);
            return;
        }

        _wgpu.RenderPassEncoderSetBindGroup(_currentRenderPass, 0, _currentBindGroup, 0, null);
        _state.BindGroupDirty = false;

        _wgpu.SamplerRelease(sampler);
    }

    public void BeginUIPass()
    {
        if (_currentRenderPass != null)
            throw new InvalidOperationException("BeginUIPass called while already in a render pass");

        if (_commandEncoder == null)
            throw new InvalidOperationException("Command encoder is null - BeginFrame not called?");

        if (_currentSurfaceTexture == null)
            throw new InvalidOperationException("Surface texture is null - BeginFrame not called?");

        _state.CurrentPassSampleCount = 1;

        var swapChainView = _wgpu.TextureCreateView(_currentSurfaceTexture, null);

        if (swapChainView == null)
            throw new Exception("Failed to create surface texture view for UI pass");

        var colorAttachment = new RenderPassColorAttachment
        {
            View = swapChainView,
            LoadOp = LoadOp.Load, // Preserve existing content (the composited scene)
            StoreOp = StoreOp.Store,
        };

        var desc = new RenderPassDescriptor
        {
            ColorAttachmentCount = 1,
            ColorAttachments = &colorAttachment,
        };

        _currentRenderPass = _wgpu.CommandEncoderBeginRenderPass(_commandEncoder, &desc);

        // Debug group for RenderDoc
        fixed (byte* label = "UIPass\0"u8)
        {
            _wgpu.RenderPassEncoderPushDebugGroup(_currentRenderPass, label);
        }

        _wgpu.RenderPassEncoderSetViewport(_currentRenderPass, 0, 0, _surfaceWidth, _surfaceHeight, 0, 1);
        _wgpu.RenderPassEncoderSetScissorRect(_currentRenderPass, 0, 0, (uint)_surfaceWidth, (uint)_surfaceHeight);

        // Store the view so we can release it in EndUIPass
        _currentSurfaceView = swapChainView;

        // Force pipeline and bind group to be rebound for the new render pass
        _state.PipelineDirty = true;
        _state.BindGroupDirty = true;
    }

    public void EndUIPass()
    {
        if (_currentRenderPass == null)
            return;

        _wgpu.RenderPassEncoderPopDebugGroup(_currentRenderPass);
        _wgpu.RenderPassEncoderEnd(_currentRenderPass);
        _wgpu.RenderPassEncoderRelease(_currentRenderPass);
        _currentRenderPass = null;

        if (_bindGroupsToRelease.Count > 0)
        {
            foreach (var bindGroup in _bindGroupsToRelease)
                _wgpu.BindGroupRelease((BindGroup*)bindGroup);
            _bindGroupsToRelease.Clear();
        }

        _currentBindGroup = null;

        if (_currentSurfaceView != null)
        {
            _wgpu.TextureViewRelease(_currentSurfaceView);
            _currentSurfaceView = null;
        }
    }
}
