//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;
using Silk.NET.Maths;

namespace NoZ.Platform;

public unsafe partial class DirectX12GraphicsDriver
{
    // Additional offscreen resources
    private ComPtr<ID3D12Resource> _offscreenResolveTarget;
    private int _offscreenSrvDescriptorIndex;

    public void ResizeOffscreenTarget(int width, int height, int msaaSamples)
    {
        if (width == _offscreenWidth && height == _offscreenHeight && msaaSamples == _msaaSamples)
            return;

        // Wait for GPU before destroying resources
        WaitForGpu();
        DestroyOffscreenTarget();

        _offscreenWidth = width;
        _offscreenHeight = height;
        _msaaSamples = msaaSamples;

        var heapProps = new HeapProperties
        {
            Type = HeapType.Default,
            CPUPageProperty = CpuPageProperty.Unknown,
            MemoryPoolPreference = MemoryPool.Unknown,
            CreationNodeMask = 1,
            VisibleNodeMask = 1
        };

        // Create render target (MSAA or not)
        var sampleCount = msaaSamples > 1 ? (uint)msaaSamples : 1u;
        var rtDesc = new ResourceDesc
        {
            Dimension = ResourceDimension.Texture2D,
            Alignment = 0,
            Width = (ulong)width,
            Height = (uint)height,
            DepthOrArraySize = 1,
            MipLevels = 1,
            Format = Format.FormatR8G8B8A8Unorm,
            SampleDesc = new SampleDesc { Count = sampleCount, Quality = 0 },
            Layout = TextureLayout.LayoutUnknown,
            Flags = ResourceFlags.AllowRenderTarget
        };

        var clearValue = new ClearValue
        {
            Format = Format.FormatR8G8B8A8Unorm
        };
        clearValue.Anonymous.Color[0] = 0;
        clearValue.Anonymous.Color[1] = 0;
        clearValue.Anonymous.Color[2] = 0;
        clearValue.Anonymous.Color[3] = 1;

        int hr;
        fixed (ComPtr<ID3D12Resource>* pRenderTarget = &_offscreenRenderTarget)
        {
            hr = _device.CreateCommittedResource(
                &heapProps,
                HeapFlags.None,
                &rtDesc,
                ResourceStates.RenderTarget,
                &clearValue,
                SilkMarshal.GuidPtrOf<ID3D12Resource>(),
                (void**)pRenderTarget);
        }

        if (hr < 0)
            throw new Exception($"Failed to create offscreen render target: 0x{hr:X8}");

        // Create depth stencil
        var dsDesc = new ResourceDesc
        {
            Dimension = ResourceDimension.Texture2D,
            Alignment = 0,
            Width = (ulong)width,
            Height = (uint)height,
            DepthOrArraySize = 1,
            MipLevels = 1,
            Format = Format.FormatD24UnormS8Uint,
            SampleDesc = new SampleDesc { Count = sampleCount, Quality = 0 },
            Layout = TextureLayout.LayoutUnknown,
            Flags = ResourceFlags.AllowDepthStencil
        };

        var dsClearValue = new ClearValue
        {
            Format = Format.FormatD24UnormS8Uint
        };
        dsClearValue.Anonymous.DepthStencil.Depth = 1.0f;
        dsClearValue.Anonymous.DepthStencil.Stencil = 0;

        fixed (ComPtr<ID3D12Resource>* pDepthStencil = &_offscreenDepthStencil)
        {
            hr = _device.CreateCommittedResource(
                &heapProps,
                HeapFlags.None,
                &dsDesc,
                ResourceStates.DepthWrite,
                &dsClearValue,
                SilkMarshal.GuidPtrOf<ID3D12Resource>(),
                (void**)pDepthStencil);
        }

        if (hr < 0)
            throw new Exception($"Failed to create offscreen depth stencil: 0x{hr:X8}");

        // If MSAA, create resolve target for sampling
        if (msaaSamples > 1)
        {
            var resolveDesc = new ResourceDesc
            {
                Dimension = ResourceDimension.Texture2D,
                Alignment = 0,
                Width = (ulong)width,
                Height = (uint)height,
                DepthOrArraySize = 1,
                MipLevels = 1,
                Format = Format.FormatR8G8B8A8Unorm,
                SampleDesc = new SampleDesc { Count = 1, Quality = 0 },
                Layout = TextureLayout.LayoutUnknown,
                Flags = ResourceFlags.None
            };

            fixed (ComPtr<ID3D12Resource>* pResolveTarget = &_offscreenResolveTarget)
            {
                hr = _device.CreateCommittedResource(
                    &heapProps,
                    HeapFlags.None,
                    &resolveDesc,
                    ResourceStates.PixelShaderResource,
                    (ClearValue*)null,
                    SilkMarshal.GuidPtrOf<ID3D12Resource>(),
                    (void**)pResolveTarget);
            }

            if (hr < 0)
                throw new Exception($"Failed to create offscreen resolve target: 0x{hr:X8}");
        }

        // Create RTV heap for offscreen target
        var rtvHeapDesc = new DescriptorHeapDesc
        {
            Type = DescriptorHeapType.Rtv,
            NumDescriptors = 1,
            Flags = DescriptorHeapFlags.None,
            NodeMask = 0
        };
        fixed (ComPtr<ID3D12DescriptorHeap>* pHeap = &_offscreenRtvHeap)
        {
            hr = _device.CreateDescriptorHeap(&rtvHeapDesc, SilkMarshal.GuidPtrOf<ID3D12DescriptorHeap>(), (void**)pHeap);
            if (hr < 0)
                throw new Exception($"Failed to create offscreen RTV heap: 0x{hr:X8}");
        }

        // Create DSV heap
        var dsvHeapDesc = new DescriptorHeapDesc
        {
            Type = DescriptorHeapType.Dsv,
            NumDescriptors = 1,
            Flags = DescriptorHeapFlags.None,
            NodeMask = 0
        };
        fixed (ComPtr<ID3D12DescriptorHeap>* pHeap = &_offscreenDsvHeap)
        {
            hr = _device.CreateDescriptorHeap(&dsvHeapDesc, SilkMarshal.GuidPtrOf<ID3D12DescriptorHeap>(), (void**)pHeap);
            if (hr < 0)
                throw new Exception($"Failed to create offscreen DSV heap: 0x{hr:X8}");
        }

        // Create RTV
        var rtvHandle = _offscreenRtvHeap.GetCPUDescriptorHandleForHeapStart();
        _device.CreateRenderTargetView(_offscreenRenderTarget, (RenderTargetViewDesc*)null, rtvHandle);

        // Create DSV
        var dsvHandle = _offscreenDsvHeap.GetCPUDescriptorHandleForHeapStart();
        var dsvDesc = new DepthStencilViewDesc
        {
            Format = Format.FormatD24UnormS8Uint,
            ViewDimension = msaaSamples > 1
                ? DsvDimension.Texture2Dms
                : DsvDimension.Texture2D,
            Flags = DsvFlags.None
        };
        _device.CreateDepthStencilView(_offscreenDepthStencil, &dsvDesc, dsvHandle);

        // Create SRV for the resolve target (or non-MSAA render target)
        _offscreenSrvDescriptorIndex = MaxBuffers + MaxTextures + MaxTextureArrays; // Use a reserved slot

        var srvHandle = _cbvSrvUavHeap.GetCPUDescriptorHandleForHeapStart();
        srvHandle.Ptr += (nuint)(_offscreenSrvDescriptorIndex * _cbvSrvUavDescriptorSize);

        var srvResource = msaaSamples > 1 ? _offscreenResolveTarget : _offscreenRenderTarget;
        var srvDesc = new ShaderResourceViewDesc
        {
            Format = Format.FormatR8G8B8A8Unorm,
            ViewDimension = SrvDimension.Texture2D,
            Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
            Anonymous = new ShaderResourceViewDescUnion
            {
                Texture2D = new Tex2DSrv
                {
                    MostDetailedMip = 0,
                    MipLevels = 1,
                    PlaneSlice = 0,
                    ResourceMinLODClamp = 0.0f
                }
            }
        };

        // For MSAA, we need to transition the resolve target first
        if (msaaSamples > 1)
        {
            _device.CreateShaderResourceView(_offscreenResolveTarget, &srvDesc, srvHandle);
        }
        else
        {
            // For non-MSAA, we'll transition and create SRV later
            _device.CreateShaderResourceView(_offscreenRenderTarget, &srvDesc, srvHandle);
        }
    }

    private void DestroyOffscreenTarget()
    {
        _offscreenRenderTarget.Dispose();
        _offscreenDepthStencil.Dispose();
        _offscreenResolveTarget.Dispose();
        _offscreenRtvHeap.Dispose();
        _offscreenDsvHeap.Dispose();

        _offscreenWidth = 0;
        _offscreenHeight = 0;
        _msaaSamples = 0;
    }

    public void BeginScenePass(Color clearColor)
    {
        if (_offscreenRenderTarget.Handle == null) return;

        // Get RTV and DSV handles
        var rtvHandle = _offscreenRtvHeap.GetCPUDescriptorHandleForHeapStart();

        var dsvHandle = _offscreenDsvHeap.GetCPUDescriptorHandleForHeapStart();

        // Set render target
        _commandList.OMSetRenderTargets(1, &rtvHandle, 0, &dsvHandle);

        // Set viewport and scissor
        SetViewport(0, 0, _offscreenWidth, _offscreenHeight);
        DisableScissor();

        // Clear
        float* color = stackalloc float[4] { clearColor.R, clearColor.G, clearColor.B, clearColor.A };
        _commandList.ClearRenderTargetView(rtvHandle, color, 0, (Box2D<int>*)null);
        _commandList.ClearDepthStencilView(dsvHandle,
            ClearFlags.Depth | ClearFlags.Stencil,
            1.0f, 0, 0, (Box2D<int>*)null);
    }

    public void EndScenePass()
    {
        if (_offscreenRenderTarget.Handle == null) return;

        if (_msaaSamples > 1 && _offscreenResolveTarget.Handle != null)
        {
            // Transition MSAA target to resolve source
            var barrier1 = new ResourceBarrier
            {
                Type = ResourceBarrierType.Transition,
                Flags = ResourceBarrierFlags.None,
                Anonymous = new ResourceBarrierUnion
                {
                    Transition = new ResourceTransitionBarrier
                    {
                        PResource = _offscreenRenderTarget,
                        StateBefore = ResourceStates.RenderTarget,
                        StateAfter = ResourceStates.ResolveSource,
                        Subresource = 0xFFFFFFFF
                    }
                }
            };

            // Transition resolve target to resolve dest
            var barrier2 = new ResourceBarrier
            {
                Type = ResourceBarrierType.Transition,
                Flags = ResourceBarrierFlags.None,
                Anonymous = new ResourceBarrierUnion
                {
                    Transition = new ResourceTransitionBarrier
                    {
                        PResource = _offscreenResolveTarget,
                        StateBefore = ResourceStates.PixelShaderResource,
                        StateAfter = ResourceStates.ResolveDest,
                        Subresource = 0xFFFFFFFF
                    }
                }
            };

            var barriers = stackalloc ResourceBarrier[2] { barrier1, barrier2 };
            _commandList.ResourceBarrier(2, barriers);

            // Resolve MSAA
            _commandList.ResolveSubresource(_offscreenResolveTarget, 0, _offscreenRenderTarget, 0,
                Format.FormatR8G8B8A8Unorm);

            // Transition resolve target back to shader resource
            var barrier3 = new ResourceBarrier
            {
                Type = ResourceBarrierType.Transition,
                Flags = ResourceBarrierFlags.None,
                Anonymous = new ResourceBarrierUnion
                {
                    Transition = new ResourceTransitionBarrier
                    {
                        PResource = _offscreenResolveTarget,
                        StateBefore = ResourceStates.ResolveDest,
                        StateAfter = ResourceStates.PixelShaderResource,
                        Subresource = 0xFFFFFFFF
                    }
                }
            };

            // Transition MSAA target back to render target for next frame
            var barrier4 = new ResourceBarrier
            {
                Type = ResourceBarrierType.Transition,
                Flags = ResourceBarrierFlags.None,
                Anonymous = new ResourceBarrierUnion
                {
                    Transition = new ResourceTransitionBarrier
                    {
                        PResource = _offscreenRenderTarget,
                        StateBefore = ResourceStates.ResolveSource,
                        StateAfter = ResourceStates.RenderTarget,
                        Subresource = 0xFFFFFFFF
                    }
                }
            };

            var postBarriers = stackalloc ResourceBarrier[2] { barrier3, barrier4 };
            _commandList.ResourceBarrier(2, postBarriers);
        }
        else
        {
            // Non-MSAA: transition render target to shader resource
            var barrier = new ResourceBarrier
            {
                Type = ResourceBarrierType.Transition,
                Flags = ResourceBarrierFlags.None,
                Anonymous = new ResourceBarrierUnion
                {
                    Transition = new ResourceTransitionBarrier
                    {
                        PResource = _offscreenRenderTarget,
                        StateBefore = ResourceStates.RenderTarget,
                        StateAfter = ResourceStates.PixelShaderResource,
                        Subresource = 0xFFFFFFFF
                    }
                }
            };
            _commandList.ResourceBarrier(1, &barrier);
        }
    }

    public void Composite(nuint compositeShader)
    {
        ref var shader = ref _shaders[(int)compositeShader];
        if (shader.RootSignature.Handle == null) return;

        // Set back buffer as render target
        var rtv = GetCurrentRtvHandle();
        _commandList.OMSetRenderTargets(1, &rtv, 0, (CpuDescriptorHandle*)null);

        // Set viewport and scissor
        SetViewport(0, 0, _offscreenWidth, _offscreenHeight);
        DisableScissor();

        // Clear back buffer
        float* clearColor = stackalloc float[4] { 0, 0, 0, 1 };
        _commandList.ClearRenderTargetView(rtv, clearColor, 0, (Box2D<int>*)null);

        // Bind shader
        BindShader(compositeShader);

        // Bind offscreen texture
        var gpuHandle = _cbvSrvUavHeap.GetGPUDescriptorHandleForHeapStart();
        gpuHandle.Ptr += (ulong)(_offscreenSrvDescriptorIndex * _cbvSrvUavDescriptorSize);
        _commandList.SetGraphicsRootDescriptorTable(1, gpuHandle);

        // Bind sampler
        var samplerHandle = _samplerHeap.GetGPUDescriptorHandleForHeapStart();
        _commandList.SetGraphicsRootDescriptorTable(3, samplerHandle);

        // Draw fullscreen quad
        // Note: This requires the fullscreen mesh to be set up
        if (_fullscreenMesh != 0)
        {
            BindMesh(_fullscreenMesh);
            SetBlendMode(BlendMode.None);

            // Get or create PSO
            ref var meshInfo = ref _meshes[(int)_fullscreenMesh];
            var psoKey = new PsoKey
            {
                ShaderHandle = compositeShader,
                BlendMode = BlendMode.None,
                VertexStride = meshInfo.Stride
            };

            if (!shader.PsoCache.TryGetValue(psoKey, out var pso))
            {
                pso = CreatePso(ref shader, meshInfo.Stride);
                shader.PsoCache[psoKey] = pso;
            }

            _commandList.SetPipelineState(pso);
            _commandList.DrawIndexedInstanced(6, 1, 0, 0, 0);
        }

        // If non-MSAA, transition render target back to render target state for next frame
        if (_msaaSamples <= 1)
        {
            var barrier = new ResourceBarrier
            {
                Type = ResourceBarrierType.Transition,
                Flags = ResourceBarrierFlags.None,
                Anonymous = new ResourceBarrierUnion
                {
                    Transition = new ResourceTransitionBarrier
                    {
                        PResource = _offscreenRenderTarget,
                        StateBefore = ResourceStates.PixelShaderResource,
                        StateAfter = ResourceStates.RenderTarget,
                        Subresource = 0xFFFFFFFF
                    }
                }
            };
            _commandList.ResourceBarrier(1, &barrier);
        }
    }
}
