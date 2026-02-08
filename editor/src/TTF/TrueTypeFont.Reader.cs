//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor
{
    partial class TrueTypeFont
    {
        partial class Reader(Stream stream, int requestedSize, string? filter) : IDisposable
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

            private BinaryReader _reader = new(stream);
            private TrueTypeFont _ttf = null!;
            private short _indexToLocFormat;
            private readonly long[] _tableOffsets = new long[Enum.GetValues<TableName>().Length];
            private Vector2Double _scale;
            private readonly string _filter = filter;
            private double _unitsPerEm;
            private int _requestedSize = requestedSize;
            private Glyph[] _glyphsById = null!;
            private const double Fixed = 1.0 / (1 << 16);

            private bool IsInFilter(char c) => _filter == null || _filter.IndexOf(c) != -1;

            public float ReadFixed() => (float)(ReadInt32() * Fixed);
            public double ReadFUnit() => ReadInt16() * _scale.x;
            public double ReadUFUnit() => ReadUInt16() * _scale.x;
            public string ReadString(int length) => new(_reader.ReadChars(length));

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

                    if (platformId == 0 || (platformId == 3 && (platformSpecificId == 1 || platformSpecificId == 10)))
                        offset = platformOffset;
                }

                if (offset == 0)
                    throw new InvalidDataException("TTF file has no unicode character map.");

                // Seek to the character map
                Seek(TableName.CMAP, offset);

                var format = ReadUInt16();

                switch (format)
                {
                    case 4:
                    {
                        var length = ReadUInt16();
                        var language = ReadUInt16();
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

                            if (rangeOffset == 0)
                            {
                                for (int c = start; c <= end; c++)
                                {
                                    if (!IsInFilter((char)c))
                                        continue;

                                    var glyphId = (ushort)(c + delta);
                                    if (_ttf._glyphs.ContainsKey((char)c))
                                        throw new InvalidDataException($"Multiple definitions for glyph {c:2x}");
                                    var glyph = new Glyph { id = glyphId, codepoint = (char)c };
                                    _ttf._glyphs[(char)c] = glyph;
                                    _glyphsById[glyphId] = glyph;
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
                                    if (_ttf._glyphs.ContainsKey((char)c))
                                        throw new InvalidDataException($"Multiple definitions for glyph {c:2x}");
                                    var glyph = new Glyph { id = glyphId, codepoint = (char)c };
                                    _ttf._glyphs[(char)c] = glyph;
                                    _glyphsById[glyphId] = glyph;
                                }
                            }
                        }
                        break;
                    }

                    case 12:
                    {
                        // Format 12: format(u16) already read, next is reserved(u16), length(u32), language(u32), numGroups(u32)
                        ReadUInt16(); // reserved
                        var length12 = ReadUInt32();
                        var language12 = ReadUInt32();
                        var numGroups = ReadUInt32();

                        for (uint g = 0; g < numGroups; g++)
                        {
                            var startCharCode = ReadUInt32();
                            var endCharCode = ReadUInt32();
                            var startGlyphId = ReadUInt32();

                            for (uint c = startCharCode; c <= endCharCode; c++)
                            {
                                // Only handle BMP range
                                if (c > 0xFFFF) continue;

                                if (!IsInFilter((char)c))
                                    continue;

                                var glyphId = (ushort)(startGlyphId + (c - startCharCode));
                                if (!_ttf._glyphs.ContainsKey((char)c))
                                {
                                    var glyph = new Glyph { id = glyphId, codepoint = (char)c };
                                    _ttf._glyphs[(char)c] = glyph;
                                    if (glyphId < _glyphsById.Length)
                                        _glyphsById[glyphId] = glyph;
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

            private bool SeekToGlyph(Glyph glyph) => SeekToGlyphById(glyph.id);

            private bool SeekToGlyphById(ushort glyphId)
            {
                // Seek to the glyph in the GLYF table
                if (_indexToLocFormat == 1)
                {
                    Seek(TableName.LOCA, glyphId * 4);
                    var offset = ReadUInt32();
                    var length = ReadUInt32() - offset;

                    // Empty glyph
                    if (length == 0)
                        return false;

                    Seek(TableName.GLYF, offset);
                }
                else
                {
                    Seek(TableName.LOCA, glyphId * 2);

                    var offset = ReadUInt16() * 2;
                    var length = (ReadUInt16() * 2) - offset;

                    if (length == 0)
                        return false;

                    Seek(TableName.GLYF, offset);
                }
                return true;
            }

            // Load a component glyph by ID (for glyphs not in the character map)
            private Glyph? LoadComponentGlyph(ushort glyphId)
            {
                if (!SeekToGlyphById(glyphId))
                    return null;

                short numberOfContours = ReadInt16();

                // Don't support nested compound glyphs for now
                if (numberOfContours < 0)
                    return null;

                var glyph = new Glyph { id = glyphId };
                ReadSimpleGlyph(glyph, numberOfContours);

                // Cache it for future use
                if (glyphId < _glyphsById.Length)
                    _glyphsById[glyphId] = glyph;

                return glyph;
            }

            private void ReadGlyphs()
            {
                // Two-pass loading: simple glyphs first, then compound glyphs
                // This ensures component glyphs are available when compound glyphs reference them
                var compoundGlyphs = new List<Glyph>();

                // First pass: load simple glyphs
                foreach (var glyph in _ttf._glyphs.Values)
                {
                    if (!SeekToGlyph(glyph))
                        continue;

                    // Peek at numberOfContours to determine if compound
                    short numberOfContours = ReadInt16();
                    if (numberOfContours < 0)
                    {
                        // Compound glyph - save for second pass
                        compoundGlyphs.Add(glyph);
                        continue;
                    }

                    // Read simple glyph
                    ReadSimpleGlyph(glyph, numberOfContours);
                }

                // Second pass: load compound glyphs
                foreach (var glyph in compoundGlyphs)
                {
                    if (!SeekToGlyph(glyph))
                        continue;

                    // Skip numberOfContours (we already know it's negative)
                    ReadInt16();
                    ReadCompoundGlyph(glyph);
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

            [Flags]
            private enum CompoundFlags : ushort
            {
                ArgsAreWords = 0x0001,
                ArgsAreXYValues = 0x0002,
                RoundXYToGrid = 0x0004,
                WeHaveAScale = 0x0008,
                MoreComponents = 0x0020,
                WeHaveAnXAndYScale = 0x0040,
                WeHaveATwoByTwo = 0x0080,
                WeHaveInstructions = 0x0100,
                UseMyMetrics = 0x0200
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

                // Compound glyph?
                if (numberOfContours < 0)
                {
                    ReadCompoundGlyph(glyph);
                    return;
                }

                ReadSimpleGlyph(glyph, numberOfContours);
            }

            private void ReadSimpleGlyph(Glyph glyph, short numberOfContours)
            {
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

            private void ReadCompoundGlyph(Glyph glyph)
            {
                // Read bounding box
                double minx = ReadFUnit();
                double miny = ReadFUnit();
                double maxx = ReadFUnit();
                double maxy = ReadFUnit();

                var allPoints = new List<Point>();
                var allContours = new List<Contour>();

                CompoundFlags flags;
                do
                {
                    flags = (CompoundFlags)ReadUInt16();
                    ushort componentGlyphIndex = ReadUInt16();

                    // Read offset (x, y)
                    double offsetX, offsetY;
                    bool skipComponent = false;
                    if (flags.HasFlag(CompoundFlags.ArgsAreWords))
                    {
                        if (flags.HasFlag(CompoundFlags.ArgsAreXYValues))
                        {
                            offsetX = ReadInt16() * _scale.x;
                            offsetY = ReadInt16() * _scale.y;
                        }
                        else
                        {
                            // Point indices - not commonly used, skip this component
                            ReadInt16();
                            ReadInt16();
                            offsetX = offsetY = 0;
                            skipComponent = true;
                        }
                    }
                    else
                    {
                        if (flags.HasFlag(CompoundFlags.ArgsAreXYValues))
                        {
                            offsetX = (sbyte)_reader.ReadByte() * _scale.x;
                            offsetY = (sbyte)_reader.ReadByte() * _scale.y;
                        }
                        else
                        {
                            // Point indices - not commonly used, skip this component
                            _reader.ReadByte();
                            _reader.ReadByte();
                            offsetX = offsetY = 0;
                            skipComponent = true;
                        }
                    }

                    // Read transformation (scale/matrix) - must always read these to maintain stream position
                    double a = 1, b = 0, c = 0, d = 1;
                    if (flags.HasFlag(CompoundFlags.WeHaveAScale))
                    {
                        a = d = ReadInt16() / 16384.0; // F2Dot14 format
                    }
                    else if (flags.HasFlag(CompoundFlags.WeHaveAnXAndYScale))
                    {
                        a = ReadInt16() / 16384.0;
                        d = ReadInt16() / 16384.0;
                    }
                    else if (flags.HasFlag(CompoundFlags.WeHaveATwoByTwo))
                    {
                        a = ReadInt16() / 16384.0;
                        b = ReadInt16() / 16384.0;
                        c = ReadInt16() / 16384.0;
                        d = ReadInt16() / 16384.0;
                    }

                    if (skipComponent)
                        continue;

                    // Get component glyph - try cache first, then load on demand
                    var componentGlyph = componentGlyphIndex < _glyphsById.Length ? _glyphsById[componentGlyphIndex] : null;

                    // If component not in cache, load it directly from font
                    if (componentGlyph?.points == null && componentGlyphIndex < _glyphsById.Length)
                    {
                        long savedPosition = Position;
                        componentGlyph = LoadComponentGlyph(componentGlyphIndex);
                        Seek(savedPosition);
                    }

                    if (componentGlyph?.points != null)
                    {
                        int pointOffset = allPoints.Count;

                        // Check if transformation flips winding (negative determinant)
                        double det = a * d - b * c;
                        bool flipWinding = det < 0;

                        // Transform and add points for each contour
                        foreach (var contour in componentGlyph.contours)
                        {
                            int contourStart = allPoints.Count;

                            if (flipWinding)
                            {
                                // Reverse point order to maintain correct winding
                                for (int i = contour.length - 1; i >= 0; i--)
                                {
                                    var point = componentGlyph.points[contour.start + i];
                                    var transformed = new Point
                                    {
                                        curve = point.curve,
                                        xy = new Vector2Double(
                                            point.xy.x * a + point.xy.y * b + offsetX,
                                            point.xy.x * c + point.xy.y * d + offsetY
                                        )
                                    };
                                    allPoints.Add(transformed);
                                }
                            }
                            else
                            {
                                // Normal order
                                for (int i = 0; i < contour.length; i++)
                                {
                                    var point = componentGlyph.points[contour.start + i];
                                    var transformed = new Point
                                    {
                                        curve = point.curve,
                                        xy = new Vector2Double(
                                            point.xy.x * a + point.xy.y * b + offsetX,
                                            point.xy.x * c + point.xy.y * d + offsetY
                                        )
                                    };
                                    allPoints.Add(transformed);
                                }
                            }

                            allContours.Add(new Contour
                            {
                                start = contourStart,
                                length = contour.length
                            });
                        }
                    }

                } while (flags.HasFlag(CompoundFlags.MoreComponents));

                // Skip instructions if present
                if (flags.HasFlag(CompoundFlags.WeHaveInstructions))
                {
                    var instructionLength = ReadUInt16();
                    _reader.ReadBytes(instructionLength);
                }

                glyph.points = allPoints.ToArray();
                glyph.contours = allContours.ToArray();
                glyph.size = new Vector2Double(maxx - minx, maxy - miny);
                glyph.bearing = new Vector2Double(minx, maxy);
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

                foreach (var glyph in _ttf._glyphs.Values)
                {
                    // If the glyph is past the end of the total number of metrics
                    // then it is contained in the end run..
                    if (glyph.id >= metricCount)
                        // TODO: implement end run..
                        throw new NotImplementedException();

                    Seek(TableName.HMTX, glyph.id * 4);
                    glyph.advance = ReadUFUnit();
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

                                var key = ((uint)left.codepoint << 16) | (uint)right.codepoint;
                                _ttf._kerning[key] = (float)kern;
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

                _ttf._glyphs = new Dictionary<char, Glyph>();
                _ttf._kerning = new Dictionary<uint, float>();

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
