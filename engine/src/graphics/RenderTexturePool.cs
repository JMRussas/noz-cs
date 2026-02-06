//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

internal static class RenderTexturePool
{
    private struct PooledTexture
    {
        public nuint Handle;
        public int Width;
        public int Height;
        public bool InUse;
    }

    private const int MaxPooledTextures = 16;
    private const int MaxPendingReleases = 32;
    private static readonly PooledTexture[] _pool = new PooledTexture[MaxPooledTextures];
    private static readonly nuint[] _pendingReleases = new nuint[MaxPendingReleases];
    private static int _poolCount;
    private static int _pendingCount;

    public static RenderTexture Acquire(int width, int height)
    {
        // First, try to find an exact size match that's not in use
        for (int i = 0; i < _poolCount; i++)
        {
            ref var entry = ref _pool[i];
            if (!entry.InUse && entry.Width == width && entry.Height == height)
            {
                entry.InUse = true;
                return new RenderTexture(entry.Handle, width, height);
            }
        }

        // Evict all unused entries - they have stale sizes (e.g. from resize)
        for (int i = _poolCount - 1; i >= 0; i--)
        {
            if (!_pool[i].InUse)
            {
                Graphics.Driver.DestroyRenderTexture(_pool[i].Handle);
                _pool[i] = _pool[--_poolCount];
            }
        }

        // No match found - create a new texture
        var handle = Graphics.Driver.CreateRenderTexture(width, height, name: "PooledRT");

        // Try to add to pool if there's room
        if (_poolCount < MaxPooledTextures)
        {
            _pool[_poolCount++] = new PooledTexture
            {
                Handle = handle,
                Width = width,
                Height = height,
                InUse = true
            };
        }

        return new RenderTexture(handle, width, height);
    }

    public static void Release(RenderTexture rt)
    {
        if (!rt.IsValid)
            return;
        
        if (_pendingCount < MaxPendingReleases)
            _pendingReleases[_pendingCount++] = rt.Handle;
    }

    internal static void FlushPendingReleases()
    {
        for (int p = 0; p < _pendingCount; p++)
        {
            var handle = _pendingReleases[p];

            // Find the texture in the pool and mark it as available
            bool found = false;
            for (int i = 0; i < _poolCount; i++)
            {
                ref var entry = ref _pool[i];
                if (entry.Handle == handle)
                {
                    entry.InUse = false;
                    found = true;
                    break;
                }
            }

            if (!found)
                Graphics.Driver.DestroyRenderTexture(handle);
        }

        _pendingCount = 0;
    }

    public static void Clear()
    {
        for (int i = 0; i < _poolCount; i++)
        {
            if (_pool[i].Handle != 0)
            {
                Graphics.Driver.DestroyRenderTexture(_pool[i].Handle);
                _pool[i] = default;
            }
        }
        _poolCount = 0;
    }
}
