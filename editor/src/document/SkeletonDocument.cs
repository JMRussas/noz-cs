//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Globalization;
using System.Numerics;

namespace NoZ.Editor;

public class BoneData
{
    public string Name = "";
    public int Index;
    public int ParentIndex = -1;
    public BoneTransform Transform;
    public float Length = 0.25f;
    public Vector2 NamePosition;

    public bool IsHeadSelected;
    public bool IsTailSelected;
    public bool IsConnected;

    public Vector2 HeadWorld;
    public Vector2 TailWorld;

    public bool IsSelected => IsHeadSelected || IsTailSelected;
    public bool IsFullySelected => IsHeadSelected && IsTailSelected;

    public BoneData()
    {
        Transform = BoneTransform.Identity;
    }
}

public enum BoneHitType
{
    None,
    Head,
    Tail,
    Bone
}

public struct BoneHitResult
{
    public int BoneIndex;
    public BoneHitType HitType;
}

public class SkeletonDocument : Document
{
    private const float BoneWidth = 0.15f;
    private const float BoundsPadding = 0.1f;

    public List<SpriteDocument> Sprites = [];

    public readonly BoneData[] Bones = new BoneData[Skeleton.MaxBones];
    public NativeArray<Matrix3x2> LocalToWorld = new(Skeleton.MaxBones);
    public NativeArray<Matrix3x2> WorldToLocal = new(Skeleton.MaxBones);
    public int BoneCount;
    public int SelectedHeadCount;
    public int SelectedTailCount;
    public int SelectedBoneCount => int.Max(SelectedHeadCount, SelectedTailCount);

    public bool CurrentConnected = true;

    public static event Action<SkeletonDocument, int, string, string>? BoneRenamed;
    public static event Action<SkeletonDocument, int, string>? BoneRemoved;
    public static event Action<SkeletonDocument, int>? BoneAdded;
    public static event Action<SkeletonDocument>? TransformsChanged;

    public SkeletonDocument()
    {
        for (var i = 0; i < Skeleton.MaxBones; i++)
            Bones[i] = new BoneData();
    }

    public static void RegisterDef()
    {
        DocumentManager.RegisterDef(new DocumentDef {
            Type = AssetType.Skeleton,
            Extension = ".skel",
            Factory = () => new SkeletonDocument(),
            EditorFactory = doc => new SkeletonEditor((SkeletonDocument)doc),
            NewFile = NewFile,
            Icon = () => EditorAssets.Sprites.IconBone
        });
    }

    public BoneData? GetParent(BoneData bone) =>
        bone.ParentIndex >= 0 ? Bones[bone.ParentIndex] : null;

    public Matrix3x2 GetParentLocalToWorld(BoneData bone, Matrix3x2 defaultTransform) =>
        bone.ParentIndex >= 0 ? LocalToWorld[bone.ParentIndex] : defaultTransform;

    public override void Load()
    {
        var contents = File.ReadAllText(Path);
        var tk = new Tokenizer(contents);

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("b"))
            {
                ParseBone(ref tk);
            }
            else
            {
                break;
            }
        }

        UpdateTransforms();
        Loaded = true;
    }

    private void ParseBone(ref Tokenizer tk)
    {
        if (!tk.ExpectQuotedString(out var boneName))
            throw new Exception("Expected bone name as quoted string");

        if (!tk.ExpectInt(out var parentIndex))
            throw new Exception("Expected parent index");

        var bone = Bones[BoneCount++];
        bone.Name = boneName;
        bone.ParentIndex = parentIndex;
        bone.Index = BoneCount - 1;
        bone.Transform.Scale = Vector2.One;
        bone.Length = 0.25f;

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("p"))
            {
                ParseBonePosition(bone, ref tk);
            }
            else if (tk.ExpectIdentifier("r"))
            {
                ParseBoneRotation(bone, ref tk);
            }
            else if (tk.ExpectIdentifier("l"))
            {
                ParseBoneLength(bone, ref tk);
            }
            else if (tk.ExpectIdentifier("c"))
            {
                bone.IsConnected = true;
            }
            else
            {
                break;
            }
        }
    }

    private static void ParseBonePosition(BoneData bone, ref Tokenizer tk)
    {
        if (!tk.ExpectFloat(out var x))
            throw new Exception("Missing 'x' in bone position");
        if (!tk.ExpectFloat(out var y))
            throw new Exception("Missing 'y' in bone position");

        bone.Transform.Position = new Vector2(x, y);
    }

    private static void ParseBoneRotation(BoneData bone, ref Tokenizer tk)
    {
        if (!tk.ExpectFloat(out var r))
            throw new Exception("Missing bone rotation value");

        bone.Transform.Rotation = r;
    }

    private static void ParseBoneLength(BoneData bone, ref Tokenizer tk)
    {
        bone.Length = float.Max(0.05f, tk.ExpectFloat());
    }

    public override void Save(StreamWriter writer)
    {
        BuildTransformsFromWorldPoints();

        for (var i = 0; i < BoneCount; i++)
        {
            var bone = Bones[i];
            var connected = bone.IsConnected ? " c" : "";
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "b \"{0}\" {1} p {2} {3} r {4} l {5}{6}",
                bone.Name,
                bone.ParentIndex,
                bone.Transform.Position.X,
                bone.Transform.Position.Y,
                bone.Transform.Rotation,
                bone.Length,
                connected));
        }
    }

    public override void PostLoad()
    {
        UpdateSprites();
        UpdateTransforms();
        InitWorldPositions();
    }

    public override void OnUndoRedo()
    {
        BuildTransformsFromWorldPoints();
        NotifyTransformsChanged();
    }

    public override void Clone(Document source)
    {
        var src = (SkeletonDocument)source;
        BoneCount = src.BoneCount;
        SelectedHeadCount = src.SelectedHeadCount;
        SelectedTailCount = src.SelectedTailCount;
        CurrentConnected = src.CurrentConnected;

        LocalToWorld.Dispose();
        WorldToLocal.Dispose();
        LocalToWorld = new NativeArray<Matrix3x2>(src.LocalToWorld);
        WorldToLocal = new NativeArray<Matrix3x2>(src.WorldToLocal);

        for (var i = 0; i < src.BoneCount; i++)
        {
            Bones[i].Name = src.Bones[i].Name;
            Bones[i].NamePosition = src.Bones[i].NamePosition;
            Bones[i].Index = src.Bones[i].Index;
            Bones[i].ParentIndex = src.Bones[i].ParentIndex;
            Bones[i].Transform = src.Bones[i].Transform;
            Bones[i].Length = src.Bones[i].Length;
            Bones[i].IsHeadSelected = src.Bones[i].IsHeadSelected;
            Bones[i].IsTailSelected = src.Bones[i].IsTailSelected;
            Bones[i].IsConnected = src.Bones[i].IsConnected;
            Bones[i].HeadWorld = src.Bones[i].HeadWorld;
            Bones[i].TailWorld = src.Bones[i].TailWorld;
        }

        Sprites = [.. src.Sprites];
    }

    public void UpdateTransforms()
    {
        if (BoneCount <= 0)
            return;

        LocalToWorld.Clear();
        WorldToLocal.Clear();
        LocalToWorld.AddRange(BoneCount);
        WorldToLocal.AddRange(BoneCount);

        var root = Bones[0];
        LocalToWorld[0] = CreateTRS(root.Transform.Position, root.Transform.Rotation, Vector2.One);
        Matrix3x2.Invert(LocalToWorld[0], out WorldToLocal[0]);

        for (var boneIndex = 1; boneIndex < BoneCount; boneIndex++)
        {
            ref var bone = ref Bones[boneIndex];
            var parent = Bones[bone.ParentIndex];
            LocalToWorld[boneIndex] = CreateTRS(
                bone.Transform.Position,
                bone.Transform.Rotation,
                Vector2.One) * LocalToWorld[bone.ParentIndex];
            Matrix3x2.Invert(LocalToWorld[boneIndex], out WorldToLocal[boneIndex]);
        }

        var rootPosition = Vector2.Transform(Vector2.Zero, LocalToWorld[0]);
        var bounds = new Rect(rootPosition.X, rootPosition.Y, 0, 0);

        for (var i = 0; i < BoneCount; i++)
        {
            var b = Bones[i];
            var boneWidth = b.Length * BoneWidth;
            var boneTransform = LocalToWorld[i];

            bounds = ExpandBounds(bounds, Vector2.Transform(Vector2.Zero, boneTransform));
            bounds = ExpandBounds(bounds, Vector2.Transform(new Vector2(b.Length, 0), boneTransform));
            bounds = ExpandBounds(bounds, Vector2.Transform(new Vector2(boneWidth, boneWidth), boneTransform));
            bounds = ExpandBounds(bounds, Vector2.Transform(new Vector2(boneWidth, -boneWidth), boneTransform));
        }

        //for (var i = 0; i < Sprites.Count; i++)
        //    bounds = Rect.Union(bounds, Sprites[i].Bounds);

        Bounds = bounds.Expand(BoundsPadding);
    }

    public void InitWorldPositions()
    {
        for (var i = 0; i < BoneCount; i++)
        {
            var bone = Bones[i];
            ref readonly var m = ref LocalToWorld[i];
            bone.HeadWorld = Vector2.Transform(Vector2.Zero, m);
            bone.TailWorld = Vector2.Transform(new Vector2(bone.Length, 0), m);
        }
    }

    public void BuildTransformsFromWorldPoints()
    {
        for (var i = 0; i < BoneCount; i++)
        {
            var bone = Bones[i];
            var headToTail = bone.TailWorld - bone.HeadWorld;
            var length = headToTail.Length();
            if (length < 0.05f)
                length = 0.05f;
            bone.Length = length;

            var worldRotation = MathF.Atan2(headToTail.Y, headToTail.X) * 180f / MathF.PI;

            if (bone.ParentIndex < 0)
            {
                bone.Transform.Position = bone.HeadWorld;
                bone.Transform.Rotation = worldRotation;
            }
            else
            {
                var parent = Bones[bone.ParentIndex];
                var parentHeadToTail = parent.TailWorld - parent.HeadWorld;
                var parentWorldRotation = MathF.Atan2(parentHeadToTail.Y, parentHeadToTail.X) * 180f / MathF.PI;

                var relativePos = bone.HeadWorld - parent.HeadWorld;
                var radians = -parentWorldRotation * MathF.PI / 180f;
                var cos = MathF.Cos(radians);
                var sin = MathF.Sin(radians);
                bone.Transform.Position = new Vector2(
                    relativePos.X * cos - relativePos.Y * sin,
                    relativePos.X * sin + relativePos.Y * cos
                );
                bone.Transform.Rotation = worldRotation - parentWorldRotation;
            }
        }

        UpdateTransforms();
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

    public int FindBoneIndex(string name)
    {
        for (var i = 0; i < BoneCount; i++)
            if (Bones[i].Name == name)
                return i;
        return -1;
    }

    public int HitTestBones(Matrix3x2 transform, Vector2 position, Span<int> bones)
    {
        var hitCount = 0;
        for (var boneIndex = BoneCount - 1; boneIndex >= 0 && hitCount < bones.Length; boneIndex--)
        {
            var b = Bones[boneIndex];
            var localToWorld = LocalToWorld[boneIndex] * transform;
            var boneStart = Vector2.Transform(Vector2.Zero, localToWorld);
            var boneEnd = Vector2.Transform(new Vector2(b.Length, 0), localToWorld);

            if (!Gizmos.HitTestBone(boneStart, boneEnd, position))
                continue;

            bones[hitCount++] = boneIndex;
        }

        return hitCount;
    }

    public int HitTestBone(Matrix3x2 transform, Vector2 position, bool cycle = false)
    {
        Span<int> bones = stackalloc int[Skeleton.MaxBones];
        var boneCount = HitTestBones(transform, position, bones);
        if (boneCount == 0)
            return -1;

        // Return topmost bone (first in list since we iterate in reverse order)
        if (!cycle)
            return bones[0];

        // Find the first selected bone and cycle to the next one
        for (var i = 0; i < boneCount; i++)
        {
            if (!Bones[bones[i]].IsSelected)
                continue;

            // Return the next bone in the list (wrap around)
            return bones[(i + 1) % boneCount];
        }

        // No selected bone found, return topmost
        return bones[0];
    }

    public int HitTestBone(Vector2 worldPos, bool cycle = false)
    {
        return HitTestBone(Matrix3x2.CreateTranslation(Position), worldPos, cycle);
    }

    public BoneHitResult HitTestJoints(Vector2 worldPos, bool cycle = false)
    {
        Span<BoneHitResult> hits = stackalloc BoneHitResult[Skeleton.MaxBones * 3];
        var hitCount = 0;

        for (var boneIndex = BoneCount - 1; boneIndex >= 0; boneIndex--)
        {
            var bone = Bones[boneIndex];
            var headPos = bone.HeadWorld + Position;
            var tailPos = bone.TailWorld + Position;

            if (Gizmos.HitTestJoint(headPos, worldPos))
                hits[hitCount++] = new BoneHitResult { BoneIndex = boneIndex, HitType = BoneHitType.Head };

            if (Gizmos.HitTestJoint(tailPos, worldPos))
                hits[hitCount++] = new BoneHitResult { BoneIndex = boneIndex, HitType = BoneHitType.Tail };

            if (Gizmos.HitTestBone(headPos, tailPos, worldPos))
                hits[hitCount++] = new BoneHitResult { BoneIndex = boneIndex, HitType = BoneHitType.Bone };
        }

        if (hitCount == 0)
            return new BoneHitResult { BoneIndex = -1, HitType = BoneHitType.None };

        if (!cycle)
            return hits[0];

        for (var i = 0; i < hitCount; i++)
        {
            var hit = hits[i];
            var isSelected = hit.HitType switch
            {
                BoneHitType.Head => Bones[hit.BoneIndex].IsHeadSelected,
                BoneHitType.Tail => Bones[hit.BoneIndex].IsTailSelected,
                BoneHitType.Bone => Bones[hit.BoneIndex].IsFullySelected,
                _ => false
            };

            if (isSelected)
                return hits[(i + 1) % hitCount];
        }

        return hits[0];
    }

    private void ReparentBoneTransform(int boneIndex, int parentIndex)
    {
        var newLocal = LocalToWorld[boneIndex] * WorldToLocal[parentIndex];

        ref var bone = ref Bones[boneIndex];
        bone.Transform.Position.X = newLocal.M31;
        bone.Transform.Position.Y = newLocal.M32;

        var scaleX = MathF.Sqrt(newLocal.M11 * newLocal.M11 + newLocal.M12 * newLocal.M12);
        var scaleY = MathF.Sqrt(newLocal.M21 * newLocal.M21 + newLocal.M22 * newLocal.M22);

        bone.Transform.Scale = new Vector2(scaleX, scaleY);
        bone.Transform.Rotation = MathF.Atan2(newLocal.M12 / scaleX, newLocal.M11 / scaleX) * 180f / MathF.PI;
    }

    public int ReparentBone(int boneIndex, int parentIndex)
    {
        Bones[boneIndex].ParentIndex = parentIndex;

        Array.Sort(Bones, 0, BoneCount, Comparer<BoneData>.Create((a, b) => a.ParentIndex.CompareTo(b.ParentIndex)));

        Span<int> boneMap = stackalloc int[Skeleton.MaxBones];
        for (var i = 0; i < BoneCount; i++)
            boneMap[Bones[i].Index] = i;

        for (var i = 1; i < BoneCount; i++)
        {
            Bones[i].ParentIndex = boneMap[Bones[i].ParentIndex];
            Bones[i].Index = i;
        }

        ReparentBoneTransform(boneMap[boneIndex], boneMap[parentIndex]);
        UpdateTransforms();

        return boneMap[boneIndex];
    }

    public void RemoveBone(int boneIndex)
    {
        if (boneIndex <= 0 || boneIndex >= BoneCount)
            return;

        var parentIndex = Bones[boneIndex].ParentIndex;

        // Reparent children to deleted bone's parent and mark them unconnected
        for (var childIndex = 0; childIndex < BoneCount; childIndex++)
        {
            if (Bones[childIndex].ParentIndex == boneIndex)
            {
                Bones[childIndex].ParentIndex = parentIndex;
                Bones[childIndex].IsConnected = false;
            }
        }

        BoneCount--;

        // Shift bones down to fill the gap
        for (var i = boneIndex; i < BoneCount; i++)
        {
            var nextBone = Bones[i + 1];
            Bones[i].Name = nextBone.Name;
            Bones[i].Index = i;
            Bones[i].Transform = nextBone.Transform;
            Bones[i].Length = nextBone.Length;
            Bones[i].IsHeadSelected = nextBone.IsHeadSelected;
            Bones[i].IsTailSelected = nextBone.IsTailSelected;
            Bones[i].IsConnected = nextBone.IsConnected;
            Bones[i].HeadWorld = nextBone.HeadWorld;
            Bones[i].TailWorld = nextBone.TailWorld;

            if (nextBone.ParentIndex == boneIndex)
                Bones[i].ParentIndex = parentIndex;
            else if (nextBone.ParentIndex > boneIndex)
                Bones[i].ParentIndex = nextBone.ParentIndex - 1;
            else
                Bones[i].ParentIndex = nextBone.ParentIndex;
        }
    }

    public string GetUniqueBoneName()
    {
        var boneName = "Bone";
        var postfix = 2;

        while (FindBoneIndex(boneName) != -1)
        {
            boneName = $"Bone{postfix++}";
        }

        return boneName;
    }

    public int GetBoneSide(int boneIndex)
    {
        var name = Bones[boneIndex].Name;
        GetBoneSideInternal(name, out _, out var side);
        return side;
    }

    private static void GetBoneSideInternal(string name, out bool isPrefix, out int side)
    {
        isPrefix = false;
        side = 0;

        if (name.Length < 2)
            return;

        if (name.StartsWith("l_", StringComparison.OrdinalIgnoreCase))
        {
            isPrefix = true;
            side = -1;
            return;
        }
        if (name.StartsWith("r_", StringComparison.OrdinalIgnoreCase))
        {
            isPrefix = true;
            side = 1;
            return;
        }
        if (name.EndsWith("_l", StringComparison.OrdinalIgnoreCase))
        {
            isPrefix = false;
            side = -1;
            return;
        }
        if (name.EndsWith("_r", StringComparison.OrdinalIgnoreCase))
        {
            isPrefix = false;
            side = 1;
        }
    }

    public int GetMirrorBone(int boneIndex)
    {
        var nameA = Bones[boneIndex].Name;
        GetBoneSideInternal(nameA, out var isPrefixA, out var sideA);

        if (sideA == 0)
            return -1;

        for (var i = 0; i < BoneCount; i++)
        {
            if (i == boneIndex)
                continue;

            var nameB = Bones[i].Name;
            GetBoneSideInternal(nameB, out var isPrefixB, out var sideB);

            if (sideB != -sideA)
                continue;

            if (isPrefixA != isPrefixB)
                continue;

            if (nameA.Length != nameB.Length)
                continue;

            if (isPrefixA)
            {
                if (string.Equals(nameA.Substring(2), nameB.Substring(2), StringComparison.Ordinal))
                    return i;
            }
            else
            {
                if (string.Equals(nameA.Substring(0, nameA.Length - 2), nameB.Substring(0, nameB.Length - 2), StringComparison.Ordinal))
                    return i;
            }
        }

        return -1;
    }

    public override void Draw()
    {
        using (Gizmos.PushState(EditorLayer.Document))
        {
            Graphics.SetTransform(Transform);

            for (var boneIndex = 0; boneIndex < BoneCount; boneIndex++)
            {
                ref readonly var b = ref Bones[boneIndex];
                if (b.ParentIndex >= 0 && !b.IsConnected)
                {
                    ref readonly var p = ref Bones[b.ParentIndex];
                    if (Vector2.DistanceSquared(b.HeadWorld, p.TailWorld) > 0.1f * 0.1f)
                    {
                        Graphics.SetSortGroup(1);
                        Gizmos.SetColor(EditorStyle.Skeleton.ParentLineColor);
                        Gizmos.DrawDashedLine(b.HeadWorld, p.TailWorld, order: 1);
                    }
                }

                Gizmos.DrawBoneAndJoints(this, boneIndex);
            }
        }

        DrawSprites();
    }

    public void DrawSprites()
    {
        using (Graphics.PushState())
        {
            Graphics.SetLayer(EditorLayer.Document);
            Graphics.SetTransform(Transform);
            Graphics.SetSortGroup(0);

            for (var i = 0; i < Sprites.Count; i++)
            {
                Debug.Assert(Sprites[i] != null);
                Debug.Assert(Sprites[i].Binding.IsBoundTo(this));
                Sprites[i].DrawSprite();
            }
        }
    }

    public override void Import(string outputPath, PropertySet meta)
    {
        using var writer = new BinaryWriter(File.Create(outputPath));

        writer.WriteAssetHeader(AssetType.Skeleton, 1, 0);
        writer.Write((byte)BoneCount);

        for (var i = 0; i < BoneCount; i++)
        {
            var bone = Bones[i];
            writer.Write(bone.Name);
            writer.Write((sbyte)bone.ParentIndex);
            writer.Write(bone.Transform.Position.X);
            writer.Write(bone.Transform.Position.Y);
            writer.Write(bone.Transform.Rotation);
            writer.Write(bone.Transform.Scale.X);
            writer.Write(bone.Transform.Scale.Y);

            ref var w = ref WorldToLocal[i];
            writer.Write(w.M11);
            writer.Write(w.M12);
            writer.Write(w.M21);
            writer.Write(w.M22);
            writer.Write(w.M31);
            writer.Write(w.M32);
        }
    }

    public static SkeletonDocument? CreateNew(string path)
    {
        const string defaultSkel = "b \"root\" -1 p 0 0 l 1\n";

        var fullPath = path;
        if (!fullPath.EndsWith(".skel", StringComparison.OrdinalIgnoreCase))
            fullPath += ".skel";

        File.WriteAllText(fullPath, defaultSkel);

        var doc = DocumentManager.Create(fullPath) as SkeletonDocument;
        doc?.Load();
        return doc;
    }

    public void UpdateSprites()
    {
        for (int i=0; i<DocumentManager.Documents.Count; i++)
            (DocumentManager.Documents[i] as SpriteDocument)?.PostLoad();

        Sprites = [.. DocumentManager.Documents
            .OfType<SpriteDocument>()
            .Where(d => d.Binding.IsBoundTo(this) && d.ShowInSkeleton)];
    }

    public void NotifyBoneRenamed(int boneIndex, string oldName, string newName)
    {
        BoneRenamed?.Invoke(this, boneIndex, oldName, newName);
        UpdateSprites();
    }

    public void NotifyBoneRemoved(int removedIndex, string removedName)
    {
        BoneRemoved?.Invoke(this, removedIndex, removedName);
        UpdateSprites();
    }

    public void NotifyBoneAdded(int boneIndex)
    {
        BoneAdded?.Invoke(this, boneIndex);
        UpdateSprites();
    }

    public void NotifyTransformsChanged()
    {
        TransformsChanged?.Invoke(this);
        UpdateSprites();
    }

    public override void Dispose()
    {
        GC.SuppressFinalize(this);
        LocalToWorld.Dispose();
        WorldToLocal.Dispose();
        base.Dispose();
    }

    private static void NewFile(StreamWriter writer)
    {
        writer.WriteLine("b \"root\" -1 p 0 0 r 0 l 0.1");
    }
}
