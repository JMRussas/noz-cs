//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Globalization;
using System.Numerics;

namespace NoZ.Editor;

[Flags]
public enum AnimationFlags : byte
{
    None = 0,
    Looping = 1 << 0,
    RootMotion = 1 << 1
}

public class AnimationBoneData
{
    public string Name = "";
    public int Index;
    public bool IsSelected;
    public BoneTransform SavedTransform;

    public AnimationBoneData()
    {
        SavedTransform = BoneTransform.Identity;
    }
}

public class AnimationFrameData
{
    public BoneTransform[] Transforms = new BoneTransform[Skeleton.MaxBones];
    public string? EventName;
    public int Hold;

    public AnimationFrameData()
    {
        for (var i = 0; i < Transforms.Length; i++)
            Transforms[i] = BoneTransform.Identity;
    }
}

internal class AnimationDocument : Document
{
    public const int MaxFrames = 64;
    private const float BoundsPadding = 0.1f;
    private const float BoneWidth = 0.15f;

    public readonly AnimationBoneData[] Bones = new AnimationBoneData[NoZ.Skeleton.MaxBones];
    public readonly AnimationFrameData[] Frames = new AnimationFrameData[MaxFrames];
    
    public NativeArray<Matrix3x2> LocalToWorld { get; private set; }

    public string? SkeletonName;
    public SkeletonDocument? Skeleton;
    public int FrameCount;
    public int CurrentFrame;
    public int BoneCount;
    public int SelectedBoneCount;
    public AnimationFlags Flags;

    private bool _isPlaying;
    private float _playTime;

    public override bool IsPlaying => _isPlaying;
    public override bool CanPlay => true;

    public bool IsLooping
    {
        get => (Flags & AnimationFlags.Looping) != 0;
        set
        {
            if (value)
                Flags |= AnimationFlags.Looping;
            else
                Flags &= ~AnimationFlags.Looping;
        }
    }
    public bool IsRootMotion
    {
        get => (Flags & AnimationFlags.RootMotion) != 0;
        set
        {
            if (value)
                Flags |= AnimationFlags.RootMotion;
            else
                Flags &= ~AnimationFlags.RootMotion;
        }
    }

    public AnimationDocument()
    {
        LocalToWorld = new NativeArray<Matrix3x2>(NoZ.Skeleton.MaxBones);

        for (var i = 0; i < NoZ.Skeleton.MaxBones; i++)
            Bones[i] = new AnimationBoneData();

        for (var i = 0; i < MaxFrames; i++)
            Frames[i] = new AnimationFrameData();
    }

    static AnimationDocument()
    {
        SkeletonDocument.BoneRenamed += OnSkeletonBoneRenamed;
        SkeletonDocument.BoneRemoved += OnSkeletonBoneRemoved;
        SkeletonDocument.BoneAdded += OnSkeletonBoneAdded;
        SkeletonDocument.TransformsChanged += OnSkeletonTransformsChanged;
    }

    private static void OnSkeletonTransformsChanged(SkeletonDocument skeleton)
    {
        foreach (var doc in DocumentManager.Documents.OfType<AnimationDocument>())
        {
            if (doc.Skeleton != skeleton)
                continue;
            doc.RebuildLocalToWorld();
        }
    }

    public static void RegisterDef()
    {
        DocumentManager.RegisterDef(new DocumentDef
        {
            Type = AssetType.Animation,
            Extension = ".anim",
            Factory = () => new AnimationDocument(),
            EditorFactory = doc => new AnimationEditor((AnimationDocument)doc),
            NewFile = NewFile,
            Icon = () => EditorAssets.Sprites.AssetIconAnimation
        });
    }

    private static void OnSkeletonBoneAdded(SkeletonDocument skeleton, int boneIndex)
    {
        foreach (var doc in DocumentManager.Documents.OfType<AnimationDocument>())
        {
            if (doc.Skeleton != skeleton)
                continue;

            if (boneIndex < NoZ.Skeleton.MaxBones)
            {
                doc.Bones[boneIndex].Name = skeleton.Bones[boneIndex].Name;
                doc.Bones[boneIndex].Index = boneIndex;
                for (var f = 0; f < doc.FrameCount; f++)
                    doc.Frames[f].Transforms[boneIndex] = BoneTransform.Identity;
            }

            doc.BoneCount = skeleton.BoneCount;
            doc.RebuildLocalToWorld();
        }
    }

    private static void OnSkeletonBoneRenamed(SkeletonDocument skeleton, int boneIndex, string oldName, string newName)
    {
        foreach (var doc in DocumentManager.Documents.OfType<AnimationDocument>())
        {
            if (doc.Skeleton != skeleton)
                continue;
            if (boneIndex >= 0 && boneIndex < doc.BoneCount && doc.Bones[boneIndex].Name == oldName)
            {
                doc.Bones[boneIndex].Name = newName;
                doc.MarkModified();
            }
        }
    }

    private static void OnSkeletonBoneRemoved(SkeletonDocument skeleton, int removedIndex, string removedName)
    {
        foreach (var doc in DocumentManager.Documents.OfType<AnimationDocument>())
        {
            if (doc.Skeleton != skeleton)
                continue;

            if (removedIndex < doc.BoneCount)
            {
                for (var i = removedIndex; i < doc.BoneCount - 1; i++)
                {
                    doc.Bones[i].Name = doc.Bones[i + 1].Name;
                    doc.Bones[i].Index = i;
                    doc.Bones[i].IsSelected = doc.Bones[i + 1].IsSelected;
                    for (var f = 0; f < doc.FrameCount; f++)
                        doc.Frames[f].Transforms[i] = doc.Frames[f].Transforms[i + 1];
                }
                doc.BoneCount--;
                doc.MarkModified();
                Notifications.Add($"Animation '{doc.Name}' updated (bone '{removedName}' removed)");
            }

            doc.BoneCount = skeleton.BoneCount;
            doc.RebuildLocalToWorld();
        }
    }

    public ref BoneTransform GetFrameTransform(int boneIndex, int frameIndex)
    {
        return ref Frames[frameIndex].Transforms[boneIndex];
    }

    public int GetFrameCountWithHolds()
    {
        var count = 0;
        for (var i = 0; i < FrameCount; i++)
        {
            count++;
            count += Frames[i].Hold;
        }
        return count;
    }

    public int GetFrameIndexWithHolds(int frameIndex)
    {
        var frameIndexWithHolds = 0;
        for (var i = 0; i < frameIndex; i++)
        {
            frameIndexWithHolds++;
            frameIndexWithHolds += Frames[i].Hold;
        }
        return frameIndexWithHolds;
    }

    public int GetRealFrameIndex(int frameIndex)
    {
        for (var i = 0; i < FrameCount; i++)
        {
            for (var h = 0; h <= Frames[i].Hold; h++, frameIndex--)
            {
                if (frameIndex == 0)
                    return i;
            }
        }
        return FrameCount;
    }

    public void UpdateTransforms(int frameIndex = -1)
    {
        if (frameIndex == -1)
            frameIndex = CurrentFrame;

        if (Skeleton == null || frameIndex < 0 || frameIndex >= FrameCount)
            return;

        LocalToWorld.Clear();
        LocalToWorld.AddRange(Skeleton.BoneCount);

        for (var boneIndex = 0; boneIndex < Skeleton.BoneCount; boneIndex++)
        {
            var bone = Skeleton.Bones[boneIndex];
            ref var frame = ref GetFrameTransform(boneIndex, frameIndex);

            LocalToWorld[boneIndex] = CreateTRS(
                bone.Transform.Position + frame.Position,
                bone.Transform.Rotation + frame.Rotation,
                Vector2.One
            );
        }

        for (var boneIndex = 1; boneIndex < Skeleton.BoneCount; boneIndex++)
        {
            var parentIndex = Skeleton.Bones[boneIndex].ParentIndex;
            LocalToWorld[boneIndex] = LocalToWorld[boneIndex] * LocalToWorld[parentIndex];
        }
    }

    public void RebuildLocalToWorld()
    {
        if (Skeleton == null)
            return;

        LocalToWorld.Clear();
        LocalToWorld.AddRange(Skeleton.BoneCount);

        for (var boneIndex = 0; boneIndex < Skeleton.BoneCount; boneIndex++)
            LocalToWorld[boneIndex] = Matrix3x2.Identity;

        if (FrameCount > 0 && CurrentFrame >= 0 && CurrentFrame < FrameCount)
            UpdateTransforms(CurrentFrame);
    }

    public void UpdateTransformsInterpolated(int frame0, int frame1, float t)
    {
        if (Skeleton == null)
            return;

        frame0 = Math.Clamp(frame0, 0, FrameCount - 1);
        frame1 = Math.Clamp(frame1, 0, FrameCount - 1);

        LocalToWorld.Clear();
        LocalToWorld.AddRange(Skeleton.BoneCount);

        for (var boneIndex = 0; boneIndex < Skeleton.BoneCount; boneIndex++)
        {
            var bone = Skeleton.Bones[boneIndex];
            ref var t0 = ref GetFrameTransform(boneIndex, frame0);
            ref var t1 = ref GetFrameTransform(boneIndex, frame1);

            var position = Vector2.Lerp(t0.Position, t1.Position, t);
            var rotation = MathEx.LerpAngle(t0.Rotation, t1.Rotation, t);

            LocalToWorld[boneIndex] = CreateTRS(
                bone.Transform.Position + position,
                bone.Transform.Rotation + rotation,
                Vector2.One
            );
        }

        for (var boneIndex = 1; boneIndex < Skeleton.BoneCount; boneIndex++)
        {
            var parentIndex = Skeleton.Bones[boneIndex].ParentIndex;
            LocalToWorld[boneIndex] = LocalToWorld[boneIndex] * LocalToWorld[parentIndex];
        }
    }

    public void UpdateBounds()
    {
        if (Skeleton == null)
            return;

        var rootPosition = Vector2.Transform(Vector2.Zero, LocalToWorld[0]);
        var bounds = new Rect(rootPosition.X, rootPosition.Y, 0, 0);

        for (var boneIndex = 0; boneIndex < BoneCount; boneIndex++)
        {
            var bone = Skeleton.Bones[boneIndex];
            var boneWidth = bone.Length * BoneWidth;
            var boneTransform = LocalToWorld[boneIndex];

            bounds = ExpandBounds(bounds, Vector2.Transform(Vector2.Zero, boneTransform));
            bounds = ExpandBounds(bounds, Vector2.Transform(new Vector2(bone.Length, 0), boneTransform));
            bounds = ExpandBounds(bounds, Vector2.Transform(new Vector2(boneWidth, boneWidth), boneTransform));
            bounds = ExpandBounds(bounds, Vector2.Transform(new Vector2(boneWidth, -boneWidth), boneTransform));
        }

        var sprites = Skeleton.Sprites;
        for (var i = 0; i < sprites.Count; i++)
        {
            var sprite = sprites[i];
            var spriteBounds = sprite.Bounds.Translate(-sprite.Binding.Offset);
            var boneTransform = Skeleton.WorldToLocal[sprite.Binding.BoneIndex] * LocalToWorld[sprite.Binding.BoneIndex];
            bounds = ExpandBounds(bounds, Vector2.Transform(spriteBounds.TopLeft, boneTransform));
            bounds = ExpandBounds(bounds, Vector2.Transform(spriteBounds.TopRight, boneTransform));
            bounds = ExpandBounds(bounds, Vector2.Transform(spriteBounds.BottomLeft, boneTransform));
            bounds = ExpandBounds(bounds, Vector2.Transform(spriteBounds.BottomRight, boneTransform));
        }

        Bounds = bounds.Expand(BoundsPadding);
    }

    private static Rect ExpandBounds(Rect bounds, Vector2 point)
    {
        var minX = MathF.Min(bounds.Left, point.X);
        var minY = MathF.Min(bounds.Top, point.Y);
        var maxX = MathF.Max(bounds.Right, point.X);
        var maxY = MathF.Max(bounds.Bottom, point.Y);
        return Rect.FromMinMax(new Vector2(minX, minY), new Vector2(maxX, maxY));
    }

    private static Matrix3x2 CreateTRS(Vector2 position, float rotationDegrees, Vector2 scale)
    {
        var rotation = rotationDegrees * MathF.PI / 180f;
        return Matrix3x2.CreateScale(scale) *
               Matrix3x2.CreateRotation(rotation) *
               Matrix3x2.CreateTranslation(position);
    }

    public void UpdateSkeleton()
    {
        if (Skeleton == null)
            return;

        var boneMap = new int[NoZ.Skeleton.MaxBones];
        for (var i = 0; i < NoZ.Skeleton.MaxBones; i++)
            boneMap[i] = -1;

        for (var i = 0; i < BoneCount; i++)
        {
            var newBoneIndex = Skeleton.FindBoneIndex(Bones[i].Name);
            if (newBoneIndex == -1)
                continue;
            boneMap[newBoneIndex] = i;
        }

        for (var i = 0; i < BoneCount; i++)
        {
            var ab = Bones[i];
            ab.Index = i;
            ab.Name = Bones[i].Name;
        }

        UpdateBounds();
        UpdateTransforms();
    }

    public int InsertFrame(int insertAt)
    {
        if (FrameCount >= MaxFrames)
            return -1;

        FrameCount++;
        var copyFrame = Math.Max(0, insertAt - 1);

        for (var frameIndex = FrameCount - 1; frameIndex > insertAt; frameIndex--)
        {
            Frames[frameIndex].Hold = Frames[frameIndex - 1].Hold;
            Frames[frameIndex].EventName = Frames[frameIndex - 1].EventName;
            for (var boneIndex = 0; boneIndex < NoZ.Skeleton.MaxBones; boneIndex++)
                Frames[frameIndex].Transforms[boneIndex] = Frames[frameIndex - 1].Transforms[boneIndex];
        }

        if (copyFrame >= 0 && Skeleton != null)
        {
            for (var j = 0; j < Skeleton.BoneCount; j++)
                GetFrameTransform(j, insertAt) = GetFrameTransform(j, copyFrame);
        }

        Frames[insertAt].Hold = 0;
        Frames[insertAt].EventName = null;

        return insertAt;
    }

    public int DeleteFrame(int frameIndex)
    {
        if (FrameCount <= 1)
            return frameIndex;

        for (var i = frameIndex; i < FrameCount - 1; i++)
        {
            Frames[i].Hold = Frames[i + 1].Hold;
            Frames[i].EventName = Frames[i + 1].EventName;
            for (var boneIndex = 0; boneIndex < NoZ.Skeleton.MaxBones; boneIndex++)
                Frames[i].Transforms[boneIndex] = Frames[i + 1].Transforms[boneIndex];
        }

        FrameCount--;
        return Math.Min(frameIndex, FrameCount - 1);
    }

    public void SetLooping(bool looping)
    {
        if (looping)
            Flags |= AnimationFlags.Looping;
        else
            Flags &= ~AnimationFlags.Looping;
        MarkMetaModified();
    }

    public void SetRootMotion(bool rootMotion)
    {
        if (rootMotion)
            Flags |= AnimationFlags.RootMotion;
        else
            Flags &= ~AnimationFlags.RootMotion;
        MarkMetaModified();
    }

    public int HitTestBones(
        Matrix3x2 transform,
        Vector2 position,
        Span<int> bones,
        int maxBones = NoZ.Skeleton.MaxBones)
    {
        if (Skeleton == null)
            return 0;

        var hitCount = 0;
        for (var boneIndex = Skeleton.BoneCount - 1; boneIndex >= 0 && hitCount < maxBones; boneIndex--)
        {
            var bone = Skeleton.Bones[boneIndex];
            var localToWorld = LocalToWorld[boneIndex] * transform;
            var boneStart = Vector2.Transform(Vector2.Zero, localToWorld);
            var boneEnd = Vector2.Transform(new Vector2(bone.Length, 0), localToWorld);

            if (!PointNearLine(position, boneStart, boneEnd, EditorStyle.Skeleton.BoneSize * Gizmos.ZoomRefScale * 2f))
                continue;

            bones[hitCount++] = boneIndex;
        }

        return hitCount;
    }

    public int HitTestBone(Matrix3x2 transform, Vector2 position)
    {
        var bones = new int[1];
        if (HitTestBones(transform, position, bones, 1) == 0)
            return -1;
        return bones[0];
    }

    private static bool PointNearLine(Vector2 point, Vector2 lineStart, Vector2 lineEnd, float threshold)
    {
        return DistanceFromLine(lineStart, lineEnd, point) <= threshold;
    }

    private static float DistanceFromLine(Vector2 lineStart, Vector2 lineEnd, Vector2 point)
    {
        var line = lineEnd - lineStart;
        var lineLength = line.Length();
        if (lineLength < 0.0001f)
            return Vector2.Distance(point, lineStart);

        var t = MathF.Max(0, MathF.Min(1, Vector2.Dot(point - lineStart, line) / (lineLength * lineLength)));
        var projection = lineStart + t * line;
        return Vector2.Distance(point, projection);
    }

    public override void Load()
    {
        FrameCount = 0;
        var contents = File.ReadAllText(Path);
        var tk = new Tokenizer(contents);

        var boneMap = new int[NoZ.Skeleton.MaxBones];
        for (var i = 0; i < NoZ.Skeleton.MaxBones; i++)
            boneMap[i] = -1;

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("s"))
                ParseSkeleton(ref tk, boneMap);
            else if (tk.ExpectIdentifier("f"))
                ParseFrame(ref tk, boneMap);
            else
                break;
        }

        if (FrameCount == 0)
        {
            for (var i = 0; i < NoZ.Skeleton.MaxBones; i++)
                Frames[0].Transforms[i] = BoneTransform.Identity;
            FrameCount = 1;
        }

        Bounds = new Rect(-1, -1, 2, 2);
        Loaded = true;
    }

    private void ParseSkeleton(ref Tokenizer tk, int[] boneMap)
    {
        if (!tk.ExpectQuotedString(out var skeletonName))
            throw new Exception("Missing quoted skeleton name");

        SkeletonName = skeletonName;
        Skeleton = DocumentManager.Find(AssetType.Skeleton, SkeletonName) as SkeletonDocument;
        if (Skeleton == null)
            return;

        if (!Skeleton.Loaded)
        {
            Skeleton.Load();
            Skeleton.LoadMetadata();
        }

        for (var i = 0; i < Skeleton.BoneCount; i++)
        {
            var bone = Skeleton.Bones[i];
            Bones[i].Name = bone.Name;
            Bones[i].Index = i;
        }

        BoneCount = Skeleton.BoneCount;

        for (var frameIndex = 0; frameIndex < MaxFrames; frameIndex++)
            for (var boneIndex = 0; boneIndex < NoZ.Skeleton.MaxBones; boneIndex++)
                Frames[frameIndex].Transforms[boneIndex] = BoneTransform.Identity;

        var boneIndex2 = 0;
        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("b"))
                ParseSkeletonBone(ref tk, boneIndex2++, boneMap);
            else
                break;
        }
    }

    private void ParseSkeletonBone(ref Tokenizer tk, int boneIndex, int[] boneMap)
    {
        if (!tk.ExpectQuotedString(out var boneName))
            throw new Exception("Missing quoted bone name");

        if (Skeleton == null)
            return;

        boneMap[boneIndex] = Skeleton.FindBoneIndex(boneName);
    }

    private void ParseFrame(ref Tokenizer tk, int[] boneMap)
    {
        var boneIndex = -1;
        FrameCount++;
        var frameIndex = FrameCount - 1;

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("b"))
            {
                boneIndex = ParseFrameBone(ref tk, boneMap);
            }
            else if (tk.ExpectIdentifier("e"))
            {
                ParseFrameEvent(ref tk, frameIndex);
            }
            else if (tk.ExpectIdentifier("r"))
            {
                ParseFrameRotation(ref tk, boneIndex, frameIndex);
            }
            else if (tk.ExpectIdentifier("s"))
            {
                ParseFrameScale(ref tk, boneIndex, frameIndex);
            }
            else if (tk.ExpectIdentifier("p"))
            {
                ParseFramePosition(ref tk, boneIndex, frameIndex);
            }
            else if (tk.ExpectIdentifier("h"))
            {
                ParseFrameHold(ref tk, frameIndex);
            }
            else
            {
                break;
            }
        }
    }

    private static int ParseFrameBone(ref Tokenizer tk, int[] boneMap)
    {
        if (!tk.ExpectInt(out var boneIndex))
            throw new Exception("Expected bone index");

        return boneMap[boneIndex];
    }

    private void ParseFrameEvent(ref Tokenizer tk, int frameIndex)
    {
        if (!tk.ExpectQuotedString(out var eventName))
            throw new Exception("Expected event name");

        Frames[frameIndex].EventName = eventName;
    }

    private void ParseFramePosition(ref Tokenizer tk, int boneIndex, int frameIndex)
    {
        if (!tk.ExpectFloat(out var x))
            throw new Exception("Expected position 'x' value");
        if (!tk.ExpectFloat(out var y))
            throw new Exception("Expected position 'y' value");

        if (boneIndex == -1)
            return;

        GetFrameTransform(boneIndex, frameIndex).Position = new Vector2(x, y);
    }

    private void ParseFrameRotation(ref Tokenizer tk, int boneIndex, int frameIndex)
    {
        if (!tk.ExpectFloat(out var r))
            throw new Exception("Expected rotation value");

        if (boneIndex == -1)
            return;

        GetFrameTransform(boneIndex, frameIndex).Rotation = r;
    }

    private void ParseFrameScale(ref Tokenizer tk, int boneIndex, int frameIndex)
    {
        if (!tk.ExpectFloat(out var s))
            throw new Exception("Expected scale value");

        if (boneIndex == -1)
            return;

        GetFrameTransform(boneIndex, frameIndex).Scale = new Vector2(s, s);
    }

    private void ParseFrameHold(ref Tokenizer tk, int frameIndex)
    {
        if (!tk.ExpectInt(out var hold))
            throw new Exception("Expected hold value");

        Frames[frameIndex].Hold = Math.Max(0, hold);
    }

    public override void PostLoad()
    {
        Skeleton = DocumentManager.Find(AssetType.Skeleton, SkeletonName ?? "") as SkeletonDocument;
        if (Skeleton == null)
            return;

        if (!Skeleton.PostLoaded)
        {
            Skeleton.PostLoad();
            Skeleton.PostLoaded = true;
        }

        Skeleton.UpdateTransforms();
        UpdateTransforms();
        UpdateBounds();
    }

    public override void Save(StreamWriter writer)
    {
        if (Skeleton == null)
            return;

        writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "s \"{0}\"", SkeletonName));

        for (var i = 0; i < Skeleton.BoneCount; i++)
        {
            var bone = Bones[i];
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture, "b \"{0}\"", bone.Name));
        }

        for (var frameIndex = 0; frameIndex < FrameCount; frameIndex++)
        {
            var f = Frames[frameIndex];
            var line = "f";

            if (f.Hold > 0)
                line += string.Format(CultureInfo.InvariantCulture, " h {0}", f.Hold);
            if (!string.IsNullOrEmpty(f.EventName))
                line += string.Format(CultureInfo.InvariantCulture, " e \"{0}\"", f.EventName);

            writer.WriteLine(line);

            for (var boneIndex = 0; boneIndex < Skeleton.BoneCount; boneIndex++)
            {
                ref var bt = ref GetFrameTransform(boneIndex, frameIndex);

                var hasPos = bt.Position != Vector2.Zero;
                var hasRot = bt.Rotation != 0f;

                if (!hasPos && !hasRot)
                    continue;

                var boneLine = string.Format(CultureInfo.InvariantCulture, "b {0}", boneIndex);

                if (hasPos)
                    boneLine += string.Format(CultureInfo.InvariantCulture, " p {0} {1}", bt.Position.X, bt.Position.Y);

                if (hasRot)
                    boneLine += string.Format(CultureInfo.InvariantCulture, " r {0}", bt.Rotation);

                writer.WriteLine(boneLine);
            }
        }
    }

    public override void LoadMetadata(PropertySet meta)
    {
        Flags = AnimationFlags.None;
        if (meta.GetBool("animation", "loop", true))
            Flags |= AnimationFlags.Looping;
        if (meta.GetBool("animation", "root_motion", false))
            Flags |= AnimationFlags.RootMotion;
    }

    public override void SaveMetadata(PropertySet meta)
    {
        meta.SetBool("animation", "loop", IsLooping);
        meta.SetBool("animation", "root_motion", IsRootMotion);
    }

    public override void Clone(Document source)
    {
        var src = (AnimationDocument)source;

        SkeletonName = src.SkeletonName;
        Skeleton = src.Skeleton;
        FrameCount = src.FrameCount;
        CurrentFrame = src.CurrentFrame;
        BoneCount = src.BoneCount;
        SelectedBoneCount = src.SelectedBoneCount;
        Flags = src.Flags;

        LocalToWorld.Dispose();
        LocalToWorld = new NativeArray<Matrix3x2>(src.LocalToWorld);

        for (var i = 0; i < BoneCount; i++)
        {
            Bones[i].Name = src.Bones[i].Name;
            Bones[i].Index = src.Bones[i].Index;
            Bones[i].IsSelected = src.Bones[i].IsSelected;
            Bones[i].SavedTransform = src.Bones[i].SavedTransform;
            LocalToWorld[i] = src.LocalToWorld[i];
        }

        for (var i = 0; i < FrameCount; i++)
        {
            Frames[i].Hold = src.Frames[i].Hold;
            Frames[i].EventName = src.Frames[i].EventName;
            for (var j = 0; j < BoneCount; j++)
                Frames[i].Transforms[j] = src.Frames[i].Transforms[j];
        }

        if (Skeleton != null)
        {
            UpdateTransforms();
            UpdateBounds();
        }
    }

    public override void OnUndoRedo()
    {
        UpdateSkeleton();
        UpdateTransforms();
    }

    public override void Draw()
    {
        if (Skeleton == null)
        {
            DrawOrigin();
            return;
        }

        using (Gizmos.PushState(EditorLayer.Document))
        {
            DrawBones();
            DrawOrigin();
            Graphics.SetSortGroup(0);
            DrawSprites();
        }
    }

    private void DrawBones()
    {
        if (Skeleton == null) return;

        var transform = Transform;
        Graphics.SetTransform(transform);
        Graphics.SetSortGroup(1);
        Gizmos.SetColor(EditorStyle.Skeleton.BoneColor);

        for (var boneIndex = 0; boneIndex < Skeleton.BoneCount; boneIndex++)
        {
            var b = Skeleton.Bones[boneIndex];
            ref readonly var boneTransform = ref LocalToWorld[boneIndex];
            var p0 = Vector2.Transform(Vector2.Zero, boneTransform);
            var p1 = Vector2.Transform(new Vector2(b.Length, 0), boneTransform);
            Gizmos.DrawBone(p0, p1);
        }
    }

    public void DrawSprites()
    {
        if (Skeleton == null) return;

        using (Graphics.PushState())
        {
            Graphics.SetLayer(EditorLayer.Document);

            for (var i = 0; i < Skeleton.Sprites.Count; i++)
            {
                var sprite = Skeleton.Sprites[i];
                Debug.Assert(sprite != null);
                Debug.Assert(sprite.Binding.IsBoundTo(Skeleton));

                ref readonly var bindPose = ref Skeleton.WorldToLocal[sprite.Binding.BoneIndex];
                ref readonly var animatedPose = ref LocalToWorld[sprite.Binding.BoneIndex];
                Graphics.SetTransform(bindPose * animatedPose * Transform);
                sprite.DrawSprite(-sprite.Binding.Offset);
            }
        }
    }

    public override void Play()
    {
        _isPlaying = true;
        _playTime = 0;
    }

    public override void Stop()
    {
        _isPlaying = false;
        UpdateTransforms(CurrentFrame);
    }

    public void UpdatePlayback(float deltaTime)
    {
        if (!_isPlaying || Skeleton == null)
            return;

        const float frameRate = 12f;
        _playTime += deltaTime;

        var totalFrames = GetFrameCountWithHolds();
        if (totalFrames <= 0)
            return;

        var duration = totalFrames / frameRate;

        if (IsLooping)
        {
            _playTime %= duration;
        }
        else if (_playTime >= duration)
        {
            _playTime = duration;
            _isPlaying = false;
        }

        var frameFloat = _playTime * frameRate;
        GetInterpolatedFrames(frameFloat, out var frame0, out var frame1, out var t);
        UpdateTransformsInterpolated(frame0, frame1, t);
    }

    private void GetInterpolatedFrames(float virtualFrame, out int frame0, out int frame1, out float t)
    {
        var virtualFrameInt = (int)virtualFrame;
        var virtualFrameFrac = virtualFrame - virtualFrameInt;

        var totalVirtual = GetFrameCountWithHolds();
        if (IsLooping)
            virtualFrameInt %= totalVirtual;
        else
            virtualFrameInt = Math.Min(virtualFrameInt, totalVirtual - 1);

        frame0 = 0;
        t = 0f;
        var accum = 0;
        for (var i = 0; i < FrameCount; i++)
        {
            var frameSpan = 1 + Frames[i].Hold;
            if (accum + frameSpan > virtualFrameInt)
            {
                frame0 = i;
                var posInFrame = virtualFrameInt - accum + virtualFrameFrac;
                t = posInFrame / frameSpan;
                break;
            }
            accum += frameSpan;
        }

        frame1 = frame0 + 1;
        if (IsLooping)
            frame1 %= FrameCount;
        else
            frame1 = Math.Min(frame1, FrameCount - 1);
    }

    public override void Import(string outputPath, PropertySet meta)
    {           
        using var writer = new BinaryWriter(File.Create(outputPath));

        writer.WriteAssetHeader(AssetType.Animation, 1);

        if (Skeleton == null)
        {
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((byte)0);
            writer.Write((byte)12);
            writer.Write((byte)0);
            return;
        }

        var realFrameCount = GetFrameCountWithHolds();
        const int frameRate = 12;


        writer.Write((byte)Skeleton.BoneCount);
        writer.Write((byte)FrameCount);
        writer.Write((byte)realFrameCount);
        writer.Write((byte)frameRate);
        writer.Write((byte)Flags);

        for (var i = 0; i < Skeleton.BoneCount; i++)
            writer.Write((byte)Bones[i].Index);

        for (var frameIndex = 0; frameIndex < FrameCount; frameIndex++)
        {
            var f = Frames[frameIndex];

            // Bone 0 (root) - position is zeroed for root motion handling
            var rootTransform = f.Transforms[0];
            writer.Write(0f); // position x
            writer.Write(0f); // position y
            writer.Write(rootTransform.Rotation + Skeleton.Bones[0].Transform.Rotation);
            writer.Write(rootTransform.Scale.X);
            writer.Write(rootTransform.Scale.Y);

            for (var boneIndex = 1; boneIndex < Skeleton.BoneCount; boneIndex++)
            {
                var bind = Skeleton.Bones[boneIndex].Transform;
                var absTransform = f.Transforms[boneIndex];
                writer.Write(bind.Position.X + absTransform.Position.X);
                writer.Write(bind.Position.Y + absTransform.Position.Y);
                writer.Write(bind.Rotation + absTransform.Rotation);
                writer.Write(absTransform.Scale.X);
                writer.Write(absTransform.Scale.Y);
            }
        }

        var baseRootMotion = Frames[0].Transforms[0].Position.X;

        for (var frameIndex = 0; frameIndex < FrameCount; frameIndex++)
        {
            var fd = Frames[frameIndex];
            byte eventId = 0; // TODO: resolve event document to ID

            var transform0 = (byte)frameIndex;
            var transform1 = IsLooping
                ? (byte)((frameIndex + 1) % FrameCount)
                : (byte)Math.Min(frameIndex + 1, FrameCount - 1);

            var rootMotion0 = Frames[transform0].Transforms[0].Position.X - baseRootMotion;
            var rootMotion1 = Frames[transform1].Transforms[0].Position.X - baseRootMotion;

            if (transform1 < transform0)
                rootMotion1 += rootMotion0 + baseRootMotion;

            if (fd.Hold == 0)
            {
                writer.Write(eventId);
                writer.Write(transform0);
                writer.Write(transform1);
                writer.Write(0f); // fraction0
                writer.Write(1f); // fraction1
                writer.Write(rootMotion0);
                writer.Write(rootMotion1);
                continue;
            }

            var holdCount = fd.Hold + 1;
            var fraction0 = 0f;
            for (var holdIndex = 0; holdIndex < holdCount; holdIndex++)
            {
                var fraction1 = (float)(holdIndex + 1) / holdCount;
                var rm1 = rootMotion0 + (rootMotion1 - rootMotion0) * fraction1;

                writer.Write(holdIndex == 0 ? eventId : (byte)0);
                writer.Write(transform0);
                writer.Write(transform1);
                writer.Write(fraction0);
                writer.Write(fraction1);
                writer.Write(rootMotion0 + (rootMotion1 - rootMotion0) * fraction0);
                writer.Write(rm1);

                fraction0 = fraction1;
            }
        }
    }

    public static AnimationDocument? CreateNew(string path, SkeletonDocument skeleton)
    {
        var fullPath = path;
        if (!fullPath.EndsWith(".anim", StringComparison.OrdinalIgnoreCase))
            fullPath += ".anim";

        var contents = $"s \"{skeleton.Name}\"\n";
        File.WriteAllText(fullPath, contents);

        var doc = DocumentManager.Create(fullPath) as AnimationDocument;
        doc?.Load();
        return doc;
    }

    private static void NewFile(StreamWriter writer)
    {
        writer.WriteLine("s \"\"");
    }

    public void SetSkeleton(SkeletonDocument? skeleton)
    {
        Skeleton = skeleton;
        SkeletonName = skeleton?.Name;
        BoneCount = 0;
        for (var i = 0; i < NoZ.Skeleton.MaxBones; i++)
        {
            Bones[i].Name = "";
            Bones[i].Index = -1;
        }
        if (skeleton != null)
        {
            BoneCount = skeleton.BoneCount;
            for (var i = 0; i < skeleton.BoneCount; i++)
            {
                var bone = skeleton.Bones[i];
                Bones[i].Name = bone.Name;
                Bones[i].Index = i;
            }
        }
        UpdateTransforms();
        UpdateBounds();
        MarkMetaModified();
    }
}
