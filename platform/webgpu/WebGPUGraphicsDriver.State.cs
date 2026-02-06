//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using Silk.NET.WebGPU;
using NoZ.Platform;
using WGPUBuffer = Silk.NET.WebGPU.Buffer;
using WGPUBufferUsage = Silk.NET.WebGPU.BufferUsage;

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

    public void SetTextureFilter(TextureFilter filter)
    {
        if (_state.TextureFilter == filter)
            return;

        _state.TextureFilter = filter;
        _state.BindGroupDirty = true;
    }

    public void SetUniform(string name, ReadOnlySpan<byte> data)
    {
        // Store uniform data by name - will be written to per-shader buffers when bind groups are created
        if (!_uniformData.TryGetValue(name, out var existing) || existing.Length != data.Length)
            _uniformData[name] = new byte[data.Length];

        data.CopyTo(_uniformData[name]);
        _state.BindGroupDirty = true;
    }

    public void SetGlobalsCount(int count)
    {
        // Ensure we have enough globals buffers allocated
        while (_globalsBufferCount < count)
        {
            var bufferDesc = new BufferDescriptor
            {
                Size = GlobalsBufferSize,
                Usage = WGPUBufferUsage.Uniform | WGPUBufferUsage.CopyDst,
                MappedAtCreation = false
            };
            _globalsBuffers[_globalsBufferCount] = _wgpu.DeviceCreateBuffer(_device, &bufferDesc);
            _globalsBufferCount++;
        }
    }

    public void SetGlobals(int index, ReadOnlySpan<byte> data)
    {
        if (index < 0 || index >= _globalsBufferCount)
            return;

        fixed (byte* dataPtr = data)
        {
            _wgpu.QueueWriteBuffer(_queue, _globalsBuffers[index], 0, dataPtr, (nuint)data.Length);
        }
    }

    public void BindGlobals(int index)
    {
        if (_currentGlobalsIndex == index)
            return;

        _currentGlobalsIndex = index;
        _state.BindGroupDirty = true;
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

            if (pipeline == null)
            {
                Log.Error($"Cannot draw - pipeline is null for shader {_state.BoundShader}");
                return;
            }

            _wgpu.RenderPassEncoderSetPipeline(_currentRenderPass, pipeline);
            _state.PipelineDirty = false;
        }

        // Update bind group if textures/buffers changed
        UpdateBindGroupIfNeeded();

        // Bind vertex and index buffers
        ref var mesh = ref _meshes[(int)_state.BoundMesh];
        _wgpu.RenderPassEncoderSetVertexBuffer(_currentRenderPass, 0, mesh.VertexBuffer, 0, (ulong)(mesh.MaxVertices * mesh.Stride));
        _wgpu.RenderPassEncoderSetIndexBuffer(_currentRenderPass, mesh.IndexBuffer, IndexFormat.Uint16, 0, (ulong)(mesh.MaxIndices * sizeof(ushort)));

        // Apply scissor state right before draw
        if (_state.ScissorEnabled)
        {
            _wgpu.RenderPassEncoderSetScissorRect(
                _currentRenderPass,
                (uint)_state.Scissor.X,
                (uint)(_state.Viewport.Height - _state.Scissor.Y - _state.Scissor.Height),
                (uint)_state.Scissor.Width,
                (uint)_state.Scissor.Height);
        }
        else
        {
            // Use render texture dimensions if rendering to texture, otherwise surface dimensions
            uint width, height;
            if (_activeRenderTexture != 0)
            {
                ref var rt = ref _renderTextures[_rtHandleToSlot[(int)_activeRenderTexture]];
                width = (uint)rt.Width;
                height = (uint)rt.Height;
            }
            else
            {
                width = (uint)_surfaceWidth;
                height = (uint)_surfaceHeight;
            }
            _wgpu.RenderPassEncoderSetScissorRect(_currentRenderPass, 0, 0, width, height);
        }

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
                    WGPUBuffer* buffer;
                    ulong bufferSize;

                    // Use indexed globals buffer for "globals" uniform
                    if (binding.Name == "globals")
                    {
                        if (_currentGlobalsIndex < 0 || _currentGlobalsIndex >= _globalsBufferCount)
                        {
                            Log.Error($"Globals index {_currentGlobalsIndex} out of range!");
                            _state.BindGroupDirty = false;
                            return;
                        }
                        buffer = _globalsBuffers[_currentGlobalsIndex];
                        bufferSize = GlobalsBufferSize;
                    }
                    else
                    {
                        // Get uniform data by name for non-globals uniforms
                        if (!_uniformData.TryGetValue(binding.Name, out var uniformData))
                        {
                            Log.Error($"Uniform '{binding.Name}' not set!");
                            _state.BindGroupDirty = false;
                            return;
                        }

                        // Get or create per-shader buffer for this uniform
                        if (!shader.UniformBuffers.TryGetValue(binding.Name, out var bufferPtr) || bufferPtr == 0)
                        {
                            var bufferDesc = new BufferDescriptor
                            {
                                Size = (ulong)uniformData.Length,
                                Usage = WGPUBufferUsage.Uniform | WGPUBufferUsage.CopyDst,
                                MappedAtCreation = false,
                            };
                            buffer = _wgpu.DeviceCreateBuffer(_device, &bufferDesc);
                            shader.UniformBuffers[binding.Name] = (nint)buffer;
                        }
                        else
                        {
                            buffer = (WGPUBuffer*)bufferPtr;
                        }

                        // Write current uniform data to the shader's buffer
                        fixed (byte* dataPtr = uniformData)
                        {
                            _wgpu.QueueWriteBuffer(_queue, buffer, 0, dataPtr, (nuint)uniformData.Length);
                        }
                        bufferSize = (ulong)uniformData.Length;
                    }

                    entries[validEntryCount++] = new BindGroupEntry
                    {
                        Binding = binding.Binding,
                        Buffer = buffer,
                        Offset = 0,
                        Size = bufferSize,
                    };
                    break;
                }

                case ShaderBindingType.Texture2D:
                case ShaderBindingType.Texture2DArray:
                case ShaderBindingType.Texture2DUnfilterable:
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
                    int textureSlot = GetTextureSlotForBinding(binding.Binding, ref shader);
                    var slotFilter = textureSlot >= 0 ? (TextureFilter)_state.TextureFilters[textureSlot] : TextureFilter.Point;
                    var sampler = slotFilter == TextureFilter.Point ? _nearestSampler : _linearSampler;
                    entries[validEntryCount++] = new BindGroupEntry
                    {
                        Binding = binding.Binding,
                        Sampler = sampler,
                    };
                    break;
                }
            }
        }

        if (_currentBindGroup != null)
        {
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

        if (_currentRenderPass != null)
            _wgpu.RenderPassEncoderSetBindGroup(_currentRenderPass, 0, _currentBindGroup, 0, null);

        _state.BindGroupDirty = false;
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
