//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System;
using System.Collections.Generic;
using System.IO;

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
            public char ascii;
            public Point[] points;
            public Contour[] contours;
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

        private Glyph[] _glyphs;
        internal List<Tuple<ushort, float>> _kerning;

        private partial class Reader { };

        public Glyph GetGlyph(char c) => _glyphs[c];

        public static TrueTypeFont Load(string path, int requestedSize, string filter)
        {
            using (var stream = File.OpenRead(path))
            {
                return Load(stream, requestedSize, filter);
            }
        }

        public static TrueTypeFont Load(Stream stream, int requestedSize, string filter)
        {
            using (var reader = new Reader(stream, requestedSize, filter))
            {
                return reader.Read();
            }
        }
    }
}
