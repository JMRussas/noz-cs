//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor
{
    partial class TrueTypeFont
    {
        public enum CurveType : byte
        {
            None,
            Cubic,
            Conic
        }

        public struct Point
        {
            public Vector2Double xy;
            public CurveType curve;
        }

        public struct Contour
        {
            public int start;
            public int length;
        }

        public class Glyph
        {
            public ushort id;
            public char codepoint;
            public Point[] points = null!;
            public Contour[] contours = null!;
            public double advance;
            public Vector2Double size;
            public Vector2Double bearing;
        }

        public double Ascent { get; private set; }
        public double Descent { get; private set; }
        public double LineGap { get; private set; }
        public double Height { get; private set; }
        public double InternalLeading { get; internal set; }
        public string FamilyName { get; internal set; } = "";

        private Dictionary<char, Glyph> _glyphs = null!;
        internal Dictionary<uint, float> _kerning = null!;

        private partial class Reader { };

        public Glyph? GetGlyph(char c) => _glyphs.GetValueOrDefault(c);
        public IEnumerable<Glyph> Glyphs => _glyphs.Values;

        public static TrueTypeFont Load(string path, int requestedSize, string? filter)
        {
            using var stream = File.OpenRead(path);
            return Load(stream, requestedSize, filter);
        }

        public static TrueTypeFont Load(Stream stream, int requestedSize, string? filter)
        {
            using var reader = new Reader(stream, requestedSize, filter);
            return reader.Read();
        }
    }
}
