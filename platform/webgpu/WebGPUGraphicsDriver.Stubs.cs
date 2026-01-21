//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using NoZ;
using NoZ.Platform;

namespace NoZ.Platform.WebGPU;

public unsafe partial class WebGPUGraphicsDriver
{
    // Synchronization (to be implemented in Phase 6)
    public nuint CreateFence()
    {
        throw new NotImplementedException("CreateFence will be implemented in Phase 6");
    }

    public void WaitFence(nuint fence)
    {
        throw new NotImplementedException("WaitFence will be implemented in Phase 6");
    }

    public void DeleteFence(nuint fence)
    {
        throw new NotImplementedException("DeleteFence will be implemented in Phase 6");
    }

    // Offscreen Rendering (to be implemented in Phase 5)
    public void ResizeOffscreenTarget(int width, int height, int msaaSamples)
    {
        throw new NotImplementedException("ResizeOffscreenTarget will be implemented in Phase 5");
    }

    public void BeginScenePass(Color clearColor)
    {
        throw new NotImplementedException("BeginScenePass will be implemented in Phase 5");
    }

    public void EndScenePass()
    {
        throw new NotImplementedException("EndScenePass will be implemented in Phase 5");
    }

    public void Composite(nuint compositeShader)
    {
        throw new NotImplementedException("Composite will be implemented in Phase 5");
    }
}
