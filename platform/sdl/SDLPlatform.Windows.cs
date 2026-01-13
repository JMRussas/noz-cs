//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.InteropServices;
using static SDL.SDL3;

namespace noz.Platform;

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
    public const int VK_RETURN = 0x0D;
    public const int VK_ESCAPE = 0x1B;
    public const int VK_TAB = 0x09;
    public const int FW_NORMAL = 400;
    public const int DEFAULT_CHARSET = 1;
    public const int OUT_DEFAULT_PRECIS = 0;
    public const int CLIP_DEFAULT_PRECIS = 0;
    public const int CLEARTYPE_QUALITY = 5;
    public const int DEFAULT_PITCH = 0;
    public const int FF_DONTCARE = 0;

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
    // Native textbox state (Windows-specific)
    private nint _editHwnd;
    private nint _editFont;
    private nint _editBgBrush;
    private bool _editVisible;
    private int _editFontSize = -1;
    private RectInt _editRect;
    private uint _editTextColor;
    private uint _editBgColor;
    private string _editLastText = "";

    public bool IsTextboxVisible => _editVisible;

    private void InitNativeTextInput()
    {
        if (!OperatingSystem.IsWindows()) return;

        var props = SDL_GetWindowProperties(_window);
        var parentHwnd = SDL_GetPointerProperty(props, SDL_PROP_WINDOW_WIN32_HWND_POINTER, nint.Zero);
        if (parentHwnd == nint.Zero) return;

        var hInstance = User32.GetModuleHandleW(null);

        _editHwnd = User32.CreateWindowExW(
            0,
            "EDIT",
            "",
            User32.WS_CHILD | User32.ES_AUTOHSCROLL | User32.ES_LEFT,
            0, 0, 100, 20,
            parentHwnd,
            nint.Zero,
            hInstance,
            nint.Zero
        );

        if (_editHwnd == nint.Zero) return;

        User32.SendMessageW(_editHwnd, User32.EM_SETMARGINS,
            User32.EC_LEFTMARGIN | User32.EC_RIGHTMARGIN, 0);
    }

    private void ShutdownNativeTextInput()
    {
        if (_editHwnd != nint.Zero)
        {
            User32.DestroyWindow(_editHwnd);
            _editHwnd = nint.Zero;
        }

        if (_editFont != nint.Zero)
        {
            User32.DeleteObject(_editFont);
            _editFont = nint.Zero;
        }

        if (_editBgBrush != nint.Zero)
        {
            User32.DeleteObject(_editBgBrush);
            _editBgBrush = nint.Zero;
        }
    }

    public void ShowTextbox(Rect rect, string text, NativeTextboxStyle style)
    {
        if (_editHwnd == nint.Zero) return;

        _editTextColor = ColorToColorRef(style.TextColor);
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

        UpdateTextboxRectInternal((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height, style.FontSize);

        User32.SetWindowTextW(_editHwnd, text ?? "");
        User32.SendMessageW(_editHwnd, User32.EM_SETSEL, 0, -1); // Select all

        User32.SetFocus(_editHwnd);
        _editVisible = true;
        _editLastText = text ?? "";
    }

    public void HideTextbox()
    {
        if (_editHwnd == nint.Zero) return;

        User32.ShowWindow(_editHwnd, User32.SW_HIDE);
        User32.InvalidateRect(_editHwnd, nint.Zero, true);
        User32.UpdateWindow(_editHwnd);
        _editVisible = false;
        _editRect = new RectInt(0, 0, 0, 0);
    }

    public void UpdateTextboxRect(Rect rect, int fontSize)
    {
        if (!_editVisible || _editHwnd == nint.Zero) return;
        UpdateTextboxRectInternal((int)rect.X, (int)rect.Y, (int)rect.Width, (int)rect.Height, fontSize);
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

    private void UpdateTextboxRectInternal(int x, int y, int width, int height, int fontSize)
    {
        UpdateEditFont(fontSize);

        var newRect = new RectInt(x, y, width, height);
        if (_editRect != newRect)
        {
            _editRect = newRect;
            CenterTextbox(x, y, width, height);
            User32.InvalidateRect(_editHwnd, nint.Zero, true);
            User32.UpdateWindow(_editHwnd);
        }
    }

    private void UpdateEditFont(int fontSize)
    {
        if (_editFontSize == fontSize) return;

        if (_editFont != nint.Zero)
            User32.DeleteObject(_editFont);

        _editFontSize = fontSize;
        _editFont = User32.CreateFontW(
            -fontSize, 0, 0, 0, User32.FW_NORMAL,
            0, 0, 0, User32.DEFAULT_CHARSET,
            User32.OUT_DEFAULT_PRECIS, User32.CLIP_DEFAULT_PRECIS, User32.CLEARTYPE_QUALITY,
            User32.DEFAULT_PITCH | User32.FF_DONTCARE, "Segoe UI");

        User32.SendMessageW(_editHwnd, User32.WM_SETFONT, _editFont, 1);
    }

    private void CenterTextbox(int x, int y, int width, int height)
    {
        var hdc = User32.GetDC(_editHwnd);
        var oldFont = User32.SelectObject(hdc, _editFont);
        User32.GetTextMetricsW(hdc, out var tm);
        User32.SelectObject(hdc, oldFont);
        User32.ReleaseDC(_editHwnd, hdc);

        var fontHeight = tm.tmHeight;
        var yOffset = (height - fontHeight) / 2;
        User32.SetWindowPos(_editHwnd, nint.Zero, x, y + yOffset, width, fontHeight, User32.SWP_SHOWWINDOW);
        User32.SendMessageW(_editHwnd, User32.EM_SETMARGINS, User32.EC_LEFTMARGIN | User32.EC_RIGHTMARGIN, 0);
    }

    private static uint ColorToColorRef(Color32 c) => (uint)(c.R | (c.G << 8) | (c.B << 16));
}
