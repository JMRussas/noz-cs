//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;
using NoZ.Platform;

namespace NoZ;

public static class UIRender
{
    private const int MaxUIVertices = 16384;
    private const int MaxUIIndices = 32768;

    private static nuint _vertexBuffer;
    private static nuint _indexBuffer;
    private static UIVertex[] _vertices = null!;
    private static ushort[] _indices = null!;
    private static int _vertexCount;
    private static int _indexCount;
    private static Shader? _shader;
    private static bool _initialized;

    public static void Init(Shader shader)
    {
        if (_initialized) return;

        _shader = shader;
        _vertices = new UIVertex[MaxUIVertices];
        _indices = new ushort[MaxUIIndices];

        _vertexBuffer = Render.Driver.CreateVertexBuffer(
            MaxUIVertices * UIVertex.SizeInBytes,
            BufferUsage.Dynamic,
            "UIRender.Vertices"
        );

        _indexBuffer = Render.Driver.CreateIndexBuffer(
            MaxUIIndices * sizeof(ushort),
            BufferUsage.Dynamic,
            "UIRender.Indices"
        );

        _initialized = true;
    }

    public static void Shutdown()
    {
        if (!_initialized) return;

        Render.Driver.DestroyBuffer(_vertexBuffer);
        Render.Driver.DestroyBuffer(_indexBuffer);
        _initialized = false;
    }

    public static void DrawRect(
        float x, float y, float width, float height,
        Color color,
        float borderRadius = 0,
        float borderWidth = 0,
        Color borderColor = default)
    {
        if (!_initialized || _shader == null) return;

        var color32 = color.ToColor32();
        var borderColor32 = borderColor.ToColor32();

        // Simple rect - no border radius
        if (borderRadius <= 0)
        {
            if (_vertexCount + 4 > MaxUIVertices || _indexCount + 6 > MaxUIIndices)
                Flush();

            var bv = _vertexCount;

            _vertices[_vertexCount++] = new UIVertex { Position = new Vector2(x, y), UV = new Vector2(1, 1), Normal = Vector2.Zero, Color = color32, BorderRatio = -1f, BorderColor = borderColor32 };
            _vertices[_vertexCount++] = new UIVertex { Position = new Vector2(x + width, y), UV = new Vector2(1, 1), Normal = Vector2.Zero, Color = color32, BorderRatio = -1f, BorderColor = borderColor32 };
            _vertices[_vertexCount++] = new UIVertex { Position = new Vector2(x + width, y + height), UV = new Vector2(1, 1), Normal = Vector2.Zero, Color = color32, BorderRatio = -1f, BorderColor = borderColor32 };
            _vertices[_vertexCount++] = new UIVertex { Position = new Vector2(x, y + height), UV = new Vector2(1, 1), Normal = Vector2.Zero, Color = color32, BorderRatio = -1f, BorderColor = borderColor32 };

            _indices[_indexCount++] = (ushort)bv;
            _indices[_indexCount++] = (ushort)(bv + 1);
            _indices[_indexCount++] = (ushort)(bv + 2);
            _indices[_indexCount++] = (ushort)(bv + 2);
            _indices[_indexCount++] = (ushort)(bv + 3);
            _indices[_indexCount++] = (ushort)bv;
            return;
        }

        // Rounded rect - 16 vertices, 36 indices (same mesh as C++)
        if (_vertexCount + 16 > MaxUIVertices || _indexCount + 36 > MaxUIIndices)
            Flush();

        var borderRatio = borderWidth / borderRadius;
        var vs = _vertexCount;
        var x0 = x;
        var x1 = x + width;
        var y0 = y;
        var y1 = y + height;
        var r = borderRadius;
        
        // 0-3 (top row, inner edge)
        _vertices[_vertexCount++] = new UIVertex { Position = new Vector2(x0, y0), UV = new Vector2(1, 1), Normal = new Vector2(0, 0), Color = color32, BorderRatio = borderRatio, BorderColor = borderColor32 };
        _vertices[_vertexCount++] = new UIVertex { Position = new Vector2(x0, y0), UV = new Vector2(0, 1), Normal = new Vector2(r, 0), Color = color32, BorderRatio = borderRatio, BorderColor = borderColor32 };
        _vertices[_vertexCount++] = new UIVertex { Position = new Vector2(x1, y0), UV = new Vector2(0, 1), Normal = new Vector2(-r, 0), Color = color32, BorderRatio = borderRatio, BorderColor = borderColor32 };
        _vertices[_vertexCount++] = new UIVertex { Position = new Vector2(x1, y0), UV = new Vector2(1, 1), Normal = new Vector2(0, 0), Color = color32, BorderRatio = borderRatio, BorderColor = borderColor32 };

        // 4-7 (top row, outer edge)
        _vertices[_vertexCount++] = new UIVertex { Position = new Vector2(x0, y0), UV = new Vector2(1, 0), Normal = new Vector2(0, r), Color = color32, BorderRatio = borderRatio, BorderColor = borderColor32 };
        _vertices[_vertexCount++] = new UIVertex { Position = new Vector2(x0, y0), UV = new Vector2(0, 0), Normal = new Vector2(r, r), Color = color32, BorderRatio = borderRatio, BorderColor = borderColor32 };
        _vertices[_vertexCount++] = new UIVertex { Position = new Vector2(x1, y0), UV = new Vector2(0, 0), Normal = new Vector2(-r, r), Color = color32, BorderRatio = borderRatio, BorderColor = borderColor32 };
        _vertices[_vertexCount++] = new UIVertex { Position = new Vector2(x1, y0), UV = new Vector2(1, 0), Normal = new Vector2(0, r), Color = color32, BorderRatio = borderRatio, BorderColor = borderColor32 };

        // 8-11 (bottom row, outer edge)
        _vertices[_vertexCount++] = new UIVertex { Position = new Vector2(x0, y1), UV = new Vector2(1, 0), Normal = new Vector2(0, -r), Color = color32, BorderRatio = borderRatio, BorderColor = borderColor32 };
        _vertices[_vertexCount++] = new UIVertex { Position = new Vector2(x0, y1), UV = new Vector2(0, 0), Normal = new Vector2(r, -r), Color = color32, BorderRatio = borderRatio, BorderColor = borderColor32 };
        _vertices[_vertexCount++] = new UIVertex { Position = new Vector2(x1, y1), UV = new Vector2(0, 0), Normal = new Vector2(-r, -r), Color = color32, BorderRatio = borderRatio, BorderColor = borderColor32 };
        _vertices[_vertexCount++] = new UIVertex { Position = new Vector2(x1, y1), UV = new Vector2(1, 0), Normal = new Vector2(0, -r), Color = color32, BorderRatio = borderRatio, BorderColor = borderColor32 };

        // 12-15 (bottom row, inner edge)
        _vertices[_vertexCount++] = new UIVertex { Position = new Vector2(x0, y1), UV = new Vector2(1, 1), Normal = new Vector2(0, 0), Color = color32, BorderRatio = borderRatio, BorderColor = borderColor32 };
        _vertices[_vertexCount++] = new UIVertex { Position = new Vector2(x0, y1), UV = new Vector2(0, 1), Normal = new Vector2(r, 0), Color = color32, BorderRatio = borderRatio, BorderColor = borderColor32 };
        _vertices[_vertexCount++] = new UIVertex { Position = new Vector2(x1, y1), UV = new Vector2(0, 1), Normal = new Vector2(-r, 0), Color = color32, BorderRatio = borderRatio, BorderColor = borderColor32 };
        _vertices[_vertexCount++] = new UIVertex { Position = new Vector2(x1, y1), UV = new Vector2(1, 1), Normal = new Vector2(0, 0), Color = color32, BorderRatio = borderRatio, BorderColor = borderColor32 };

        // top
        AddTriangle(vs, 0, 1, 4);
        AddTriangle(vs, 4, 1, 5);
        AddTriangle(vs, 1, 2, 5);
        AddTriangle(vs, 5, 2, 6);
        AddTriangle(vs, 2, 3, 6);
        AddTriangle(vs, 6, 3, 7);

        // middle
        AddTriangle(vs, 4, 5, 8);
        AddTriangle(vs, 8, 5, 9);
        AddTriangle(vs, 9, 5, 6);
        AddTriangle(vs, 9, 6, 10);
        AddTriangle(vs, 6, 7, 10);
        AddTriangle(vs, 10, 7, 11);

        // bottom
        AddTriangle(vs, 8, 9, 12);
        AddTriangle(vs, 12, 9, 13);
        AddTriangle(vs, 9, 10, 13);
        AddTriangle(vs, 13, 10, 14);
        AddTriangle(vs, 10, 11, 14);
        AddTriangle(vs, 14, 11, 15);
    }

    private static void AddTriangle(int baseVertex, int i0, int i1, int i2)
    {
        _indices[_indexCount++] = (ushort)(baseVertex + i0);
        _indices[_indexCount++] = (ushort)(baseVertex + i1);
        _indices[_indexCount++] = (ushort)(baseVertex + i2);
    }

    public static void Flush()
    {
        if (_vertexCount == 0 || !_initialized || _shader == null)
            return;

        var vertexSpan = MemoryMarshal.AsBytes(_vertices.AsSpan(0, _vertexCount));
        Render.Driver.UpdateVertexBuffer(_vertexBuffer, 0, vertexSpan);
        Render.Driver.UpdateIndexBuffer(_indexBuffer, 0, _indices.AsSpan(0, _indexCount));

        Render.SetVertexBuffer<UIVertex>(_vertexBuffer);
        Render.SetIndexBuffer(_indexBuffer);
        Render.Driver.BindShader(_shader.Handle);

        if (Render.Camera != null)
        {
            var view = Render.Camera.ViewMatrix;
            var projection = new Matrix4x4(
                view.M11, view.M12, 0, view.M31,
                view.M21, view.M22, 0, view.M32,
                0, 0, 1, 0,
                0, 0, 0, 1
            );
            Render.Driver.SetUniformMatrix4x4("u_projection", projection);
        }

        Render.Driver.SetBlendMode(BlendMode.Premultiplied);
        Render.Driver.DrawElements(0, _indexCount, 0);

        _vertexCount = 0;
        _indexCount = 0;
    }
}
