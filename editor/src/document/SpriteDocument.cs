//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Globalization;
using System.Numerics;

namespace NoZ.Editor;

public class SpriteFrame : IDisposable
{
    public readonly Shape Shape = new();
    public int Hold;

    public void Dispose()
    {
        Shape.Dispose();
    }
}

public class SpriteDocument : Document
{
    public class BoneBinding
    {
        public string SkeletonName = "";
        public string BoneName = "";
        public SkeletonDocument? Skeleton;
        public int BoneIndex = -1;
        public Vector2 Offset;

        public bool IsBound => Skeleton != null && BoneIndex >= 0;
        public bool IsBoundTo(SkeletonDocument skeleton) =>
            Skeleton == skeleton && BoneIndex >= 0;

        public void Set(SkeletonDocument? skeleton, int boneIndex)
        {
            if (skeleton == null || boneIndex < 0 || boneIndex >= skeleton.BoneCount)
            {
                Clear();
                return;
            }

            Skeleton = skeleton;
            SkeletonName = skeleton.Name;
            BoneName = skeleton.Bones[boneIndex].Name;
            BoneIndex = boneIndex;
        }

        public void Clear()
        {
            Skeleton = null;
            SkeletonName = "";
            BoneName = "";
            BoneIndex = -1;
            Offset = Vector2.Zero;
        }

        public void CopyFrom(BoneBinding src)
        {
            SkeletonName = src.SkeletonName;
            BoneName = src.BoneName;
            Skeleton = src.Skeleton;
            BoneIndex = src.BoneIndex;
            Offset = src.Offset;
        }

        public void Resolve()
        {
            Skeleton = null;
            BoneIndex = -1;

            if (string.IsNullOrEmpty(SkeletonName))
                return;

            foreach (var doc in DocumentManager.Documents)
            {
                if (doc is SkeletonDocument skel && doc.Name == SkeletonName)
                {
                    Skeleton = skel;
                    if (!string.IsNullOrEmpty(BoneName))
                        BoneIndex = skel.FindBoneIndex(BoneName);
                    break;
                }
            }
        }
    }

    public readonly SpriteFrame[] Frames = new SpriteFrame[Sprite.MaxFrames];
    public ushort FrameCount;
    public byte Palette;
    public float Depth;
    public ushort Order;
    public RectInt RasterBounds { get; private set; }
    public Vector2Int AtlasSize => new(RasterBounds.Size.X * FrameCount, RasterBounds.Size.Y);
    public bool ShowInSkeleton { get; set; }
    public bool ShowTiling { get; set; }
    public bool ShowSkeletonOverlay { get; set; }

    internal AtlasDocument? Atlas;
    internal Rect AtlasUV;

    public readonly BoneBinding Binding = new();

    public SpriteDocument()
    {
        for (var i = 0; i < Frames.Length; i++)
            Frames[i] = new SpriteFrame();
    }

    public static void RegisterDef()
    {
        DocumentManager.RegisterDef(new DocumentDef(
            AssetType.Sprite,
            ".sprite",
            () => new SpriteDocument(),
            doc => new SpriteEditor((SpriteDocument)doc),
            newFile: NewFile
        ));
    }

    public SpriteFrame GetFrame(ushort frameIndex) => Frames[frameIndex];

    private static void NewFile(StreamWriter writer)
    {
        writer.WriteLine("c 0 a");
    }

    public override void Load()
    {
        var contents = File.ReadAllText(Path);
        var tk = new Tokenizer(contents);
        Load(ref tk);
        UpdateBounds();
        Loaded = true;
    }

    private void Load(ref Tokenizer tk)
    {
        var f = Frames[FrameCount++];

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("p"))
            {
                ParsePath(f, ref tk);
            }
            else if (tk.ExpectIdentifier("c"))
            {
                Palette = (byte)tk.ExpectInt();
                IsAntiAliased = tk.ExpectIdentifier("a");
            }
            else if (tk.ExpectIdentifier("o"))
            {
                Order = (ushort)tk.ExpectInt();
            }
            else if (tk.ExpectIdentifier("f"))
            {
                if (tk.ExpectIdentifier("h"))
                    f.Hold = tk.ExpectInt();
                f = Frames[FrameCount++];
            }
            else
            {
                break;
            }
        }
    }

    private static void ParsePath(SpriteFrame f, ref Tokenizer tk)
    {
        var pathIndex = f.Shape.AddPath();
        byte fillColor = 0;
        var position = Vector2.Zero;
        var scale = Vector2.One;
        var opacity = 1.0f;

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("c"))
            {
                fillColor = (byte)tk.ExpectInt();
            }
            else if (tk.ExpectIdentifier("o"))
            {
                opacity = tk.ExpectFloat();
            }
            else if (tk.ExpectIdentifier("h"))
            {
                f.Shape.SetPathSubtract(pathIndex, true);
            }
            else if (tk.ExpectIdentifier("a"))
            {
                ParseAnchor(f.Shape, pathIndex, ref tk);
            }
            else
            {
                break;
            }
        }

        f.Shape.SetPathFillColor(pathIndex, fillColor);
        f.Shape.SetPathFillOpacity(pathIndex, opacity);
    }

    private static void ParseAnchor(Shape shape, ushort pathIndex, ref Tokenizer tk)
    {
        var x = tk.ExpectFloat();
        var y = tk.ExpectFloat();
        var curve = tk.ExpectFloat();
        shape.AddAnchor(pathIndex, new Vector2(x, y), curve);
    }

    public void UpdateBounds()
    {
        if (FrameCount <= 0)
        {
            Bounds = new Rect(-0.5f, -0.5f, 1f, 1f);
            return;
        }

        var bounds = Frames[0].Shape.Bounds;
        for (ushort fi = 1; fi < FrameCount; fi++)
        {
            var fb = Frames[fi].Shape.Bounds;
            var minX = MathF.Min(bounds.X, fb.X);
            var minY = MathF.Min(bounds.Y, fb.Y);
            var maxX = MathF.Max(bounds.Right, fb.Right);
            var maxY = MathF.Max(bounds.Bottom, fb.Bottom);
            bounds = Rect.FromMinMax(new Vector2(minX, minY), new Vector2(maxX, maxY));
        }
        Bounds = bounds;

        if (Bounds.Width <= 0 || Bounds.Height <= 0)
        {
            Bounds = new Rect(-0.5f, -0.5f, 1f, 1f);
            return;
        }

        RasterBounds = Frames[0].Shape.RasterBounds;

        for (ushort fi = 0; fi < FrameCount; fi++)
        {
            Frames[fi].Shape.UpdateSamples();
            Frames[fi].Shape.UpdateBounds();
            RasterBounds = RasterBounds.Union(Frames[fi].Shape.RasterBounds);
        }

        if (IsAntiAliased)
            RasterBounds = RasterBounds.Expand(1);

        Bounds = RasterBounds.ToRect().Scale(1.0f / EditorApplication.Config.PixelsPerUnit);
    }

    // :save
    public override void Save(StreamWriter writer)
    {
        writer.WriteLine($"c {Palette}");
        if (IsAntiAliased)
            writer.WriteLine(" a");
        if (Order > 0)
            writer.WriteLine($"o {Order}");
        writer.WriteLine();

        for (ushort frameIndex = 0; frameIndex < FrameCount; frameIndex++)
        {
            var f = GetFrame(frameIndex);

            if (FrameCount > 1 || f.Hold > 0)
            {
                writer.Write('f');
                if (f.Hold > 0)
                    writer.Write($" h {f.Hold}");
                writer.WriteLine();
            }

            SaveFrame(f, writer);

            if (frameIndex < FrameCount - 1)
                writer.WriteLine();
        }        
    }
    
    private static void SaveFrame(SpriteFrame f, StreamWriter writer)
    {
        var shape = f.Shape;

        for (ushort pIdx = 0; pIdx < shape.PathCount; pIdx++)
        {
            var path = shape.GetPath(pIdx);

            var holeStr = path.IsSubtract ? " h" : "";
            writer.WriteLine($"p c {path.FillColor}{holeStr} o {path.FillOpacity}");

            for (ushort aIdx = 0; aIdx < path.AnchorCount; aIdx++)
            {
                var anchor = shape.GetAnchor((ushort)(path.AnchorStart + aIdx));
                writer.Write(string.Format(CultureInfo.InvariantCulture, "a {0} {1}", anchor.Position.X, anchor.Position.Y));
                if (MathF.Abs(anchor.Curve) > float.Epsilon)
                    writer.Write(string.Format(CultureInfo.InvariantCulture, " {0}", anchor.Curve));
                writer.WriteLine();
            }

            writer.WriteLine();
        }
    }

    public override void Draw()
    {
        DrawOrigin();

        var size = Bounds.Size;
        if (size.X <= 0 || size.Y <= 0 || Atlas == null)
            return;

        ref var frame0 = ref Frames[0];
        if (frame0.Shape.PathCount == 0)
        {
            DrawBounds();
            return;
        }

        DrawSprite();
    }

    public void DrawSprite(in Vector2 offset = default)
    {
        if (Atlas == null) return;

        using (Graphics.PushState())
        {
            Graphics.SetTexture(Atlas.Texture);
            Graphics.SetShader(EditorAssets.Shaders.Texture);
            Graphics.SetColor(Color.White);
            // todo: fix
            //Graphics.SetTextureFilter(IsAntiAliased ? TextureFilter.Linear : TextureFilter.Point);
            Graphics.SetTextureFilter(TextureFilter.Point);
            var bounds = RasterBounds.ToRect().Scale(Graphics.PixelsPerUnitInv);
            Graphics.Draw(
                bounds.Translate(offset),
                AtlasUV,
                order: Order);
        }
    }

    public override void Clone(Document source)
    {
        var src = (SpriteDocument)source;
        FrameCount = src.FrameCount;
        Palette = src.Palette;
        Depth = src.Depth;
        Order = src.Order;
        Bounds = src.Bounds;
        Binding.CopyFrom(src.Binding);

        for (var i = 0; i < src.FrameCount; i++)
        {
            Frames[i].Shape.CopyFrom(src.Frames[i].Shape);
            Frames[i].Hold = src.Frames[i].Hold;
        }

        for (var i = src.FrameCount; i < Sprite.MaxFrames; i++)
            Frames[i].Shape.Clear();
    }

    public override void LoadMetadata(PropertySet meta)
    {
        Binding.SkeletonName = meta.GetString("bone", "skeleton", "");
        Binding.BoneName = meta.GetString("bone", "name", "");
        Binding.Offset = meta.GetVector2("bone", "offset", Vector2.Zero);
        ShowInSkeleton = meta.GetBool("sprite", "show_in_skeleton", false);
        ShowTiling = meta.GetBool("sprite", "show_tiling", false);
        ShowSkeletonOverlay = meta.GetBool("sprite", "show_skeleton_overlay", false);
    }

    public override void SaveMetadata(PropertySet meta)
    {
        meta.SetBool("sprite", "show_in_skeleton", ShowInSkeleton);
        meta.SetBool("sprite", "show_tiling", ShowTiling);
        meta.SetBool("sprite", "show_skeleton_overlay", ShowSkeletonOverlay);
        if (Binding.IsBound)
        {
            meta.SetString("bone", "skeleton", Binding.SkeletonName);
            meta.SetString("bone", "name", Binding.BoneName);
            meta.SetVec2("bone", "offset", Binding.Offset);
        }
        else
        {
            meta.ClearGroup("bone");
        }
    }

    public override void PostLoad()
    {
        Binding.Resolve();
    }

    public void SetBoneBinding(SkeletonDocument? skeleton, int boneIndex)
    {
        Binding.Set(skeleton, boneIndex);
        MarkMetaModified();
    }

    public void ClearBoneBinding()
    {
        var skeleton = Binding.Skeleton;
        Binding.Clear();
        skeleton?.UpdateSprites();
        MarkMetaModified();
    }

    public override void Import(string outputPath, PropertySet config, PropertySet meta)
    {
        UpdateBounds();

        using var writer = new BinaryWriter(File.Create(outputPath));
        writer.WriteAssetHeader(AssetType.Sprite, Sprite.Version, 0);
        writer.Write(FrameCount);
        writer.Write((ushort)(Atlas?.Index ?? 0));
        writer.Write((short)RasterBounds.Left);
        writer.Write((short)RasterBounds.Top);
        writer.Write((short)RasterBounds.Right);
        writer.Write((short)RasterBounds.Bottom);
        writer.Write(AtlasUV.Left);
        writer.Write(AtlasUV.Top);
        writer.Write(AtlasUV.Right);
        writer.Write(AtlasUV.Bottom);
        writer.Write((float)EditorApplication.Config.PixelsPerUnit);
        writer.Write((byte)(IsAntiAliased ? TextureFilter.Linear : TextureFilter.Point));
        writer.Write(Order);
        writer.Write(Binding.Offset.X);
        writer.Write(Binding.Offset.Y);
    }

    public override void OnUndoRedo()
    {
        UpdateBounds();
        AtlasManager.UpdateSprite(this);
        base.OnUndoRedo();
    }
}
