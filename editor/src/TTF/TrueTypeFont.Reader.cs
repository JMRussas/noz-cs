//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System;
using System.IO;
using System.Collections.Generic;

namespace NoZ.Editor
{
    partial class TrueTypeFont
    {
        partial class Reader : IDisposable
        {
            private enum TableName
            {
                None,
                HEAD,
                LOCA,
                GLYF,
                HMTX,
                HHEA,
                CMAP,
                MAXP,
                KERN,
                NAME,
                OS2
            }

            private BinaryReader _reader;
            private TrueTypeFont _ttf;
            private short _indexToLocFormat;
            private long[] _tableOffsets;
            private Vector2Double _scale;
            private string _filter;
            private double _unitsPerEm;
            private int _requestedSize;
            private Glyph[] _glyphsById;

            public Reader(Stream stream, int requestedSize, string filter)
            {
                _requestedSize = requestedSize;
                _filter = filter;
                _reader = new BinaryReader(stream);
                _tableOffsets = new long[Enum.GetValues(typeof(TableName)).Length];
            }

            private const double Fixed = 1.0 / (1 << 16);

            private bool IsInFilter(char c) => _filter == null || _filter.IndexOf(c) != -1;


            public float ReadFixed()
            {
                return (float)(ReadInt32() * Fixed);
            }

            public double ReadFUnit()
            {
                return ReadInt16() * _scale.x;
            }

            public double ReadUFUnit()
            {
                return ReadUInt16() * _scale.x;
            }

            public string ReadString(int length)
            {
                return new string(_reader.ReadChars(length));
            }

            public void ReadDate()
            {
                ReadUInt32();
                ReadUInt32();
            }

            public ushort ReadUInt16()
            {
                return (ushort)((_reader.ReadByte() << 8) | _reader.ReadByte());
            }

            public short ReadInt16()
            {
                return (short)((_reader.ReadByte() << 8) | _reader.ReadByte());
            }

            public uint ReadUInt32()
            {
                return
                    (((uint)_reader.ReadByte()) << 24) |
                    (((uint)_reader.ReadByte()) << 16) |
                    (((uint)_reader.ReadByte()) << 8) |
                    _reader.ReadByte()
                ;
            }

            public int ReadInt32()
            {
                return
                    ((_reader.ReadByte()) << 24) |
                    ((_reader.ReadByte()) << 16) |
                    ((_reader.ReadByte()) << 8) |
                    _reader.ReadByte()
                ;
            }

            public ushort[] ReadUInt16Array(int length)
            {
                var result = new ushort[length];
                for (int i = 0; i < length; i++)
                {
                    result[i] = ReadUInt16();
                }
                return result;
            }

            public void Dispose()
            {
                _reader?.Dispose();
            }

            public long Seek(long offset)
            {
                long old = _reader.BaseStream.Position;
                _reader.BaseStream.Seek(offset, SeekOrigin.Begin);
                return old;
            }

            private long Seek(TableName table)
            {
                return Seek(table, 0);
            }

            private long Seek(TableName table, long offset)
            {
                return Seek(_tableOffsets[(int)table] + offset);
            }

            public long Position => _reader.BaseStream.Position;


            /// <summary>
            /// Read the CMAP table within the True Type Font.  This will build the 
            /// </summary>
            private void ReadCMAP()
            {
                Seek(TableName.CMAP, 0);

                /*var version = */
                ReadUInt16();
                var tableCount = ReadUInt16();

                uint offset = 0;
                for (int i = 0; i < tableCount && offset == 0; i++)
                {
                    var platformId = ReadUInt16();
                    var platformSpecificId = ReadUInt16();
                    var platformOffset = ReadUInt32();    // Offset

                    if (platformId == 0 || (platformId == 3 && platformSpecificId == 1))
                        offset = platformOffset;
                }

                if (offset == 0)
                    throw new InvalidDataException("TTF file has no unicode character map.");

                // Seek to the character map 
                Seek(TableName.CMAP, offset);

                var format = ReadUInt16();
                var length = ReadUInt16();
                var language = ReadUInt16();

                switch (format)
                {
                    case 4:
                    {
                        var segcount = ReadUInt16() / 2;
                        var searchRange = ReadUInt16();
                        var entitySelector = ReadUInt16();
                        var rangeShift = ReadUInt16();
                        var endCode = ReadUInt16Array(segcount);
                        ReadUInt16();
                        var startCode = ReadUInt16Array(segcount);
                        var idDelta = ReadUInt16Array(segcount);
                        var glyphIdArray = Position;
                        var idRangeOffset = ReadUInt16Array(segcount);

                        for (int i = 0; endCode[i] != 0xFFFF; i++)
                        {
                            var end = endCode[i];
                            var start = startCode[i];
                            var delta = (short)idDelta[i];
                            var rangeOffset = idRangeOffset[i];

                            if (start > 254)
                                continue;
                            if (end > 254)
                                end = 254;

                            if (rangeOffset == 0)
                            {
                                for (int c = start; c <= end; c++)
                                {
                                    if (!IsInFilter((char)c))
                                        continue;

                                    var glyphId = (ushort)(c + delta);
                                    if (_ttf._glyphs[c] != null)
                                        throw new InvalidDataException($"Multiple definitions for glyph {c:2x}");
                                    _ttf._glyphs[c] = new Glyph { id = glyphId, ascii = (char)c };
                                    _glyphsById[glyphId] = _ttf._glyphs[c];
                                }
                            }
                            else
                            {
                                for (int c = start; c <= end; c++)
                                {
                                    if (!IsInFilter((char)c))
                                        continue;

                                    Seek(glyphIdArray + i * 2 + rangeOffset + 2 * (c - start));
                                    ushort glyphId = ReadUInt16();
                                    if (_ttf._glyphs[c] != null)
                                        throw new InvalidDataException($"Multiple definitions for glyph {c:2x}");
                                    _ttf._glyphs[c] = new Glyph { id = glyphId, ascii = (char)c };
                                    _glyphsById[glyphId] = _ttf._glyphs[c];
                                }
                            }
                        }
                        break;
                    }

                    default:
                        throw new NotImplementedException();

                }
            }

            private void ReadHEAD()
            {
                Seek(TableName.HEAD, 0);

                /* var version = */
                ReadFixed();
                /* var fontRevision = */
                ReadFixed();
                /* var checksumAdjustment = */
                ReadUInt32();
                /* var magicNumber = */
                ReadUInt32();
                /* var flags = */
                ReadUInt16();
                _unitsPerEm = ReadUInt16();
                ReadDate();
                ReadDate();
                /* var xmin = */
                ReadInt16();
                /* var ymin = */
                ReadInt16();
                /* var xmax = */
                ReadInt16();
                /* var ymax = */
                ReadInt16();
                ReadUInt16();
                ReadUInt16();
                ReadInt16();

                _indexToLocFormat = ReadInt16();

                _scale = Vector2Double.Zero;
                _scale.x = _requestedSize / _unitsPerEm;
                _scale.y = _requestedSize / _unitsPerEm;
            }

            private void ReadGlyphs()
            {
                for (int i = 0; i < _ttf._glyphs.Length; i++)
                {
                    var glyph = _ttf._glyphs[i];
                    if (glyph == null)
                        continue;

                    // Seek to the glyph in the GLYF table
                    if (_indexToLocFormat == 1)
                    {
                        Seek(TableName.LOCA, glyph.id * 4);
                        var offset = ReadUInt32();
                        var length = ReadUInt32() - offset;

                        // Empty glyph
                        if (length == 0)
                            continue;

                        Seek(TableName.GLYF, offset);
                    }
                    else
                    {
                        Seek(TableName.LOCA, glyph.id * 2);

                        var offset = ReadUInt16() * 2;
                        var length = (ReadUInt16() * 2) - offset;

                        if (length == 0)
                            continue;

                        Seek(TableName.GLYF, offset);
                    }

                    // Read the glyph
                    ReadGlyph(glyph);
                }
            }

            [Flags]
            private enum PointFlags : byte
            {
                OnCurve = 1,
                XShortVector = 2,
                YShortVector = 4,
                Repeat = 8,
                XIsSame = 16,
                YIsSame = 32
            }

            private void ReadPoints(Glyph glyph, PointFlags[] flags, bool isX)
            {
                PointFlags byteFlag = isX ? PointFlags.XShortVector : PointFlags.YShortVector;
                PointFlags deltaFlag = isX ? PointFlags.XIsSame : PointFlags.YIsSame;

                double value = 0;
                for (int i = 0; i < glyph.points.Length; i++)
                {
                    ref var point = ref glyph.points[i];
                    var pointFlags = flags[i];

                    if ((pointFlags & byteFlag) == byteFlag)
                    {
                        if ((pointFlags & deltaFlag) == deltaFlag)
                        {
                            value += _reader.ReadByte();
                        }
                        else
                        {
                            value -= _reader.ReadByte();
                        }
                    }
                    else if ((pointFlags & deltaFlag) != deltaFlag)
                    {
                        value += ReadInt16();
                    }

                    if (isX)
                        point.xy.x = value * _scale.x;
                    else
                        point.xy.y = value * _scale.y;
                }
            }

            private void ReadGlyph(Glyph glyph)
            {
                short numberOfContours = ReadInt16();

                // Simple ?
                if (numberOfContours < 0)
                    throw new NotImplementedException("Compound glyphs not supported");

                double minx = ReadFUnit();
                double miny = ReadFUnit();
                double maxx = ReadFUnit();
                double maxy = ReadFUnit();

                var endPoints = ReadUInt16Array(numberOfContours);
                var instructionLength = ReadUInt16();
                var instructions = _reader.ReadBytes(instructionLength);
                var numPoints = endPoints[endPoints.Length - 1] + 1;

                glyph.contours = new Contour[numberOfContours];
                for (int i = 0, start = 0; i < numberOfContours; i++)
                {
                    glyph.contours[i].start = start;
                    glyph.contours[i].length = endPoints[i] - start + 1;
                    start = endPoints[i] + 1;
                }

                // Read the flags.
                var flags = new PointFlags[numPoints];
                for (int i = 0; i < numPoints;)
                {
                    var readFlags = (PointFlags)_reader.ReadByte();
                    flags[i++] = readFlags;

                    if (readFlags.HasFlag(PointFlags.Repeat))
                    {
                        var repeat = _reader.ReadByte();
                        for (int r = 0; r < repeat; r++)
                            flags[i++] = readFlags;
                    }
                }

                glyph.points = new Point[numPoints];
                glyph.size = new Vector2Double(maxx - minx, maxy - miny);
                glyph.bearing = new Vector2Double(minx, maxy);

                for (int i = 0; i < numPoints; i++)
                {
                    glyph.points[i].curve = flags[i].HasFlag(PointFlags.OnCurve) ? CurveType.None : CurveType.Conic;
                    glyph.points[i].xy = Vector2Double.Zero;
                }

                ReadPoints(glyph, flags, true);
                ReadPoints(glyph, flags, false);
            }

            private void ReadHHEA()
            {
                Seek(TableName.HHEA);

                /* float verison = */
                ReadFixed();
                _ttf.Ascent = ReadFUnit();
                _ttf.Descent = ReadFUnit();
                _ttf.LineGap = ReadFUnit();
                _ttf.Height = _ttf.Ascent - _ttf.Descent;

                // Skip
                Seek(TableName.HHEA, 34);

                var metricCount = ReadUInt16();

                for (int i = 0; i < _ttf._glyphs.Length; i++)
                {
                    if (_ttf._glyphs[i] == null)
                        continue;

                    // If the glyph is past the end of the total number of metrics
                    // then it is contained in the end run..
                    if (_ttf._glyphs[i].id >= metricCount)
                        // TODO: implement end run..
                        throw new NotImplementedException();

                    Seek(TableName.HMTX, _ttf._glyphs[i].id * 4);
                    _ttf._glyphs[i].advance = ReadUFUnit();
                    double leftBearing = ReadFUnit();
                }
            }

            private void ReadOS2()
            {
                if (_tableOffsets[(int)TableName.OS2] == 0)
                {
                    Log.Debug("OS/2 table not found");
                    return;
                }

                Seek(TableName.OS2);

                var version = ReadUInt16();
                ReadInt16(); // xAvgCharWidth
                ReadUInt16(); // usWeightClass
                ReadUInt16(); // usWidthClass
                ReadUInt16(); // fsType
                ReadInt16(); // ySubscriptXSize
                ReadInt16(); // ySubscriptYSize
                ReadInt16(); // ySubscriptXOffset
                ReadInt16(); // ySubscriptYOffset
                ReadInt16(); // ySuperscriptXSize
                ReadInt16(); // ySuperscriptYSize
                ReadInt16(); // ySuperscriptXOffset
                ReadInt16(); // ySuperscriptYOffset
                ReadInt16(); // yStrikeoutSize
                ReadInt16(); // yStrikeoutPosition
                ReadInt16(); // sFamilyClass
                _reader.ReadBytes(10); // panose
                _reader.ReadBytes(16); // ulUnicodeRange1-4
                _reader.ReadBytes(4); // achVendID
                ReadUInt16(); // fsSelection
                ReadUInt16(); // usFirstCharIndex
                ReadUInt16(); // usLastCharIndex
                var sTypoAscender = ReadFUnit();
                ReadFUnit(); // sTypoDescender
                var sTypoLineGap = ReadFUnit();
                var usWinAscent = ReadUFUnit();
                var usWinDescent = ReadUFUnit();

                _ttf.Ascent = usWinAscent;
                _ttf.Descent = -usWinDescent;
                _ttf.Height = usWinAscent + usWinDescent;
                _ttf.LineGap = sTypoLineGap;
                _ttf.InternalLeading = usWinAscent - sTypoAscender;
            }

            private void ReadMAXP()
            {
                Seek(TableName.MAXP, 0);
                var version = ReadFixed();
                _glyphsById = new Glyph[ReadUInt16()];
            }

            private void ReadNAME()
            {
                if (_tableOffsets[(int)TableName.NAME] == 0)
                    return;

                Seek(TableName.NAME, 0);

                var format = ReadUInt16();
                var count = ReadUInt16();
                var stringOffset = ReadUInt16();

                // Look for the font family name (nameID = 1)
                // Prefer Windows platform (3) with Unicode encoding (1)
                for (int i = 0; i < count; i++)
                {
                    var platformId = ReadUInt16();
                    var encodingId = ReadUInt16();
                    var languageId = ReadUInt16();
                    var nameId = ReadUInt16();
                    var length = ReadUInt16();
                    var offset = ReadUInt16();

                    // nameID 1 = Font Family name
                    if (nameId == 1)
                    {
                        var pos = Position;
                        Seek(TableName.NAME, stringOffset + offset);

                        // Windows platform uses UTF-16 BE
                        if (platformId == 3 && encodingId == 1)
                        {
                            var chars = new char[length / 2];
                            for (int j = 0; j < chars.Length; j++)
                                chars[j] = (char)ReadUInt16();
                            _ttf.FamilyName = new string(chars);
                            return;
                        }
                        // Mac or Unicode platform - ASCII/UTF-8
                        else if (platformId == 0 || platformId == 1)
                        {
                            _ttf.FamilyName = ReadString(length);
                            // Keep looking for Windows platform which is preferred
                        }

                        Seek(pos);
                    }
                }
            }

            private void ReadKERN()
            {
                Seek(TableName.KERN, 2);
                int numTables = ReadInt16();
                for (int i = 0; i < numTables; i++)
                {
                    long tableStart = Position;
                    /*var version = */
                    ReadUInt16();
                    int length = ReadUInt16();
                    int coverage = ReadUInt16();
                    int format = coverage & 0xFF00;

                    switch (format)
                    {
                        case 0:
                        {
                            int pairCount = ReadUInt16();
                            int searchRange = ReadUInt16();
                            int entrySelector = ReadUInt16();
                            int rangeShift = ReadUInt16();

                            for (int pair = 0; pair < pairCount; ++pair)
                            {
                                var leftId = ReadUInt16();
                                var rightId = ReadUInt16();
                                var left = _glyphsById[leftId];
                                var right = _glyphsById[rightId];
                                double kern = ReadFUnit();

                                if (left == null || right == null)
                                    continue;

                                if (null == _ttf._kerning)
                                    _ttf._kerning = new List<Tuple<ushort, float>>();

                                _ttf._kerning.Add(new Tuple<ushort, float>(
                                    (ushort)((left.ascii << 8) + right.ascii),
                                    (float)kern
                                    ));
                            }
                            break;
                        }

                        default:
                            throw new NotImplementedException();
                    }

                    Seek(tableStart + length);
                }
            }


            public TrueTypeFont Read()
            {
                _ttf = new TrueTypeFont();

                ReadUInt32(); // Scalar type
                ushort numTables = ReadUInt16();
                ReadUInt16(); // Search range
                ReadUInt16(); // Entry Selector
                ReadUInt16(); // Range Shift

                // Right now we only support ASCII table.
                _ttf._glyphs = new Glyph[255];

                // Read all of the relevant table offsets and validate their checksums
                for (int i = 0; i < numTables; i++)
                {
                    var tag = ReadString(4);
                    var checksum = ReadUInt32();
                    var offset = ReadUInt32();
                    var length = ReadUInt32();

                    TableName name = TableName.None;
                    // OS/2 table has a slash which doesn't parse as enum
                    if (tag == "OS/2")
                        name = TableName.OS2;
                    else if (!Enum.TryParse(tag.ToUpper(), out name))
                        continue;

                    _tableOffsets[(int)name] = offset;

                    uint CalculateCheckum()
                    {
                        var old = Seek(offset);
                        uint sum = 0;
                        uint count = (length + 3) / 4;
                        for (uint j = 0; j < count; j++)
                            sum = (sum + ReadUInt32() & 0xffffffff);

                        Seek(old);
                        return sum;
                    };

                    if (tag != "head" && CalculateCheckum() != checksum)
                        throw new InvalidDataException($"Checksum mismatch on '{tag}' block");
                }

                ReadHEAD();

                ReadMAXP();

                ReadCMAP();

                ReadHHEA();

                ReadOS2();

                ReadGlyphs();

                ReadKERN();

                ReadNAME();

                return _ttf;
            }
        }
    }
}
