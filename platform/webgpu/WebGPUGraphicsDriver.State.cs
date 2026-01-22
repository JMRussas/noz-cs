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

        // Bind vertex and index buffers
        ref var mesh = ref _meshes[(int)_state.BoundMesh];
        _wgpu.RenderPassEncoderSetVertexBuffer(_currentRenderPass, 0, mesh.VertexBuffer, 0, (ulong)(mesh.MaxVertices * mesh.Stride));
        _wgpu.RenderPassEncoderSetIndexBuffer(_currentRenderPass, mesh.IndexBuffer, IndexFormat.Uint16, 0, (ulong)(mesh.MaxIndices * sizeof(ushort)));

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
        if (!_state.BindGroupDirty)
            return;

        ref var shader = ref _shaders[(int)_state.BoundShader];
        var bindings = shader.Bindings;

        if (bindings == null || bindings.Count == 0)
        {
            Log.Error("Shader has no binding metadata!");
            _state.BindGroupDirty = false;
            return;
        }

        var entries = stackalloc BindGroupEntry[bindings.Count];
        int validEntryCount = 0;

        for (int i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];

            switch (binding.Type)
            {
                case ShaderBindingType.UniformBuffer:
                {
                    nuint uniformBuffer = GetUniformBufferByName(binding.Name);
                    if (uniformBuffer == 0)
                    {
                        Log.Error($"Uniform '{binding.Name}' not bound!");
                        _state.BindGroupDirty = false;
                        return;
                    }

                    entries[validEntryCount++] = new BindGroupEntry
                    {
                        Binding = binding.Binding,
                        Buffer = _buffers[(int)uniformBuffer].Buffer,
                        Offset = 0,
                        Size = (ulong)_buffers[(int)uniformBuffer].SizeInBytes,
                    };
                    break;
                }

                case ShaderBindingType.Texture2D:
                case ShaderBindingType.Texture2DArray:
                {
                    int textureSlot = GetTextureSlotForBinding(binding.Binding, ref shader);
                    nuint textureHandle = textureSlot >= 0 ? (nuint)_state.BoundTextures[textureSlot] : 0;

                    if (textureHandle == 0)
                    {
                        Log.Error($"Texture slot {textureSlot} (binding {binding.Binding}) not bound!");
                        _state.BindGroupDirty = false;
                        return;
                    }

                    entries[validEntryCount++] = new BindGroupEntry
                    {
                        Binding = binding.Binding,
                        TextureView = _textures[(int)textureHandle].TextureView,
                    };
                    break;
                }

                case ShaderBindingType.Sampler:
                {
                    int samplerSlot = GetTextureSlotForBinding(binding.Binding, ref shader);
                    nuint samplerTextureHandle = samplerSlot >= 0 ? (nuint)_state.BoundTextures[samplerSlot] : 0;

                    if (samplerTextureHandle == 0)
                    {
                        Log.Error($"Sampler for slot {samplerSlot} (binding {binding.Binding}) not bound!");
                        _state.BindGroupDirty = false;
                        return;
                    }

                    entries[validEntryCount++] = new BindGroupEntry
                    {
                        Binding = binding.Binding,
                        Sampler = _textures[(int)samplerTextureHandle].Sampler,
                    };
                    break;
                }
            }
        }

        if (_currentBindGroup != null)
        {
            // Defer release until after render pass ends
            Log.Debug($"Deferring release of old bind group: {(nint)_currentBindGroup:X}");
            _bindGroupsToRelease.Add((nint)_currentBindGroup);
            _currentBindGroup = null;
        }

        var desc = new BindGroupDescriptor
        {
            Layout = shader.BindGroupLayout0,
            EntryCount = (uint)validEntryCount,
            Entries = entries,
        };
        _currentBindGroup = _wgpu.DeviceCreateBindGroup(_device, &desc);

        if (_currentBindGroup == null)
        {
            Log.Error("Failed to create bind group!");
            return;
        }

        Log.Debug($"Created bind group: {(nint)_currentBindGroup:X} with {validEntryCount} entries");

        if (_currentRenderPass != null)
        {
            Log.Debug($"Binding group to render pass: {(nint)_currentRenderPass:X}");
            _wgpu.RenderPassEncoderSetBindGroup(_currentRenderPass, 0, _currentBindGroup, 0, null);
        }

        _state.BindGroupDirty = false;
    }

    private nuint GetUniformBufferByName(string name)
    {
        return name switch
        {
            "globals" => (nuint)_state.BoundUniformBuffers[0],
            "text_params" => (nuint)_state.BoundUniformBuffers[1],
            _ => 0
        };
    }

    private int GetTextureSlotForBinding(uint bindingNumber, ref ShaderInfo shader)
    {
        for (int i = 0; i < shader.TextureSlots.Count; i++)
        {
            var slot = shader.TextureSlots[i];
            if (slot.TextureBinding == bindingNumber || slot.SamplerBinding == bindingNumber)
                return i;
        }
        return -1;
    }
}
