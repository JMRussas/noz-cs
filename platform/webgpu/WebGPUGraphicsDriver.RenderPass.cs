//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using Silk.NET.WebGPU;

namespace NoZ.Platform.WebGPU;

public unsafe partial class WebGPUGraphicsDriver
{
    public void BeginScenePass(Color clearColor)
    {
        if (_currentRenderPass != null)
            throw new InvalidOperationException("BeginScenePass called while already in a render pass");

        if (_commandEncoder == null)
            throw new InvalidOperationException("Command encoder is null - BeginFrame not called?");

        if (_currentSurfaceTextureView == null)
            throw new InvalidOperationException("Surface texture view is null - BeginFrame not called?");

        // Reset all cached state — new render pass encoder needs everything rebound
        _state = default;
        _state.CurrentPassSampleCount = 1;
        _state.PipelineDirty = true;
        _state.BindGroupDirty = true;
        _currentGlobalsIndex = -1;

        var colorAttachment = new RenderPassColorAttachment
        {
            View = _currentSurfaceTextureView,
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

    public void ResumeScenePass()
    {
        if (_currentRenderPass != null)
            throw new InvalidOperationException("ResumeScenePass called while already in a render pass");

        if (_commandEncoder == null)
            throw new InvalidOperationException("Command encoder is null - BeginFrame not called?");

        if (_currentSurfaceTextureView == null)
            throw new InvalidOperationException("Surface texture view is null - BeginFrame not called?");

        // Reset all cached state — new render pass encoder needs everything rebound
        _state = default;
        _state.CurrentPassSampleCount = 1;
        _state.PipelineDirty = true;
        _state.BindGroupDirty = true;
        _currentGlobalsIndex = -1;

        var colorAttachment = new RenderPassColorAttachment
        {
            View = _currentSurfaceTextureView,
            ResolveTarget = null,
            LoadOp = LoadOp.Load,
            StoreOp = StoreOp.Store,
        };

        var desc = new RenderPassDescriptor
        {
            ColorAttachments = &colorAttachment,
            ColorAttachmentCount = 1,
            DepthStencilAttachment = null
        };

        _currentRenderPass = _wgpu.CommandEncoderBeginRenderPass(_commandEncoder, in desc);

        fixed (byte* label = "ScenePass (resumed)\0"u8)
        {
            _wgpu.RenderPassEncoderPushDebugGroup(_currentRenderPass, label);
        }

        _wgpu.RenderPassEncoderSetViewport(_currentRenderPass, 0, 0, _surfaceWidth, _surfaceHeight, 0, 1);
        _wgpu.RenderPassEncoderSetScissorRect(_currentRenderPass, 0, 0, (uint)_surfaceWidth, (uint)_surfaceHeight);
    }
}
