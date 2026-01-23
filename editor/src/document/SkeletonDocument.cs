//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Globalization;
using System.Numerics;

namespace NoZ.Editor;

public struct BoneTransform
{
    public Vector2 Position;
    public float Rotation;
    public Vector2 Scale;

    public static readonly BoneTransform Identity = new()
    {
        Position = Vector2.Zero,
        Rotation = 0f,
        Scale = Vector2.One
    };
}

public class BoneData
{
    public string Name = "";
    public int Index;
    public int ParentIndex = -1;
    public BoneTransform Transform;
    public Matrix3x2 LocalToWorld;
    public Matrix3x2 WorldToLocal;
    public float Length = 0.25f;
    public bool Selected;

    public BoneData()
    {
        Transform = BoneTransform.Identity;
        LocalToWorld = Matrix3x2.Identity;
        WorldToLocal = Matrix3x2.Identity;
    }
}

public class Skin
{
    public string AssetName = "";
    public Document? Mesh;
}

public class SkeletonDocument : Document
{
    public const int MaxBones = 64;
    public const int MaxSkins = 16;
    private const float BoneWidth = 0.15f;
    private const float BoundsPadding = 0.1f;

    public readonly BoneData[] Bones = new BoneData[MaxBones];
    public readonly Skin[] Skins = new Skin[MaxSkins];
    public int BoneCount;
    public int SkinCount;
    public int SelectedBoneCount;
    public float Opacity = 1f;

    public SkeletonDocument()
    {
        for (var i = 0; i < MaxBones; i++)
            Bones[i] = new BoneData();
        for (var i = 0; i < MaxSkins; i++)
            Skins[i] = new Skin();
    }

    public static void RegisterDef()
    {
        DocumentManager.RegisterDef(new DocumentDef(
            AssetType.Skeleton,
            ".skel",
            () => new SkeletonDocument(),
            doc => new SkeletonEditor((SkeletonDocument)doc)
        ));
    }

    public BoneData? GetParent(BoneData bone)
    {
        return bone.ParentIndex >= 0 ? Bones[bone.ParentIndex] : null;
    }

    public Matrix3x2 GetParentLocalToWorld(BoneData bone, Matrix3x2 defaultTransform)
    {
        return bone.ParentIndex >= 0 ? Bones[bone.ParentIndex].LocalToWorld : defaultTransform;
    }

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
        if (!tk.ExpectFloat(out var l))
            throw new Exception("Missing bone length value");

        bone.Length = l;
    }

    public override void Save(StreamWriter writer)
    {
        for (var i = 0; i < BoneCount; i++)
        {
            var bone = Bones[i];
            writer.WriteLine(string.Format(CultureInfo.InvariantCulture,
                "b \"{0}\" {1} p {2} {3} r {4} l {5}",
                bone.Name,
                bone.ParentIndex,
                bone.Transform.Position.X,
                bone.Transform.Position.Y,
                bone.Transform.Rotation,
                bone.Length));
        }
    }

    public override void LoadMetadata(PropertySet meta)
    {
        SkinCount = 0;
        foreach (var key in meta.GetKeys("skin"))
        {
            Skins[SkinCount++].AssetName = key;
        }
    }

    public override void SaveMetadata(PropertySet meta)
    {
        meta.ClearGroup("skin");
        for (var i = 0; i < SkinCount; i++)
        {
            if (Skins[i].Mesh == null)
                continue;
            meta.AddKey("skin", Skins[i].AssetName);
        }
    }

    public override void PostLoad()
    {
        UpdateTransforms();
    }

    public override void OnUndoRedo()
    {
        UpdateTransforms();
    }

    public override void Clone(Document source)
    {
        var src = (SkeletonDocument)source;
        BoneCount = src.BoneCount;
        SkinCount = src.SkinCount;
        Opacity = src.Opacity;

        for (var i = 0; i < src.BoneCount; i++)
        {
            Bones[i].Name = src.Bones[i].Name;
            Bones[i].Index = src.Bones[i].Index;
            Bones[i].ParentIndex = src.Bones[i].ParentIndex;
            Bones[i].Transform = src.Bones[i].Transform;
            Bones[i].LocalToWorld = src.Bones[i].LocalToWorld;
            Bones[i].WorldToLocal = src.Bones[i].WorldToLocal;
            Bones[i].Length = src.Bones[i].Length;
            Bones[i].Selected = src.Bones[i].Selected;
        }

        for (var i = 0; i < src.SkinCount; i++)
        {
            Skins[i].AssetName = src.Skins[i].AssetName;
            Skins[i].Mesh = src.Skins[i].Mesh;
        }
    }

    public void UpdateTransforms()
    {
        if (BoneCount <= 0)
            return;

        var root = Bones[0];
        root.LocalToWorld = CreateTRS(root.Transform.Position, root.Transform.Rotation, Vector2.One);
        Matrix3x2.Invert(root.LocalToWorld, out root.WorldToLocal);

        for (var boneIndex = 1; boneIndex < BoneCount; boneIndex++)
        {
            var bone = Bones[boneIndex];
            var parent = Bones[bone.ParentIndex];
            bone.LocalToWorld = CreateTRS(bone.Transform.Position, bone.Transform.Rotation, Vector2.One) * parent.LocalToWorld;
            Matrix3x2.Invert(bone.LocalToWorld, out bone.WorldToLocal);
        }

        var rootPosition = Vector2.Transform(Vector2.Zero, Bones[0].LocalToWorld);
        var bounds = new Rect(rootPosition.X, rootPosition.Y, 0, 0);

        for (var i = 0; i < BoneCount; i++)
        {
            var b = Bones[i];
            var boneWidth = b.Length * BoneWidth;
            var boneTransform = b.LocalToWorld;

            bounds = ExpandBounds(bounds, Vector2.Transform(Vector2.Zero, boneTransform));
            bounds = ExpandBounds(bounds, Vector2.Transform(new Vector2(b.Length, 0), boneTransform));
            bounds = ExpandBounds(bounds, Vector2.Transform(new Vector2(boneWidth, boneWidth), boneTransform));
            bounds = ExpandBounds(bounds, Vector2.Transform(new Vector2(boneWidth, -boneWidth), boneTransform));
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

    public int FindBoneIndex(string name)
    {
        for (var i = 0; i < BoneCount; i++)
            if (Bones[i].Name == name)
                return i;
        return -1;
    }

    public int HitTestBones(Matrix3x2 transform, Vector2 position, int[] bones, int maxBones = MaxBones)
    {
        var hitCount = 0;
        for (var boneIndex = BoneCount - 1; boneIndex >= 0 && hitCount < maxBones; boneIndex--)
        {
            var b = Bones[boneIndex];
            var localToWorld = b.LocalToWorld * transform;
            var boneStart = Vector2.Transform(Vector2.Zero, localToWorld);
            var boneEnd = Vector2.Transform(new Vector2(b.Length, 0), localToWorld);

            if (!PointNearLine(position, boneStart, boneEnd, EditorStyle.SkeletonBoneRadius * Gizmos.ZoomRefScale * 2f))
                continue;

            bones[hitCount++] = boneIndex;
        }

        return hitCount;
    }

    public int HitTestBone(Matrix3x2 transform, Vector2 position)
    {
        var bones = new int[MaxBones];
        var boneCount = HitTestBones(transform, position, bones);
        if (boneCount == 0)
            return -1;

        var bestDist = float.MaxValue;
        var bestBoneIndex = -1;

        for (var i = 0; i < boneCount; i++)
        {
            var b = Bones[bones[i]];
            var localToWorld = b.LocalToWorld * transform;
            var b0 = Vector2.Transform(Vector2.Zero, localToWorld);
            var b1 = Vector2.Transform(new Vector2(b.Length, 0), localToWorld);
            var dist = DistanceFromLine(b0, b1, position);

            if (dist < bestDist)
            {
                bestDist = dist;
                bestBoneIndex = bones[i];
            }
        }

        return bestBoneIndex;
    }

    public int HitTestBone(Vector2 worldPos)
    {
        return HitTestBone(Matrix3x2.CreateTranslation(Position), worldPos);
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

    private static void ReparentBoneTransform(BoneData bone, BoneData parent)
    {
        var newLocal = bone.LocalToWorld * parent.WorldToLocal;

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

        var boneMap = new int[MaxBones];
        for (var i = 0; i < BoneCount; i++)
            boneMap[Bones[i].Index] = i;

        for (var i = 1; i < BoneCount; i++)
        {
            Bones[i].ParentIndex = boneMap[Bones[i].ParentIndex];
            Bones[i].Index = i;
        }

        ReparentBoneTransform(Bones[boneMap[boneIndex]], Bones[boneMap[parentIndex]]);
        UpdateTransforms();

        return boneMap[boneIndex];
    }

    public void RemoveBone(int boneIndex)
    {
        if (boneIndex <= 0 || boneIndex >= BoneCount)
            return;

        var parentIndex = Bones[boneIndex].ParentIndex;

        for (var childIndex = 0; childIndex < BoneCount; childIndex++)
        {
            if (Bones[childIndex].ParentIndex == boneIndex)
            {
                Bones[childIndex].ParentIndex = parentIndex;
                ReparentBoneTransform(Bones[childIndex], Bones[parentIndex]);
            }
        }

        BoneCount--;

        for (var i = boneIndex; i < BoneCount; i++)
        {
            var nextBone = Bones[i + 1];
            Bones[i].Name = nextBone.Name;
            Bones[i].Index = i;
            Bones[i].Transform = nextBone.Transform;
            Bones[i].LocalToWorld = nextBone.LocalToWorld;
            Bones[i].WorldToLocal = nextBone.WorldToLocal;
            Bones[i].Length = nextBone.Length;
            Bones[i].Selected = nextBone.Selected;

            if (nextBone.ParentIndex == boneIndex)
                Bones[i].ParentIndex = parentIndex;
            else if (nextBone.ParentIndex > boneIndex)
                Bones[i].ParentIndex = nextBone.ParentIndex - 1;
            else
                Bones[i].ParentIndex = nextBone.ParentIndex;
        }

        UpdateTransforms();
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

            var lineWidth = EditorStyle.SkeletonBoneWidth * Gizmos.ZoomRefScale;
            var boneRadius = EditorStyle.SkeletonBoneRadius * Gizmos.ZoomRefScale;
            var dashLength = EditorStyle.SkeletonParentDash * Gizmos.ZoomRefScale;

            for (var boneIndex = 0; boneIndex < BoneCount; boneIndex++)
            {
                var b = Bones[boneIndex];
                var p0 = Vector2.Transform(Vector2.Zero, b.LocalToWorld);
                var p1 = Vector2.Transform(new Vector2(b.Length, 0), b.LocalToWorld);

                if (b.ParentIndex >= 0)
                {
                    var parentTransform = GetParentLocalToWorld(b, b.LocalToWorld);
                    var pp = Vector2.Transform(Vector2.Zero, parentTransform);
                    Gizmos.DrawDashedLine(pp, p0, lineWidth, dashLength, EditorStyle.SkeletonBoneColor);
                }

                Gizmos.DrawBone(p0, p1, lineWidth, EditorStyle.SkeletonBoneColor);
                Gizmos.SetColor(EditorStyle.SkeletonBoneColor);
                Gizmos.DrawCircle(p0, boneRadius);
            }
        }
    }



    public override void Import(string outputPath, PropertySet config, PropertySet meta)
    {
        using var writer = new BinaryWriter(File.Create(outputPath));

        writer.WriteAssetHeader(AssetType.Skeleton, 1, 0);
        writer.Write((byte)BoneCount);

        for (var i = 0; i < BoneCount; i++)
        {
            var bone = Bones[i];
            writer.Write((sbyte)bone.ParentIndex);
            writer.Write(bone.Transform.Position.X);
            writer.Write(bone.Transform.Position.Y);
            writer.Write(bone.Transform.Rotation);
            writer.Write(bone.Transform.Scale.X);
            writer.Write(bone.Transform.Scale.Y);

            writer.Write(bone.WorldToLocal.M11);
            writer.Write(bone.WorldToLocal.M12);
            writer.Write(bone.WorldToLocal.M21);
            writer.Write(bone.WorldToLocal.M22);
            writer.Write(bone.WorldToLocal.M31);
            writer.Write(bone.WorldToLocal.M32);
        }
    }

    public static SkeletonDocument? CreateNew(string path)
    {
        const string defaultSkel = "b \"root\" -1 p 0 0 l 1\n";

        var fullPath = path;
        if (!fullPath.EndsWith(".skel", StringComparison.OrdinalIgnoreCase))
            fullPath += ".skel";

        File.WriteAllText(fullPath, defaultSkel);

        var doc = DocumentManager.Load(fullPath) as SkeletonDocument;
        doc?.Load();
        return doc;
    }
}
