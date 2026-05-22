//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public static class VectorExtensions
{
    extension(Vector3 v)
    {
        public Vector2 XY => new(v.X, v.Y);
    }    

    extension(Vector2 v)
    {
        public Vector3 XYZ => new(v.X, v.Y, 0);
    }    
}
