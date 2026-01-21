//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using Silk.NET.WebGPU;
using NoZ.Platform;

namespace NoZ.Platform.WebGPU;

public unsafe partial class WebGPUGraphicsDriver
{
    public void SetBlendMode(BlendMode mode)
    {
        if (_state.BlendMode == mode)
            return;

        _state.BlendMode = mode;
        _state.PipelineDirty = true;
    }

    public void DrawElements(int firstIndex, int indexCount, int baseVertex = 0)
    {
        if (_currentRenderPass == null)
            throw new InvalidOperationException("DrawElements called outside of render pass");

        // Update pipeline if shader/blend/vertex format changed
        if (_state.PipelineDirty)
        {
            var pipeline = GetOrCreatePipeline(
                _state.BoundShader,
                _state.BlendMode,
                _meshes[(int)_state.BoundMesh].Stride
            );
            _wgpu.RenderPassEncoderSetPipeline(_currentRenderPass, pipeline);
            _state.PipelineDirty = false;
        }

        // Update bind group if textures/buffers changed
        UpdateBindGroupIfNeeded();

        // Draw indexed
        _wgpu.RenderPassEncoderDrawIndexed(
            _currentRenderPass,
            (uint)indexCount,
            1, // instance count
            (uint)firstIndex,
            baseVertex,
            0 // first instance
        );
    }

    private void UpdateBindGroupIfNeeded()
    {
        // Check if bound resources changed
        if (!_state.BindGroupDirty)
            return;

        ref var shader = ref _shaders[(int)_state.BoundShader];

        // Build bind group entries
        var entries = stackalloc BindGroupEntry[5];

        // Binding 0: Globals uniform buffer
        entries[0] = new BindGroupEntry
        {
            Binding = 0,
            Buffer = _buffers[(int)_state.BoundUniformBuffer0].Buffer,
            Offset = 0,
            Size = (ulong)_buffers[(int)_state.BoundUniformBuffer0].SizeInBytes,
        };

        // Binding 1: Bone texture
        entries[1] = new BindGroupEntry
        {
            Binding = 1,
            TextureView = _textures[(int)_state.BoundBoneTexture].TextureView,
        };

        // Binding 2: Bone sampler
        entries[2] = new BindGroupEntry
        {
            Binding = 2,
            Sampler = _textures[(int)_state.BoundBoneTexture].Sampler,
        };

        // Binding 3: Texture array
        entries[3] = new BindGroupEntry
        {
            Binding = 3,
            TextureView = _textures[(int)_state.BoundTexture0].TextureView,
        };

        // Binding 4: Texture sampler
        entries[4] = new BindGroupEntry
        {
            Binding = 4,
            Sampler = _textures[(int)_state.BoundTexture0].Sampler,
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
            EntryCount = 5,
            Entries = entries,
        };
        _currentBindGroup = _wgpu.DeviceCreateBindGroup(_device, &desc);

        // Bind to render pass
        if (_currentRenderPass != null)
        {
            _wgpu.RenderPassEncoderSetBindGroup(_currentRenderPass, 0, _currentBindGroup, 0, null);
        }

        _state.BindGroupDirty = false;
    }
}
