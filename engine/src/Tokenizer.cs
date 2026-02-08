using System.Numerics;

namespace NoZ;

public enum TokenType
{
    None,
    Int,
    Float,
    String,
    Identifier,
    Vec2,
    Vec3,
    Vec4,
    Delimiter,
    Color,
    Bool,
    EOF
}

public struct Token
{
    public int Start;
    public int Length;
    public int Line;
    public int Column;
    public TokenType Type;

    public int IntValue;
    public float FloatValue;
    public bool BoolValue;
    public Color ColorValue;
    public Vector2 Vec2Value;
    public Vector3 Vec3Value;
    public Vector4 Vec4Value;
}

public ref struct Tokenizer
{
    private struct NamedColor(string name, Color color)
    {
        public readonly string Name = name;
        public readonly Color Color = color;
    }
    
    private static readonly NamedColor[] NamedColors =
    [
        new("black", new Color(0f, 0f, 0f, 1f)),
        new("white", new Color(1f, 1f, 1f, 1f)),
        new("red", new Color(1f, 0f, 0f, 1f)),
        new("green", new Color(0f, 0.5f, 0f, 1f)),
        new("blue", new Color(0f, 0f, 1f, 1f)),
        new("yellow", new Color(1f, 1f, 0f, 1f)),
        new("cyan", new Color(0f, 1f, 1f, 1f)),
        new("magenta", new Color(1f, 0f, 1f, 1f)),
        new("gray", new Color(0.5f, 0.5f, 0.5f, 1f)),
        new("grey", new Color(0.5f, 0.5f, 0.5f, 1f)),
        new("orange", new Color(1f, 0.65f, 0f, 1f)),
        new("pink", new Color(1f, 0.75f, 0.8f, 1f)),
        new("purple", new Color(0.5f, 0f, 0.5f, 1f)),
        new("brown", new Color(0.65f, 0.16f, 0.16f, 1f)),
        new("transparent", new Color(0f, 0f, 0f, 0f))
    ];
    
    private readonly ReadOnlySpan<char> _input;
    private int _position;
    private readonly int _length;
    private int _line = 1;
    private int _column = 1;
    private Token _nextToken;
    private Token _currentToken;

    public bool IsEOF => _nextToken.Type == TokenType.EOF;

    public Tokenizer(ReadOnlySpan<char> input)
    {
        _input = input;
        _length = _input.Length;

        ReadToken();
        _currentToken = _nextToken;
    }

    public ReadOnlySpan<char> GetSpan() => GetSpan(_currentToken);

    public ReadOnlySpan<char> GetSpan(Token token)
    {
        if (token.Length <= 0 || token.Start < 0 || token.Start >= _input.Length)
            return ReadOnlySpan<char>.Empty;
        return _input.Slice(token.Start, Math.Min(token.Length, _input.Length - token.Start));
    }

    public string GetString() => GetSpan(_currentToken).ToString();

    public string GetString(Token token) => GetSpan(token).ToString();

    public bool Equals(string value, bool ignoreCase = false)
    {
        return Equals(_currentToken, value, ignoreCase);
    }

    public bool Equals(Token token, ReadOnlySpan<char> value, bool ignoreCase = false)
    {
        var span = GetSpan(token);
        var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        return span.Equals(value, comparison);
    }

    public bool Peek(ReadOnlySpan<char> value, bool ignoreCase = false)
    {
        return Equals(_nextToken, value, ignoreCase);
    }

    public bool ExpectToken(out Token token)
    {
        if (_nextToken.Type == TokenType.EOF)
        {
            token = default;
            return false;
        }

        token = _nextToken;
        ReadToken();
        return true;
    }

    public bool ExpectLine(out string value)
    {
        value = "";

        var rewindPos = _nextToken.Start;
        if (_nextToken.Type == TokenType.String && rewindPos > 0)
        {
            var prev = _input[rewindPos - 1];
            if (prev == '"' || prev == '\'')
                rewindPos--;
        }

        _position = rewindPos;

        if (!HasTokens())
            return false;

        var start = _position;
        while (HasTokens() && _input[_position] != '\n')
        {
            NextChar();
        }

        var end = _position;
        NextChar(); // skip newline

        value = _input.Slice(start, end - start).Trim().ToString();

        if (string.IsNullOrEmpty(value))
            return false;

        ReadToken();
        return true;
    }

    public string? ExpectQuotedString()
    {
        return ExpectQuotedString(out var result) ? result : null;
    }

    public bool ExpectQuotedString(out string value)
    {
        if (_nextToken.Type != TokenType.String)
        {
            value = "";
            return false;
        }

        value = GetString(_nextToken);
        ReadToken();
        return true;
    }

    public int ExpectInt(int defaultValue = 0)
    {
        if (!ExpectInt(out int value))
            return defaultValue;
        return value;
    }

    public bool ExpectInt(out int value)
    {
        if (_nextToken.Type != TokenType.Int)
        {
            value = 0;
            return false;
        }

        value = _nextToken.IntValue;
        ReadToken();
        return true;
    }

    public float ExpectFloat(float defaultValue = 0f)
    {
        if (!ExpectFloat(out float value))
            return defaultValue;
        return value;
    }

    public bool ExpectFloat(out float value)
    {
        if (_nextToken.Type == TokenType.Int)
        {
            value = _nextToken.IntValue;
            ReadToken();
            return true;
        }

        if (_nextToken.Type != TokenType.Float)
        {
            value = 0;
            return false;
        }

        value = _nextToken.FloatValue;
        ReadToken();
        return true;
    }

    public bool ExpectBool(bool defaultValue = false)
    {
        if (!ExpectBool(out bool value))
            return defaultValue;
        return value;
    }

    public bool ExpectBool(out bool value)
    {
        if (_nextToken.Type == TokenType.Bool)
        {
            value = _nextToken.BoolValue;
            ReadToken();
            return true;
        }

        if (!ExpectInt(out int i))
        {
            value = false;
            return false;
        }
        value = i != 0;
        return true;
    }

    public string ExpectIdentifier()
    {
        if (!ExpectIdentifier(out string value))
            return "";

        return value;
    }

    public bool ExpectIdentifier(string? match = null)
    {
        if (_nextToken.Type != TokenType.Identifier)
            return false;

        if (match != null && !Equals(_nextToken, match, ignoreCase: false))
            return false;

        ReadToken();
        return true;
    }

    public bool ExpectIdentifier(out string value)
    {
        if (_nextToken.Type != TokenType.Identifier)
        {
            value = "";
            return false;
        }

        ReadToken();
        value = GetString();
        return true;
    }

    public bool ExpectVec2(out Vector2 value)
    {
        if (_nextToken.Type != TokenType.Vec2)
        {
            value = default;
            return false;
        }

        value = _nextToken.Vec2Value;
        ReadToken();
        return true;
    }

    public bool ExpectVec3(out Vector3 value)
    {
        if (_nextToken.Type != TokenType.Vec3)
        {
            value = default;
            return false;
        }

        value = _nextToken.Vec3Value;
        ReadToken();
        return true;
    }

    public bool ExpectVec4(out Vector4 value)
    {
        if (_nextToken.Type != TokenType.Vec4)
        {
            value = default;
            return false;
        }

        value = _nextToken.Vec4Value;
        ReadToken();
        return true;
    }

    public bool ExpectColor(out Color value)
    {
        if (_nextToken.Type != TokenType.Color)
        {
            value = default;
            return false;
        }

        value = _nextToken.ColorValue;
        ReadToken();
        return true;
    }

    public bool ExpectDelimiter(char c)
    {
        if (_nextToken.Type != TokenType.Delimiter)
            return false;

        if (_nextToken.Length != 1 || _input[_nextToken.Start] != c)
            return false;

        ReadToken();
        return true;
    }

    private bool HasTokens() => _position < _length;

    private char PeekChar()
    {
        if (!HasTokens()) return '\0';
        return _input[_position];
    }

    private char NextChar()
    {
        if (!HasTokens()) return '\0';

        var c = _input[_position++];
        if (c == '\n')
        {
            _line++;
            _column = 1;
        }
        else
        {
            _column++;
        }
        return c;
    }

    private void SkipWhitespace()
    {
        while (HasTokens())
        {
            var c = PeekChar();
            if (!char.IsWhiteSpace(c))
                return;
            NextChar();
        }
    }

    private void BeginToken()
    {
        _nextToken.Line = _line;
        _nextToken.Column = _column;
        _nextToken.Start = _position;
        _nextToken.Length = 0;
    }

    private void EndToken(TokenType type)
    {
        _nextToken.Length = _position - _nextToken.Start;
        _nextToken.Type = type;
    }

    private static bool IsDelimiter(char c) => c == '[' || c == ']' || c == '=' || c == ',' || c == '<' || c == '>' || c == ':';

    private static bool IsIdentifierStart(char c) => char.IsLetter(c) || c == '_';

    private static bool IsIdentifierChar(char c) => char.IsLetterOrDigit(c) || c == '_' || c == ':' || c == '/' || c == '-';

    private static bool IsNumberStart(char c) => char.IsDigit(c) || c == '-' || c == '+' || c == '.';

    private bool ReadQuotedString()
    {
        var quote = PeekChar();
        if (quote != '"' && quote != '\'')
            return false;

        NextChar(); // skip opening quote
        BeginToken();

        while (HasTokens())
        {
            var c = NextChar();

            if (c == quote)
            {
                EndToken(TokenType.String);
                _nextToken.Length--; // exclude closing quote
                return true;
            }

            if (c == '\\' && HasTokens())
            {
                NextChar(); // skip escaped char
            }
        }

        EndToken(TokenType.String);
        return true;
    }

    private bool ReadBool()
    {
        if (_position + 4 <= _length && _input.Slice(_position, 4).Equals("true", StringComparison.OrdinalIgnoreCase))
        {
            BeginToken();
            for (int i = 0; i < 4; i++) NextChar();
            EndToken(TokenType.Bool);
            _nextToken.BoolValue = true;
            return true;
        }

        if (_position + 5 <= _length && _input.Slice(_position, 5).Equals("false", StringComparison.OrdinalIgnoreCase))
        {
            BeginToken();
            for (int i = 0; i < 5; i++) NextChar();
            EndToken(TokenType.Bool);
            _nextToken.BoolValue = false;
            return true;
        }

        return false;
    }

    private bool ReadNumber()
    {
        var c = PeekChar();
        if (!IsNumberStart(c))
            return false;

        // Don't treat lone +/- as number
        if ((c == '-' || c == '+') && _position + 1 < _length && !char.IsDigit(_input[_position + 1]) && _input[_position + 1] != '.')
            return false;

        BeginToken();

        var hasDecimal = false;
        var hasExponent = false;

        while (HasTokens())
        {
            c = PeekChar();

            if (c == '.' && !hasDecimal && !hasExponent)
            {
                hasDecimal = true;
                NextChar();
                continue;
            }

            if ((c == 'E' || c == 'e') && !hasExponent)
            {
                hasExponent = true;
                NextChar();
                if (HasTokens() && (PeekChar() == '+' || PeekChar() == '-'))
                    NextChar();
                continue;
            }

            if (char.IsDigit(c))
            {
                NextChar();
                continue;
            }

            if ((c == '-' || c == '+') && _nextToken.Start == _position)
            {
                NextChar();
                continue;
            }

            break;
        }

        EndToken((hasDecimal || hasExponent) ? TokenType.Float : TokenType.Int);

        var span = GetSpan(_nextToken);
        if (hasDecimal || hasExponent)
            _nextToken.FloatValue = float.TryParse(span, out float f) ? f : 0f;
        else
            _nextToken.IntValue = int.TryParse(span, out int i) ? i : 0;

        return true;
    }

    private bool ReadIdentifier()
    {
        if (!IsIdentifierStart(PeekChar()))
            return false;

        BeginToken();

        while (HasTokens() && IsIdentifierChar(PeekChar()))
            NextChar();

        EndToken(TokenType.Identifier);
        return true;
    }

    private bool ReadVec(bool startToken = true)
    {
        if (PeekChar() != '(')
            return false;

        if (startToken)
            BeginToken();

        var savedToken = _nextToken;

        SkipWhitespace();
        NextChar(); // skip '('

        var componentCount = 0;
        var components = new float[4];

        while (HasTokens())
        {
            SkipWhitespace();

            if (PeekChar() == ')')
            {
                NextChar();
                break;
            }

            if (componentCount > 0)
            {
                if (PeekChar() == ',')
                {
                    NextChar();
                    SkipWhitespace();
                }
            }

            if (!ReadNumber())
                break;

            if (componentCount < 4)
            {
                if (_nextToken.Type == TokenType.Int)
                    components[componentCount] = _nextToken.IntValue;
                else
                    components[componentCount] = _nextToken.FloatValue;
            }
            componentCount++;
        }

        _nextToken = savedToken;

        if (componentCount == 1)
        {
            _nextToken.FloatValue = components[0];
            EndToken(TokenType.Float);
        }
        else if (componentCount == 2)
        {
            _nextToken.Vec2Value = new Vector2(components[0], components[1]);
            EndToken(TokenType.Vec2);
        }
        else if (componentCount == 3)
        {
            _nextToken.Vec3Value = new Vector3(components[0], components[1], components[2]);
            EndToken(TokenType.Vec3);
        }
        else if (componentCount == 4)
        {
            _nextToken.Vec4Value = new Vector4(components[0], components[1], components[2], components[3]);
            EndToken(TokenType.Vec4);
        }
        else
        {
            return false;
        }

        return true;
    }

    private bool ReadColor()
    {
        var c = PeekChar();

        // Hex colors: #RGB, #RRGGBB, #RRGGBBAA
        if (c == '#')
        {
            BeginToken();
            NextChar(); // skip #

            while (HasTokens() && IsHexDigit(PeekChar()))
                NextChar();

            EndToken(TokenType.Color);

            var hex = GetSpan(_nextToken).Slice(1); // skip #
            _nextToken.ColorValue = ParseHexColor(hex);
            return true;
        }

        // rgba(r, g, b, a)
        if (_position + 4 <= _length && _input.Slice(_position, 4).Equals("rgba", StringComparison.OrdinalIgnoreCase))
        {
            BeginToken();
            for (var i = 0; i < 4; i++) NextChar();
            SkipWhitespace();
            ReadVec(false);

            if (_nextToken.Type == TokenType.Vec4)
            {
                _nextToken.ColorValue = new Color(
                    _nextToken.Vec4Value.X / 255f,
                    _nextToken.Vec4Value.Y / 255f,
                    _nextToken.Vec4Value.Z / 255f,
                    _nextToken.Vec4Value.W
                );
            }
            _nextToken.Type = TokenType.Color;
            return true;
        }

        // rgb(r, g, b)
        if (_position + 3 <= _length && _input.Slice(_position, 3).Equals("rgb", StringComparison.OrdinalIgnoreCase))
        {
            BeginToken();
            for (var i = 0; i < 3; i++) NextChar();
            SkipWhitespace();
            ReadVec(false);

            if (_nextToken.Type == TokenType.Vec3)
            {
                _nextToken.ColorValue = new Color(
                    _nextToken.Vec3Value.X / 255f,
                    _nextToken.Vec3Value.Y / 255f,
                    _nextToken.Vec3Value.Z / 255f,
                    1f
                );
            }
            _nextToken.Type = TokenType.Color;
            return true;
        }

        foreach (var namedColor in NamedColors)
        {
            var name = namedColor.Name;
            var color = namedColor.Color;
            if (_position + name.Length > _length ||
                !_input.Slice(_position, name.Length).Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            BeginToken();
            for (var i = 0; i < name.Length; i++) NextChar();
            EndToken(TokenType.Color);
            _nextToken.ColorValue = color;
            return true;
        }

        return false;
    }

    private static bool IsHexDigit(char c) =>
        c is >= '0' and <= '9' || c is >= 'a' and <= 'f' || c is >= 'A' and <= 'F';

    private static int ParseHexDigit(char c)
    {
        if (c is >= '0' and <= '9') return c - '0';
        if (c is >= 'a' and <= 'f') return c - 'a' + 10;
        if (c is >= 'A' and <= 'F') return c - 'A' + 10;
        return 0;
    }

    private static int ParseHexByte(ReadOnlySpan<char> hex) =>
        ParseHexDigit(hex[0]) * 16 + ParseHexDigit(hex[1]);

    private static Color ParseHexColor(ReadOnlySpan<char> hex)
    {
        switch (hex.Length)
        {
            // #RGB
            case 3:
            {
                var r = ParseHexDigit(hex[0]);
                var g = ParseHexDigit(hex[1]);
                var b = ParseHexDigit(hex[2]);
                return new Color(r / 15f, g / 15f, b / 15f, 1f);
            }
            // #RRGGBB
            case 6:
            {
                var r = ParseHexByte(hex.Slice(0, 2));
                var g = ParseHexByte(hex.Slice(2, 2));
                var b = ParseHexByte(hex.Slice(4, 2));
                return new Color(r / 255f, g / 255f, b / 255f, 1f);
            }
            // #RRGGBBAA
            case 8:
            {
                var r = ParseHexByte(hex.Slice(0, 2));
                var g = ParseHexByte(hex.Slice(2, 2));
                var b = ParseHexByte(hex.Slice(4, 2));
                var a = ParseHexByte(hex.Slice(6, 2));
                return new Color(r / 255f, g / 255f, b / 255f, a / 255f);
            }
            default:
                return Color.White;
        }
    }

    private bool ReadToken()
    {
        _currentToken = _nextToken;

        SkipWhitespace();

        if (!HasTokens())
        {
            BeginToken();
            EndToken(TokenType.EOF);
            return false;
        }

        var c = PeekChar();

        // Delimiter
        if (IsDelimiter(c))
        {
            BeginToken();
            NextChar();
            EndToken(TokenType.Delimiter);
            return true;
        }

        // Quoted string
        if (ReadQuotedString())
            return true;

        // Boolean
        if (ReadBool())
            return true;

        // Color
        if (ReadColor())
            return true;

        // Vector
        if (ReadVec())
            return true;

        // Number
        if (ReadNumber())
            return true;

        // Identifier
        if (ReadIdentifier())
            return true;

        // Unknown - skip character
        BeginToken();
        NextChar();
        EndToken(TokenType.None);
        return true;
    }
}
