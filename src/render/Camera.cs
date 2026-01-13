//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace noz;

public class Camera
{
    private Vector4 _extents = new(-1, 1, -1, 1);
    private Matrix3x2 _view;
    private Matrix3x2 _invView;
    private Vector2Int _screenSize;
    private Rect _bounds;
    private Vector2 _shakeOffset;
    private Vector2 _shakeIntensity;
    private Vector2 _shakeNoise;
    private float _shakeDuration;
    private float _shakeElapsed;
    private Rect _viewport;
    private Func<Camera, Vector2Int, Rect>? _updateFunc;

    public Vector2 Position { get; set; } = Vector2.Zero;

    public float Rotation { get; set; } = 0;

    public Matrix3x2 ViewMatrix => _view;
    public Vector2Int ScreenSize => _screenSize;
    public Rect WorldBounds => _bounds;
    public Vector2 WorldSize => _bounds.Size;
    public Rect Viewport
    {
        get => _viewport;
        set => _viewport = value;
    }

    public void SetSize(float width, float height)
    {
        var hw = MathF.Abs(width) * 0.5f;
        var hh = MathF.Abs(height) * 0.5f;

        if (width == 0 && height == 0)
            _extents = new Vector4(float.MaxValue, float.MaxValue, float.MaxValue, float.MaxValue);
        else if (width == 0)
            _extents = new Vector4(float.MaxValue, float.MaxValue, -hh, hh);
        else if (height == 0)
            _extents = new Vector4(-hw, hw, float.MaxValue, float.MaxValue);
        else
            _extents = new Vector4(-hw, hw, -hh, hh);
    }

    public void SetSize(Vector2 size) => SetSize(size.X, size.Y);

    public void SetExtents(float left, float right, float bottom, float top)
    {
        _extents = new Vector4(left, right, bottom, top);
    }

    public void SetUpdateFunc(Func<Camera, Vector2Int, Rect>? func)
    {
        _updateFunc = func;
    }

    public void Shake(Vector2 intensity, float duration)
    {
        _shakeDuration = duration;
        _shakeElapsed = 0;
        _shakeIntensity = intensity;
        _shakeNoise = new Vector2(
            Random.Shared.NextSingle() * 100f,
            Random.Shared.NextSingle() * 100f
        );
    }

    public void Shake(float intensity, float duration) => Shake(new Vector2(intensity, intensity), duration);

    public void Update()
    {
        Update(Application.WindowSize.ToVector2Int());
    }

    public void Update(Vector2Int availableSize)
    {
        UpdateShake();

        _screenSize = availableSize;

        if (_updateFunc != null)
        {
            _bounds = _updateFunc(this, availableSize);
        }
        else
        {
            var left = _extents.X;
            var right = _extents.Y;
            var bottom = _extents.Z;
            var top = _extents.W;

            var screenAspect = (float)_screenSize.X / _screenSize.Y;

            var isAutoWidth = MathF.Abs(left) >= float.MaxValue || MathF.Abs(right) >= float.MaxValue;
            var isAutoHeight = MathF.Abs(bottom) >= float.MaxValue || MathF.Abs(top) >= float.MaxValue;

            if (isAutoWidth && isAutoHeight)
            {
                // Default to height of 2 units, auto width
                var height = 2f;
                var width = height * screenAspect;
                left = -width * 0.5f;
                right = width * 0.5f;
                bottom = -height * 0.5f;
                top = height * 0.5f;
            }
            else if (isAutoWidth)
            {
                var height = top - bottom;
                var width = MathF.Abs(height) * screenAspect;
                left = -width * 0.5f;
                right = width * 0.5f;
            }
            else if (isAutoHeight)
            {
                var width = right - left;
                var height = width / screenAspect;
                bottom = -height * 0.5f;
                top = height * 0.5f;
            }

            _bounds = Rect.FromMinMax(new Vector2(left, bottom), new Vector2(right, top));
        }

        // Apply position and shake
        _bounds = _bounds.Translate(Position + _shakeOffset);

        UpdateViewMatrix();
    }

    public Vector2 ScreenToWorld(Vector2 screenPos)
    {
        Vector2 localPos = screenPos;
        Vector2 viewportSize;

        if (_viewport.Width > 0 && _viewport.Height > 0)
        {
            localPos.X -= _viewport.X;
            localPos.Y -= _viewport.Y;
            viewportSize = new Vector2(_viewport.Width, _viewport.Height);
        }
        else
        {
            viewportSize = new Vector2(_screenSize.X, _screenSize.Y);
        }

        // Convert to NDC (-1 to 1)
        Vector2 ndc = new(
            localPos.X / viewportSize.X * 2f - 1f,
            localPos.Y / viewportSize.Y * 2f - 1f
        );

        // Transform by inverse view matrix
        return Vector2.Transform(ndc, _invView);
    }

    public Vector2 WorldToScreen(Vector2 worldPos)
    {
        // Transform by view matrix
        Vector2 ndc = Vector2.Transform(worldPos, _view);

        Vector2 viewportSize;
        Vector2 viewportOffset = Vector2.Zero;

        if (_viewport.Width > 0 && _viewport.Height > 0)
        {
            viewportSize = new Vector2(_viewport.Width, _viewport.Height);
            viewportOffset = new Vector2(_viewport.X, _viewport.Y);
        }
        else
        {
            viewportSize = new Vector2(_screenSize.X, _screenSize.Y);
        }

        // Convert from NDC to screen
        return new Vector2(
            (ndc.X + 1f) * 0.5f * viewportSize.X + viewportOffset.X,
            (ndc.Y + 1f) * 0.5f * viewportSize.Y + viewportOffset.Y
        );
    }

    private void UpdateShake()
    {
        if (_shakeDuration <= 0)
            return;

        _shakeElapsed += Time.DeltaTime;
        var t = _shakeElapsed / _shakeDuration;

        if (t >= 1f)
        {
            _shakeDuration = 0;
            _shakeOffset = Vector2.Zero;
            return;
        }

        // Use simple noise approximation (TODO: implement proper Perlin noise)
        var noiseX = MathF.Sin(_shakeNoise.X + t * 20f) * 0.5f;
        var noiseY = MathF.Sin(_shakeNoise.Y + t * 20f) * 0.5f;

        _shakeOffset = new Vector2(
            _shakeIntensity.X * noiseX,
            _shakeIntensity.Y * noiseY
        ) * (1f - t);  // Decay over time
    }

    private void UpdateViewMatrix()
    {
        var zoomX = 2f / MathF.Abs(_bounds.Width);
        var zoomY = 2f / MathF.Abs(_bounds.Height);
        var center = _bounds.Center;
        var c = MathF.Cos(Rotation);
        var s = MathF.Sin(Rotation);

        // Matrix3x2 constructor: (m11, m12, m21, m22, m31, m32)
        // Row 0: (c*zoomX, -s*zoomY)  Row 1: (s*zoomX, c*zoomY)  Row 2: (transX, transY)
        _view = new Matrix3x2(
            c * zoomX, -s * zoomY,
            s * zoomX, c * zoomY,
            -(c * center.X + s * center.Y) * zoomX,
            -(-s * center.X + c * center.Y) * zoomY
        );

        Matrix3x2.Invert(_view, out _invView);
    }
}
