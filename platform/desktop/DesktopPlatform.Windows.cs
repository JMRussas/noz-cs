//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.InteropServices;
using static SDL.SDL3;

namespace NoZ.Platform;

internal static partial class User32
{
    public const int WS_CHILD = 0x40000000;
    public const int ES_AUTOHSCROLL = 0x0080;
    public const int ES_LEFT = 0x0000;
    public const int ES_PASSWORD = 0x0020;
    public const int WM_SETFONT = 0x0030;
    public const int WM_COMMAND = 0x0111;
    public const int EN_CHANGE = 0x0300;
    public const int EM_SETSEL = 0x00B1;
    public const int EM_SETMARGINS = 0x00D3;
    public const int EM_SETPASSWORDCHAR = 0x00CC;
    public const int EM_SCROLLCARET = 0x00B7;
    public const int EC_LEFTMARGIN = 0x0001;
    public const int EC_RIGHTMARGIN = 0x0002;
    public const int SWP_SHOWWINDOW = 0x0040;
    public const int SW_HIDE = 0;
    public const int GWL_STYLE = -16;
    public const int GWLP_WNDPROC = -4;
    public const int WM_CHAR = 0x0102;
    public const int WM_KEYDOWN = 0x0100;
    public const int WM_KEYUP = 0x0101;
    public const int WM_CTLCOLOREDIT = 0x0133;
    public const int VK_RETURN = 0x0D;
    public const int VK_ESCAPE = 0x1B;
    public const int VK_TAB = 0x09;
    public const int VK_UP = 0x26;
    public const int VK_DOWN = 0x28;
    public const int FW_THIN = 100;
    public const int FW_LIGHT = 300;
    public const int FW_NORMAL = 400;
    public const int DEFAULT_CHARSET = 1;
    public const int OUT_DEFAULT_PRECIS = 0;
    public const int CLIP_DEFAULT_PRECIS = 0;
    public const int ANTIALIASED_QUALITY = 4;
    public const int DRAFT_QUALITY = 1;
    public const int PROOF_QUALITY = 2;
    public const int CLEARTYPE_QUALITY = 5;
    public const int DEFAULT_PITCH = 0;
    public const int FF_DONTCARE = 0;
    public const int EM_SETCUEBANNER = 0x1501;
    public const int WM_PAINT = 0x000F;
    public const int WM_ERASEBKGND = 0x0014;
    public const int DT_SINGLELINE = 0x0020;
    public const int DT_VCENTER = 0x0004;
    public const int DT_LEFT = 0x0000;
    public const int WM_LBUTTONDOWN = 0x0201;
    public const int WM_LBUTTONUP = 0x0202;
    public const int WM_LBUTTONDBLCLK = 0x0203;
    public const int WS_EX_TRANSPARENT = 0x00000020;
    public const int WS_CLIPCHILDREN = 0x02000000;

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetClientRect(nint hWnd, out RECT lpRect);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int DrawTextW(nint hdc, string lpchText, int cchText, ref RECT lprc, uint format);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT
    {
        public int Left, Top, Right, Bottom;
    }

    public delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    public static partial nint CreateWindowExW(
        int dwExStyle,
        [MarshalAs(UnmanagedType.LPWStr)] string lpClassName,
        [MarshalAs(UnmanagedType.LPWStr)] string lpWindowName,
        int dwStyle,
        int x, int y, int nWidth, int nHeight,
        nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyWindow(nint hWnd);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ShowWindow(nint hWnd, int nCmdShow);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowPos(nint hWnd, nint hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);

    [LibraryImport("user32.dll")]
    public static partial nint SetFocus(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial nint SendMessageW(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern nint SendMessageW(nint hWnd, uint msg, nint wParam, string lParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    public static extern int GetWindowTextW(nint hWnd, char[] lpString, int nMaxCount);

    [LibraryImport("user32.dll")]
    public static partial int GetWindowTextLengthW(nint hWnd);

    [LibraryImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetWindowTextW(nint hWnd,
        [MarshalAs(UnmanagedType.LPWStr)] string lpString);

    [LibraryImport("user32.dll")]
    public static partial nint GetWindowLongPtrW(nint hWnd, int nIndex);

    [LibraryImport("user32.dll")]
    public static partial nint SetWindowLongPtrW(nint hWnd, int nIndex, nint dwNewLong);

    [LibraryImport("user32.dll")]
    public static partial nint CallWindowProcW(nint lpPrevWndFunc, nint hWnd,
        uint msg, nint wParam, nint lParam);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool InvalidateRect(nint hWnd, nint lpRect,
        [MarshalAs(UnmanagedType.Bool)] bool bErase);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool UpdateWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool RedrawWindow(nint hWnd, nint lprcUpdate, nint hrgnUpdate, uint flags);

    public const uint RDW_INVALIDATE = 0x0001;
    public const uint RDW_UPDATENOW = 0x0100;
    public const uint RDW_ERASE = 0x0004;

    [LibraryImport("user32.dll")]
    public static partial nint GetDC(nint hWnd);

    [LibraryImport("user32.dll")]
    public static partial int ReleaseDC(nint hWnd, nint hDC);

    [LibraryImport("kernel32.dll")]
    public static partial nint GetModuleHandleW([MarshalAs(UnmanagedType.LPWStr)] string? lpModuleName);

    [LibraryImport("gdi32.dll", SetLastError = true)]
    public static partial nint CreateFontW(
        int cHeight, int cWidth, int cEscapement, int cOrientation, int cWeight,
        uint bItalic, uint bUnderline, uint bStrikeOut, uint iCharSet,
        uint iOutPrecision, uint iClipPrecision, uint iQuality,
        uint iPitchAndFamily, [MarshalAs(UnmanagedType.LPWStr)] string pszFaceName);

    [LibraryImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DeleteObject(nint ho);

    [LibraryImport("gdi32.dll")]
    public static partial nint CreateSolidBrush(uint color);

    [LibraryImport("gdi32.dll")]
    public static partial nint SelectObject(nint hdc, nint h);

    [LibraryImport("gdi32.dll")]
    public static partial uint SetTextColor(nint hdc, uint color);

    [LibraryImport("gdi32.dll")]
    public static partial uint SetBkColor(nint hdc, uint color);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetTextMetricsW(nint hdc, out TEXTMETRICW lptm);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetCharABCWidthsW(nint hdc, uint first, uint last, [Out] ABC[] abc);

    [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
    public static extern bool GetGlyphOutlineW(nint hdc, uint uChar, uint fuFormat, out GLYPHMETRICS lpgm, uint cjBuffer, nint pvBuffer, ref MAT2 lpmat2);

    public const uint GGO_METRICS = 0;

    [StructLayout(LayoutKind.Sequential)]
    public struct ABC
    {
        public int abcA;
        public uint abcB;
        public int abcC;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct GLYPHMETRICS
    {
        public uint gmBlackBoxX;
        public uint gmBlackBoxY;
        public POINT gmptGlyphOrigin;
        public short gmCellIncX;
        public short gmCellIncY;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct POINT
    {
        public int x;
        public int y;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct MAT2
    {
        public FIXED eM11;
        public FIXED eM12;
        public FIXED eM21;
        public FIXED eM22;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct FIXED
    {
        public short fract;
        public short value;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    public struct TEXTMETRICW
    {
        public int tmHeight;
        public int tmAscent;
        public int tmDescent;
        public int tmInternalLeading;
        public int tmExternalLeading;
        public int tmAveCharWidth;
        public int tmMaxCharWidth;
        public int tmWeight;
        public int tmOverhang;
        public int tmDigitizedAspectX;
        public int tmDigitizedAspectY;
        public char tmFirstChar;
        public char tmLastChar;
        public char tmDefaultChar;
        public char tmBreakChar;
        public byte tmItalic;
        public byte tmUnderlined;
        public byte tmStruckOut;
        public byte tmPitchAndFamily;
        public byte tmCharSet;
    }
}

public unsafe partial class SDLPlatform
{
    private nint _editHwnd;
    private nint _editFont;
    private nint _editBgBrush;
    private bool _editVisible;
    private int _editFontSize = -1;
    private int _editFontHeight = 0;
    private RectInt _editRect;
    private uint _editTextColor;
    private uint _editBgColor = uint.MaxValue;
    private string _editLastText = "";
    private nint _editOriginalWndProc;
    private nint _parentHwnd;
    private nint _parentOriginalWndProc;
    private User32.WndProc? _editWndProcDelegate;
    private User32.WndProc? _parentWndProcDelegate;
    private string? _editPlaceholder;
    private uint _editPlaceholderColor;
    private string? _editFontFamily;

    public bool IsTextboxVisible => _editVisible;

    private void InitNativeTextInput()
    {
        if (!OperatingSystem.IsWindows()) return;

        var props = SDL_GetWindowProperties(_window);
        _parentHwnd = SDL_GetPointerProperty(props, SDL_PROP_WINDOW_WIN32_HWND_POINTER, nint.Zero);
        if (_parentHwnd == nint.Zero) return;        

        var hInstance = User32.GetModuleHandleW(null);

        _editHwnd = User32.CreateWindowExW(
            0,
            "EDIT",
            "",
            User32.WS_CHILD | User32.ES_AUTOHSCROLL | User32.ES_LEFT,
            0, 0, 0, 0,
            _parentHwnd,
            nint.Zero,
            hInstance,
            nint.Zero
        );

        if (_editHwnd == nint.Zero) return;

        User32.SendMessageW(_editHwnd, User32.EM_SETMARGINS, User32.EC_LEFTMARGIN | User32.EC_RIGHTMARGIN, 0);

        _editWndProcDelegate = EditSubclassProc;
        _editOriginalWndProc = User32.SetWindowLongPtrW(
            _editHwnd,
            User32.GWLP_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(_editWndProcDelegate));

        _parentWndProcDelegate = ParentSubclassProc;
        _parentOriginalWndProc = User32.SetWindowLongPtrW(
            _parentHwnd,
            User32.GWLP_WNDPROC,
            Marshal.GetFunctionPointerForDelegate(_parentWndProcDelegate));
    }

    private nint HandleEditPaint(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        var result = User32.CallWindowProcW(_editOriginalWndProc, hWnd, msg, wParam, lParam);
        var textLength = User32.GetWindowTextLengthW(hWnd);
        if (textLength != 0 || _editPlaceholder == null)
            return result;

        var hdc = User32.GetDC(hWnd);
        var oldFont = User32.SelectObject(hdc, _editFont);
        User32.SetTextColor(hdc, _editPlaceholderColor);
        User32.SetBkColor(hdc, _editBgColor);

        User32.GetClientRect(hWnd, out var rect);
        rect.Left += 1;
        User32.DrawTextW(
            hdc,
            _editPlaceholder,
            _editPlaceholder.Length,
            ref rect,
            User32.DT_SINGLELINE | User32.DT_LEFT);

        User32.SelectObject(hdc, oldFont);
        User32.ReleaseDC(hWnd, hdc);
        return result;
    }

    private nint EditSubclassProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        // Forward all key events to the input system
        if (msg == User32.WM_KEYDOWN || msg == User32.WM_KEYUP)
        {
            var code = VirtualKeyToInputCode((int)wParam);
            if (code != InputCode.None)
            {
                var evt = msg == User32.WM_KEYDOWN
                    ? PlatformEvent.KeyDown(code)
                    : PlatformEvent.KeyUp(code);
                OnEvent?.Invoke(evt);
            }

            // For special keys (Enter/Escape/Tab/Arrows), don't pass to textbox
            if (wParam == User32.VK_RETURN || wParam == User32.VK_ESCAPE || wParam == User32.VK_TAB ||
                wParam == User32.VK_UP || wParam == User32.VK_DOWN)
                return 0;
        }

        // Block WM_CHAR for special keys
        if (msg == User32.WM_CHAR)
        {
            if (wParam == User32.VK_RETURN || wParam == User32.VK_ESCAPE || wParam == User32.VK_TAB)
                return 0;
        }

        //if (msg == User32.WM_ERASEBKGND)
        //    return 1;

        // Handle WM_PAINT to draw placeholder when text is empty
        if (msg == User32.WM_PAINT)
            return HandleEditPaint(hWnd, msg, wParam, lParam);

        // Force repaint when text changes (to show/hide placeholder)
        if (msg == User32.WM_CHAR || msg == User32.WM_KEYDOWN)
        {
            var result = User32.CallWindowProcW(_editOriginalWndProc, hWnd, msg, wParam, lParam);
            if (!string.IsNullOrEmpty(_editPlaceholder))
            {
                User32.InvalidateRect(hWnd, nint.Zero, true);
            }
            return result;
        }

        // Force repaint after mouse clicks to ensure placeholder is redrawn
        if ((msg == User32.WM_LBUTTONDOWN || msg == User32.WM_LBUTTONUP || msg == User32.WM_LBUTTONDBLCLK) &&
            !string.IsNullOrEmpty(_editPlaceholder))
        {
            var result = User32.CallWindowProcW(_editOriginalWndProc, hWnd, msg, wParam, lParam);
            User32.RedrawWindow(hWnd, nint.Zero, nint.Zero, User32.RDW_INVALIDATE | User32.RDW_UPDATENOW | User32.RDW_ERASE);
            return result;
        }

        return User32.CallWindowProcW(_editOriginalWndProc, hWnd, msg, wParam, lParam);
    }

    private nint ParentSubclassProc(nint hWnd, uint msg, nint wParam, nint lParam)
    {
        if (msg == User32.WM_CTLCOLOREDIT && lParam == _editHwnd)
        {
            var hdc = wParam;
            User32.SetTextColor(hdc, _editTextColor);
            User32.SetBkColor(hdc, _editBgColor);
            return _editBgBrush;
        }

        return User32.CallWindowProcW(_parentOriginalWndProc, hWnd, msg, wParam, lParam);
    }

    private void ShutdownNativeTextInput()
    {
        if (_editHwnd != nint.Zero && _editOriginalWndProc != nint.Zero)
            User32.SetWindowLongPtrW(_editHwnd, User32.GWLP_WNDPROC, _editOriginalWndProc);

        if (_parentHwnd != nint.Zero && _parentOriginalWndProc != nint.Zero)
            User32.SetWindowLongPtrW(_parentHwnd, User32.GWLP_WNDPROC, _parentOriginalWndProc);

        if (_editHwnd != nint.Zero)
            User32.DestroyWindow(_editHwnd);

        if (_editFont != nint.Zero)
            User32.DeleteObject(_editFont);

        if (_editBgBrush != nint.Zero)
            User32.DeleteObject(_editBgBrush);

        _editOriginalWndProc = nint.Zero;
        _parentOriginalWndProc = nint.Zero;
        _editHwnd = nint.Zero;
        _editFont = nint.Zero;
        _editBgBrush = nint.Zero;
        _editWndProcDelegate = null;
        _parentWndProcDelegate = null;
    }

    public void ShowTextbox(Rect rect, string text, NativeTextboxStyle style)
    {
        if (_editHwnd == nint.Zero) return;

        _editTextColor = ColorToColorRef(style.TextColor);
        _editPlaceholder = style.Placeholder;
        _editPlaceholderColor = ColorToColorRef(style.PlaceholderColor);

        var fontFamily = string.IsNullOrEmpty(style.FontFamily) ? "Segoe UI" : style.FontFamily;
        if (_editFontFamily != fontFamily)
        {
            _editFontFamily = fontFamily;
            _editFontSize = -1;
        }

        var bgColor = ColorToColorRef(style.BackgroundColor);
        if (_editBgColor != bgColor)
        {
            _editBgColor = bgColor;
            if (_editBgBrush != nint.Zero)
                User32.DeleteObject(_editBgBrush);
            _editBgBrush = User32.CreateSolidBrush(_editBgColor);
        }

        // Password mode
        var currentStyle = User32.GetWindowLongPtrW(_editHwnd, User32.GWL_STYLE);
        if (style.Password)
        {
            User32.SetWindowLongPtrW(_editHwnd, User32.GWL_STYLE, currentStyle | User32.ES_PASSWORD);
            User32.SendMessageW(_editHwnd, User32.EM_SETPASSWORDCHAR, (nint)'*', 0);
        }
        else
        {
            User32.SetWindowLongPtrW(_editHwnd, User32.GWL_STYLE, currentStyle & ~User32.ES_PASSWORD);
            User32.SendMessageW(_editHwnd, User32.EM_SETPASSWORDCHAR, 0, 0);
        }

        UpdateTextboxRectInternal(new RectInt((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height), style.FontSize);

        User32.SetWindowTextW(_editHwnd, text ?? "");
        User32.SendMessageW(_editHwnd, User32.EM_SETSEL, 0, -1); // Select all

        User32.SetFocus(_editHwnd);
        _editVisible = true;
        _editLastText = text ?? "";
    }

    public void HideTextbox()
    {
        if (_editHwnd == nint.Zero) return;

        // Send KeyUp for special keys to prevent them getting stuck
        // when focus changes away from the textbox
        OnEvent?.Invoke(PlatformEvent.KeyUp(InputCode.KeyEscape));
        OnEvent?.Invoke(PlatformEvent.KeyUp(InputCode.KeyEnter));
        OnEvent?.Invoke(PlatformEvent.KeyUp(InputCode.KeyTab));

        User32.ShowWindow(_editHwnd, User32.SW_HIDE);
        User32.InvalidateRect(_editHwnd, nint.Zero, true);
        User32.UpdateWindow(_editHwnd);
        _editVisible = false;
        _editRect = new RectInt(0, 0, 0, 0);
    }

    public void UpdateTextboxRect(Rect rect, int fontSize)
    {
        if (!_editVisible || _editHwnd == nint.Zero) return;
        UpdateTextboxRectInternal(new RectInt((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height), fontSize);
    }

    public bool UpdateTextboxText(ref string text)
    {
        if (_editHwnd == nint.Zero || !_editVisible) return false;

        var length = User32.GetWindowTextLengthW(_editHwnd);
        if (length > 0)
        {
            var buffer = new char[length + 1];
            User32.GetWindowTextW(_editHwnd, buffer, length + 1);
            var currentText = new string(buffer, 0, length);

            if (currentText != _editLastText)
            {
                _editLastText = currentText;
                text = currentText;
                return true;
            }
        }
        else if (_editLastText.Length > 0)
        {
            _editLastText = "";
            text = "";
            return true;
        }

        return false;
    }

    private void UpdateTextboxRectInternal(in RectInt rect, int fontSize)
    {
        UpdateEditFont(fontSize);

        if (_editRect != rect)
        {
            _editRect = rect;
            UpdateEditRect(rect);
            User32.RedrawWindow(_editHwnd, nint.Zero, nint.Zero, User32.RDW_INVALIDATE | User32.RDW_UPDATENOW | User32.RDW_ERASE);
        }
    }

    private void UpdateEditFont(int fontSize)
    {
        if (_editFontSize == fontSize) return;

        if (_editFont != nint.Zero)
            User32.DeleteObject(_editFont);

        _editFontSize = fontSize;
        var fontFamily = _editFontFamily ?? "Segoe UI";
        var weight = User32.FW_NORMAL;

        // Extract weight from font family name if present (e.g., "Segoe UI Semibold" -> "Segoe UI", weight=600)
        var (baseFontFamily, extractedWeight) = ExtractFontWeight(fontFamily);
        if (extractedWeight > 0)
        {
            fontFamily = baseFontFamily;
            weight = extractedWeight;
        }

        // fontSize is already in physical pixels (scaled by UI scale)
        // Use negative height for character cell height
        _editFont = User32.CreateFontW(
            (int)-(fontSize + (fontSize * 0.03125f * 0.5f)),
            0,
            0,
            0,
            weight,
            0,
            0,
            0,
            User32.DEFAULT_CHARSET,
            User32.OUT_DEFAULT_PRECIS,
            User32.CLIP_DEFAULT_PRECIS,
            User32.DRAFT_QUALITY,
            User32.DEFAULT_PITCH | User32.FF_DONTCARE,
            fontFamily);

        // Get the actual font height
        var hdc = User32.GetDC(_editHwnd);
        var oldFont = User32.SelectObject(hdc, _editFont);
        User32.GetTextMetricsW(hdc, out var tm);
        User32.SelectObject(hdc, oldFont);
        User32.ReleaseDC(_editHwnd, hdc);
        _editFontHeight = tm.tmHeight;

        User32.SendMessageW(_editHwnd, User32.WM_SETFONT, _editFont, 1);
    }

    private void UpdateEditRect(in RectInt rect)
    {
        var hdc = User32.GetDC(_editHwnd);
        var oldFont = User32.SelectObject(hdc, _editFont);
        User32.GetTextMetricsW(hdc, out var tm);
        User32.SelectObject(hdc, oldFont);
        User32.ReleaseDC(_editHwnd, hdc);
        User32.SetWindowPos(_editHwnd, nint.Zero, rect.X, rect.Y + (rect.Height - _editFontHeight) / 2 + 1, rect.Width, _editFontHeight, User32.SWP_SHOWWINDOW);
        User32.SendMessageW(_editHwnd, User32.EM_SETMARGINS, User32.EC_LEFTMARGIN | User32.EC_RIGHTMARGIN, 0);
    }

    private static uint ColorToColorRef(Color32 c) => (uint)(c.R | (c.G << 8) | (c.B << 16));

    private static (string baseName, int weight) ExtractFontWeight(string fontFamily)
    {
        // Common font weight suffixes and their Windows font weight values
        var weightSuffixes = new (string suffix, int weight)[]
        {
            (" Thin", 100),
            (" ExtraLight", 200),
            (" UltraLight", 200),
            (" Light", 300),
            (" Regular", 400),
            (" Medium", 500),
            (" SemiBold", 600),
            (" Semibold", 600),
            (" DemiBold", 600),
            (" Bold", 700),
            (" ExtraBold", 800),
            (" UltraBold", 800),
            (" Black", 900),
            (" Heavy", 900),
        };

        foreach (var (suffix, weight) in weightSuffixes)
            if (fontFamily.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                return (fontFamily[..^suffix.Length], weight);

        return (fontFamily, 0);
    }

    private static InputCode VirtualKeyToInputCode(int vk)
    {
        return vk switch
        {
            0x08 => InputCode.KeyBackspace,
            0x09 => InputCode.KeyTab,
            0x0D => InputCode.KeyEnter,
            0x10 => InputCode.KeyLeftShift,
            0x11 => InputCode.KeyLeftCtrl,
            0x12 => InputCode.KeyLeftAlt,
            0x1B => InputCode.KeyEscape,
            0x20 => InputCode.KeySpace,
            0x21 => InputCode.KeyPageUp,
            0x22 => InputCode.KeyPageDown,
            0x23 => InputCode.KeyEnd,
            0x24 => InputCode.KeyHome,
            0x25 => InputCode.KeyLeft,
            0x26 => InputCode.KeyUp,
            0x27 => InputCode.KeyRight,
            0x28 => InputCode.KeyDown,
            0x2D => InputCode.KeyInsert,
            0x2E => InputCode.KeyDelete,
            >= 0x30 and <= 0x39 => InputCode.Key0 + (vk - 0x30),
            >= 0x41 and <= 0x5A => InputCode.KeyA + (vk - 0x41),
            0x5B => InputCode.KeyLeftSuper,
            0x5C => InputCode.KeyRightSuper,
            >= 0x70 and <= 0x7B => InputCode.KeyF1 + (vk - 0x70),
            0xA0 => InputCode.KeyLeftShift,
            0xA1 => InputCode.KeyRightShift,
            0xA2 => InputCode.KeyLeftCtrl,
            0xA3 => InputCode.KeyRightCtrl,
            0xA4 => InputCode.KeyLeftAlt,
            0xA5 => InputCode.KeyRightAlt,
            0xBA => InputCode.KeySemicolon,
            0xBB => InputCode.KeyEquals,
            0xBC => InputCode.KeyComma,
            0xBD => InputCode.KeyMinus,
            0xBE => InputCode.KeyPeriod,
            0xC0 => InputCode.KeyTilde,
            0xDB => InputCode.KeyLeftBracket,
            0xDD => InputCode.KeyRightBracket,
            0xDE => InputCode.KeyQuote,
            _ => InputCode.None
        };
    }
}
