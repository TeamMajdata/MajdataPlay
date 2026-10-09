using MajdataPlay.Platform.Android.Runtime.View;

#nullable enable

namespace MajdataPlay.Platform.Android.IO
{
    /// <summary>Identifies Android key codes while preserving their public names and numeric values.</summary>
    public enum KeyCode
    {
        /// <summary>Identifies the <c>KEYCODE_UNKNOWN</c> Android key code.</summary>
        Unknown = KeyEvent.KeycodeUnknown,
        /// <summary>Identifies the <c>KEYCODE_SOFT_LEFT</c> Android key code.</summary>
        SoftLeft = KeyEvent.KeycodeSoftLeft,
        /// <summary>Identifies the <c>KEYCODE_SOFT_RIGHT</c> Android key code.</summary>
        SoftRight = KeyEvent.KeycodeSoftRight,
        /// <summary>Identifies the <c>KEYCODE_HOME</c> Android key code.</summary>
        Home = KeyEvent.KeycodeHome,
        /// <summary>Identifies the <c>KEYCODE_BACK</c> Android key code.</summary>
        Back = KeyEvent.KeycodeBack,
        /// <summary>Identifies the <c>KEYCODE_CALL</c> Android key code.</summary>
        Call = KeyEvent.KeycodeCall,
        /// <summary>Identifies the <c>KEYCODE_ENDCALL</c> Android key code.</summary>
        Endcall = KeyEvent.KeycodeEndcall,
        /// <summary>Identifies the <c>KEYCODE_0</c> Android key code.</summary>
        Num0 = KeyEvent.Keycode0,
        /// <summary>Identifies the <c>KEYCODE_1</c> Android key code.</summary>
        Num1 = KeyEvent.Keycode1,
        /// <summary>Identifies the <c>KEYCODE_2</c> Android key code.</summary>
        Num2 = KeyEvent.Keycode2,
        /// <summary>Identifies the <c>KEYCODE_3</c> Android key code.</summary>
        Num3 = KeyEvent.Keycode3,
        /// <summary>Identifies the <c>KEYCODE_4</c> Android key code.</summary>
        Num4 = KeyEvent.Keycode4,
        /// <summary>Identifies the <c>KEYCODE_5</c> Android key code.</summary>
        Num5 = KeyEvent.Keycode5,
        /// <summary>Identifies the <c>KEYCODE_6</c> Android key code.</summary>
        Num6 = KeyEvent.Keycode6,
        /// <summary>Identifies the <c>KEYCODE_7</c> Android key code.</summary>
        Num7 = KeyEvent.Keycode7,
        /// <summary>Identifies the <c>KEYCODE_8</c> Android key code.</summary>
        Num8 = KeyEvent.Keycode8,
        /// <summary>Identifies the <c>KEYCODE_9</c> Android key code.</summary>
        Num9 = KeyEvent.Keycode9,
        /// <summary>Identifies the <c>KEYCODE_STAR</c> Android key code.</summary>
        Star = KeyEvent.KeycodeStar,
        /// <summary>Identifies the <c>KEYCODE_POUND</c> Android key code.</summary>
        Pound = KeyEvent.KeycodePound,
        /// <summary>Identifies the <c>KEYCODE_DPAD_UP</c> Android key code.</summary>
        DpadUp = KeyEvent.KeycodeDpadUp,
        /// <summary>Identifies the <c>KEYCODE_DPAD_DOWN</c> Android key code.</summary>
        DpadDown = KeyEvent.KeycodeDpadDown,
        /// <summary>Identifies the <c>KEYCODE_DPAD_LEFT</c> Android key code.</summary>
        DpadLeft = KeyEvent.KeycodeDpadLeft,
        /// <summary>Identifies the <c>KEYCODE_DPAD_RIGHT</c> Android key code.</summary>
        DpadRight = KeyEvent.KeycodeDpadRight,
        /// <summary>Identifies the <c>KEYCODE_DPAD_CENTER</c> Android key code.</summary>
        DpadCenter = KeyEvent.KeycodeDpadCenter,
        /// <summary>Identifies the <c>KEYCODE_VOLUME_UP</c> Android key code.</summary>
        VolumeUp = KeyEvent.KeycodeVolumeUp,
        /// <summary>Identifies the <c>KEYCODE_VOLUME_DOWN</c> Android key code.</summary>
        VolumeDown = KeyEvent.KeycodeVolumeDown,
        /// <summary>Identifies the <c>KEYCODE_POWER</c> Android key code.</summary>
        Power = KeyEvent.KeycodePower,
        /// <summary>Identifies the <c>KEYCODE_CAMERA</c> Android key code.</summary>
        Camera = KeyEvent.KeycodeCamera,
        /// <summary>Identifies the <c>KEYCODE_CLEAR</c> Android key code.</summary>
        Clear = KeyEvent.KeycodeClear,
        /// <summary>Identifies the <c>KEYCODE_A</c> Android key code.</summary>
        A = KeyEvent.KeycodeA,
        /// <summary>Identifies the <c>KEYCODE_B</c> Android key code.</summary>
        B = KeyEvent.KeycodeB,
        /// <summary>Identifies the <c>KEYCODE_C</c> Android key code.</summary>
        C = KeyEvent.KeycodeC,
        /// <summary>Identifies the <c>KEYCODE_D</c> Android key code.</summary>
        D = KeyEvent.KeycodeD,
        /// <summary>Identifies the <c>KEYCODE_E</c> Android key code.</summary>
        E = KeyEvent.KeycodeE,
        /// <summary>Identifies the <c>KEYCODE_F</c> Android key code.</summary>
        F = KeyEvent.KeycodeF,
        /// <summary>Identifies the <c>KEYCODE_G</c> Android key code.</summary>
        G = KeyEvent.KeycodeG,
        /// <summary>Identifies the <c>KEYCODE_H</c> Android key code.</summary>
        H = KeyEvent.KeycodeH,
        /// <summary>Identifies the <c>KEYCODE_I</c> Android key code.</summary>
        I = KeyEvent.KeycodeI,
        /// <summary>Identifies the <c>KEYCODE_J</c> Android key code.</summary>
        J = KeyEvent.KeycodeJ,
        /// <summary>Identifies the <c>KEYCODE_K</c> Android key code.</summary>
        K = KeyEvent.KeycodeK,
        /// <summary>Identifies the <c>KEYCODE_L</c> Android key code.</summary>
        L = KeyEvent.KeycodeL,
        /// <summary>Identifies the <c>KEYCODE_M</c> Android key code.</summary>
        M = KeyEvent.KeycodeM,
        /// <summary>Identifies the <c>KEYCODE_N</c> Android key code.</summary>
        N = KeyEvent.KeycodeN,
        /// <summary>Identifies the <c>KEYCODE_O</c> Android key code.</summary>
        O = KeyEvent.KeycodeO,
        /// <summary>Identifies the <c>KEYCODE_P</c> Android key code.</summary>
        P = KeyEvent.KeycodeP,
        /// <summary>Identifies the <c>KEYCODE_Q</c> Android key code.</summary>
        Q = KeyEvent.KeycodeQ,
        /// <summary>Identifies the <c>KEYCODE_R</c> Android key code.</summary>
        R = KeyEvent.KeycodeR,
        /// <summary>Identifies the <c>KEYCODE_S</c> Android key code.</summary>
        S = KeyEvent.KeycodeS,
        /// <summary>Identifies the <c>KEYCODE_T</c> Android key code.</summary>
        T = KeyEvent.KeycodeT,
        /// <summary>Identifies the <c>KEYCODE_U</c> Android key code.</summary>
        U = KeyEvent.KeycodeU,
        /// <summary>Identifies the <c>KEYCODE_V</c> Android key code.</summary>
        V = KeyEvent.KeycodeV,
        /// <summary>Identifies the <c>KEYCODE_W</c> Android key code.</summary>
        W = KeyEvent.KeycodeW,
        /// <summary>Identifies the <c>KEYCODE_X</c> Android key code.</summary>
        X = KeyEvent.KeycodeX,
        /// <summary>Identifies the <c>KEYCODE_Y</c> Android key code.</summary>
        Y = KeyEvent.KeycodeY,
        /// <summary>Identifies the <c>KEYCODE_Z</c> Android key code.</summary>
        Z = KeyEvent.KeycodeZ,
        /// <summary>Identifies the <c>KEYCODE_COMMA</c> Android key code.</summary>
        Comma = KeyEvent.KeycodeComma,
        /// <summary>Identifies the <c>KEYCODE_PERIOD</c> Android key code.</summary>
        Period = KeyEvent.KeycodePeriod,
        /// <summary>Identifies the <c>KEYCODE_ALT_LEFT</c> Android key code.</summary>
        AltLeft = KeyEvent.KeycodeAltLeft,
        /// <summary>Identifies the <c>KEYCODE_ALT_RIGHT</c> Android key code.</summary>
        AltRight = KeyEvent.KeycodeAltRight,
        /// <summary>Identifies the <c>KEYCODE_SHIFT_LEFT</c> Android key code.</summary>
        ShiftLeft = KeyEvent.KeycodeShiftLeft,
        /// <summary>Identifies the <c>KEYCODE_SHIFT_RIGHT</c> Android key code.</summary>
        ShiftRight = KeyEvent.KeycodeShiftRight,
        /// <summary>Identifies the <c>KEYCODE_TAB</c> Android key code.</summary>
        Tab = KeyEvent.KeycodeTab,
        /// <summary>Identifies the <c>KEYCODE_SPACE</c> Android key code.</summary>
        Space = KeyEvent.KeycodeSpace,
        /// <summary>Identifies the <c>KEYCODE_SYM</c> Android key code.</summary>
        Sym = KeyEvent.KeycodeSym,
        /// <summary>Identifies the <c>KEYCODE_EXPLORER</c> Android key code.</summary>
        Explorer = KeyEvent.KeycodeExplorer,
        /// <summary>Identifies the <c>KEYCODE_ENVELOPE</c> Android key code.</summary>
        Envelope = KeyEvent.KeycodeEnvelope,
        /// <summary>Identifies the <c>KEYCODE_ENTER</c> Android key code.</summary>
        Enter = KeyEvent.KeycodeEnter,
        /// <summary>Identifies the <c>KEYCODE_DEL</c> Android key code.</summary>
        Del = KeyEvent.KeycodeDel,
        /// <summary>Identifies the <c>KEYCODE_GRAVE</c> Android key code.</summary>
        Grave = KeyEvent.KeycodeGrave,
        /// <summary>Identifies the <c>KEYCODE_MINUS</c> Android key code.</summary>
        Minus = KeyEvent.KeycodeMinus,
        /// <summary>Identifies the <c>KEYCODE_EQUALS</c> Android key code.</summary>
        Equals = KeyEvent.KeycodeEquals,
        /// <summary>Identifies the <c>KEYCODE_LEFT_BRACKET</c> Android key code.</summary>
        LeftBracket = KeyEvent.KeycodeLeftBracket,
        /// <summary>Identifies the <c>KEYCODE_RIGHT_BRACKET</c> Android key code.</summary>
        RightBracket = KeyEvent.KeycodeRightBracket,
        /// <summary>Identifies the <c>KEYCODE_BACKSLASH</c> Android key code.</summary>
        Backslash = KeyEvent.KeycodeBackslash,
        /// <summary>Identifies the <c>KEYCODE_SEMICOLON</c> Android key code.</summary>
        Semicolon = KeyEvent.KeycodeSemicolon,
        /// <summary>Identifies the <c>KEYCODE_APOSTROPHE</c> Android key code.</summary>
        Apostrophe = KeyEvent.KeycodeApostrophe,
        /// <summary>Identifies the <c>KEYCODE_SLASH</c> Android key code.</summary>
        Slash = KeyEvent.KeycodeSlash,
        /// <summary>Identifies the <c>KEYCODE_AT</c> Android key code.</summary>
        At = KeyEvent.KeycodeAt,
        /// <summary>Identifies the <c>KEYCODE_NUM</c> Android key code.</summary>
        Num = KeyEvent.KeycodeNum,
        /// <summary>Identifies the <c>KEYCODE_HEADSETHOOK</c> Android key code.</summary>
        Headsethook = KeyEvent.KeycodeHeadsethook,
        /// <summary>Identifies the <c>KEYCODE_FOCUS</c> Android key code.</summary>
        Focus = KeyEvent.KeycodeFocus,
        /// <summary>Identifies the <c>KEYCODE_PLUS</c> Android key code.</summary>
        Plus = KeyEvent.KeycodePlus,
        /// <summary>Identifies the <c>KEYCODE_MENU</c> Android key code.</summary>
        Menu = KeyEvent.KeycodeMenu,
        /// <summary>Identifies the <c>KEYCODE_NOTIFICATION</c> Android key code.</summary>
        Notification = KeyEvent.KeycodeNotification,
        /// <summary>Identifies the <c>KEYCODE_SEARCH</c> Android key code.</summary>
        Search = KeyEvent.KeycodeSearch,
        /// <summary>Identifies the <c>KEYCODE_MEDIA_PLAY_PAUSE</c> Android key code.</summary>
        MediaPlayPause = KeyEvent.KeycodeMediaPlayPause,
        /// <summary>Identifies the <c>KEYCODE_MEDIA_STOP</c> Android key code.</summary>
        MediaStop = KeyEvent.KeycodeMediaStop,
        /// <summary>Identifies the <c>KEYCODE_MEDIA_NEXT</c> Android key code.</summary>
        MediaNext = KeyEvent.KeycodeMediaNext,
        /// <summary>Identifies the <c>KEYCODE_MEDIA_PREVIOUS</c> Android key code.</summary>
        MediaPrevious = KeyEvent.KeycodeMediaPrevious,
        /// <summary>Identifies the <c>KEYCODE_MEDIA_REWIND</c> Android key code.</summary>
        MediaRewind = KeyEvent.KeycodeMediaRewind,
        /// <summary>Identifies the <c>KEYCODE_MEDIA_FAST_FORWARD</c> Android key code.</summary>
        MediaFastForward = KeyEvent.KeycodeMediaFastForward,
        /// <summary>Identifies the <c>KEYCODE_MUTE</c> Android key code.</summary>
        Mute = KeyEvent.KeycodeMute,
        /// <summary>Identifies the <c>KEYCODE_PAGE_UP</c> Android key code.</summary>
        PageUp = KeyEvent.KeycodePageUp,
        /// <summary>Identifies the <c>KEYCODE_PAGE_DOWN</c> Android key code.</summary>
        PageDown = KeyEvent.KeycodePageDown,
        /// <summary>Identifies the <c>KEYCODE_PICTSYMBOLS</c> Android key code.</summary>
        Pictsymbols = KeyEvent.KeycodePictsymbols,
        /// <summary>Identifies the <c>KEYCODE_SWITCH_CHARSET</c> Android key code.</summary>
        SwitchCharset = KeyEvent.KeycodeSwitchCharset,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_A</c> Android key code.</summary>
        ButtonA = KeyEvent.KeycodeButtonA,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_B</c> Android key code.</summary>
        ButtonB = KeyEvent.KeycodeButtonB,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_C</c> Android key code.</summary>
        ButtonC = KeyEvent.KeycodeButtonC,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_X</c> Android key code.</summary>
        ButtonX = KeyEvent.KeycodeButtonX,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_Y</c> Android key code.</summary>
        ButtonY = KeyEvent.KeycodeButtonY,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_Z</c> Android key code.</summary>
        ButtonZ = KeyEvent.KeycodeButtonZ,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_L1</c> Android key code.</summary>
        ButtonL1 = KeyEvent.KeycodeButtonL1,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_R1</c> Android key code.</summary>
        ButtonR1 = KeyEvent.KeycodeButtonR1,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_L2</c> Android key code.</summary>
        ButtonL2 = KeyEvent.KeycodeButtonL2,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_R2</c> Android key code.</summary>
        ButtonR2 = KeyEvent.KeycodeButtonR2,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_THUMBL</c> Android key code.</summary>
        ButtonThumbl = KeyEvent.KeycodeButtonThumbl,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_THUMBR</c> Android key code.</summary>
        ButtonThumbr = KeyEvent.KeycodeButtonThumbr,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_START</c> Android key code.</summary>
        ButtonStart = KeyEvent.KeycodeButtonStart,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_SELECT</c> Android key code.</summary>
        ButtonSelect = KeyEvent.KeycodeButtonSelect,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_MODE</c> Android key code.</summary>
        ButtonMode = KeyEvent.KeycodeButtonMode,
        /// <summary>Identifies the <c>KEYCODE_ESCAPE</c> Android key code.</summary>
        Escape = KeyEvent.KeycodeEscape,
        /// <summary>Identifies the <c>KEYCODE_FORWARD_DEL</c> Android key code.</summary>
        ForwardDel = KeyEvent.KeycodeForwardDel,
        /// <summary>Identifies the <c>KEYCODE_CTRL_LEFT</c> Android key code.</summary>
        CtrlLeft = KeyEvent.KeycodeCtrlLeft,
        /// <summary>Identifies the <c>KEYCODE_CTRL_RIGHT</c> Android key code.</summary>
        CtrlRight = KeyEvent.KeycodeCtrlRight,
        /// <summary>Identifies the <c>KEYCODE_CAPS_LOCK</c> Android key code.</summary>
        CapsLock = KeyEvent.KeycodeCapsLock,
        /// <summary>Identifies the <c>KEYCODE_SCROLL_LOCK</c> Android key code.</summary>
        ScrollLock = KeyEvent.KeycodeScrollLock,
        /// <summary>Identifies the <c>KEYCODE_META_LEFT</c> Android key code.</summary>
        MetaLeft = KeyEvent.KeycodeMetaLeft,
        /// <summary>Identifies the <c>KEYCODE_META_RIGHT</c> Android key code.</summary>
        MetaRight = KeyEvent.KeycodeMetaRight,
        /// <summary>Identifies the <c>KEYCODE_FUNCTION</c> Android key code.</summary>
        Function = KeyEvent.KeycodeFunction,
        /// <summary>Identifies the <c>KEYCODE_SYSRQ</c> Android key code.</summary>
        Sysrq = KeyEvent.KeycodeSysrq,
        /// <summary>Identifies the <c>KEYCODE_BREAK</c> Android key code.</summary>
        Break = KeyEvent.KeycodeBreak,
        /// <summary>Identifies the <c>KEYCODE_MOVE_HOME</c> Android key code.</summary>
        MoveHome = KeyEvent.KeycodeMoveHome,
        /// <summary>Identifies the <c>KEYCODE_MOVE_END</c> Android key code.</summary>
        MoveEnd = KeyEvent.KeycodeMoveEnd,
        /// <summary>Identifies the <c>KEYCODE_INSERT</c> Android key code.</summary>
        Insert = KeyEvent.KeycodeInsert,
        /// <summary>Identifies the <c>KEYCODE_FORWARD</c> Android key code.</summary>
        Forward = KeyEvent.KeycodeForward,
        /// <summary>Identifies the <c>KEYCODE_MEDIA_PLAY</c> Android key code.</summary>
        MediaPlay = KeyEvent.KeycodeMediaPlay,
        /// <summary>Identifies the <c>KEYCODE_MEDIA_PAUSE</c> Android key code.</summary>
        MediaPause = KeyEvent.KeycodeMediaPause,
        /// <summary>Identifies the <c>KEYCODE_MEDIA_CLOSE</c> Android key code.</summary>
        MediaClose = KeyEvent.KeycodeMediaClose,
        /// <summary>Identifies the <c>KEYCODE_MEDIA_EJECT</c> Android key code.</summary>
        MediaEject = KeyEvent.KeycodeMediaEject,
        /// <summary>Identifies the <c>KEYCODE_MEDIA_RECORD</c> Android key code.</summary>
        MediaRecord = KeyEvent.KeycodeMediaRecord,
        /// <summary>Identifies the <c>KEYCODE_F1</c> Android key code.</summary>
        F1 = KeyEvent.KeycodeF1,
        /// <summary>Identifies the <c>KEYCODE_F2</c> Android key code.</summary>
        F2 = KeyEvent.KeycodeF2,
        /// <summary>Identifies the <c>KEYCODE_F3</c> Android key code.</summary>
        F3 = KeyEvent.KeycodeF3,
        /// <summary>Identifies the <c>KEYCODE_F4</c> Android key code.</summary>
        F4 = KeyEvent.KeycodeF4,
        /// <summary>Identifies the <c>KEYCODE_F5</c> Android key code.</summary>
        F5 = KeyEvent.KeycodeF5,
        /// <summary>Identifies the <c>KEYCODE_F6</c> Android key code.</summary>
        F6 = KeyEvent.KeycodeF6,
        /// <summary>Identifies the <c>KEYCODE_F7</c> Android key code.</summary>
        F7 = KeyEvent.KeycodeF7,
        /// <summary>Identifies the <c>KEYCODE_F8</c> Android key code.</summary>
        F8 = KeyEvent.KeycodeF8,
        /// <summary>Identifies the <c>KEYCODE_F9</c> Android key code.</summary>
        F9 = KeyEvent.KeycodeF9,
        /// <summary>Identifies the <c>KEYCODE_F10</c> Android key code.</summary>
        F10 = KeyEvent.KeycodeF10,
        /// <summary>Identifies the <c>KEYCODE_F11</c> Android key code.</summary>
        F11 = KeyEvent.KeycodeF11,
        /// <summary>Identifies the <c>KEYCODE_F12</c> Android key code.</summary>
        F12 = KeyEvent.KeycodeF12,
        /// <summary>Identifies the <c>KEYCODE_NUM_LOCK</c> Android key code.</summary>
        NumLock = KeyEvent.KeycodeNumLock,
        /// <summary>Identifies the <c>KEYCODE_NUMPAD_0</c> Android key code.</summary>
        Numpad0 = KeyEvent.KeycodeNumpad0,
        /// <summary>Identifies the <c>KEYCODE_NUMPAD_1</c> Android key code.</summary>
        Numpad1 = KeyEvent.KeycodeNumpad1,
        /// <summary>Identifies the <c>KEYCODE_NUMPAD_2</c> Android key code.</summary>
        Numpad2 = KeyEvent.KeycodeNumpad2,
        /// <summary>Identifies the <c>KEYCODE_NUMPAD_3</c> Android key code.</summary>
        Numpad3 = KeyEvent.KeycodeNumpad3,
        /// <summary>Identifies the <c>KEYCODE_NUMPAD_4</c> Android key code.</summary>
        Numpad4 = KeyEvent.KeycodeNumpad4,
        /// <summary>Identifies the <c>KEYCODE_NUMPAD_5</c> Android key code.</summary>
        Numpad5 = KeyEvent.KeycodeNumpad5,
        /// <summary>Identifies the <c>KEYCODE_NUMPAD_6</c> Android key code.</summary>
        Numpad6 = KeyEvent.KeycodeNumpad6,
        /// <summary>Identifies the <c>KEYCODE_NUMPAD_7</c> Android key code.</summary>
        Numpad7 = KeyEvent.KeycodeNumpad7,
        /// <summary>Identifies the <c>KEYCODE_NUMPAD_8</c> Android key code.</summary>
        Numpad8 = KeyEvent.KeycodeNumpad8,
        /// <summary>Identifies the <c>KEYCODE_NUMPAD_9</c> Android key code.</summary>
        Numpad9 = KeyEvent.KeycodeNumpad9,
        /// <summary>Identifies the <c>KEYCODE_NUMPAD_DIVIDE</c> Android key code.</summary>
        NumpadDivide = KeyEvent.KeycodeNumpadDivide,
        /// <summary>Identifies the <c>KEYCODE_NUMPAD_MULTIPLY</c> Android key code.</summary>
        NumpadMultiply = KeyEvent.KeycodeNumpadMultiply,
        /// <summary>Identifies the <c>KEYCODE_NUMPAD_SUBTRACT</c> Android key code.</summary>
        NumpadSubtract = KeyEvent.KeycodeNumpadSubtract,
        /// <summary>Identifies the <c>KEYCODE_NUMPAD_ADD</c> Android key code.</summary>
        NumpadAdd = KeyEvent.KeycodeNumpadAdd,
        /// <summary>Identifies the <c>KEYCODE_NUMPAD_DOT</c> Android key code.</summary>
        NumpadDot = KeyEvent.KeycodeNumpadDot,
        /// <summary>Identifies the <c>KEYCODE_NUMPAD_COMMA</c> Android key code.</summary>
        NumpadComma = KeyEvent.KeycodeNumpadComma,
        /// <summary>Identifies the <c>KEYCODE_NUMPAD_ENTER</c> Android key code.</summary>
        NumpadEnter = KeyEvent.KeycodeNumpadEnter,
        /// <summary>Identifies the <c>KEYCODE_NUMPAD_EQUALS</c> Android key code.</summary>
        NumpadEquals = KeyEvent.KeycodeNumpadEquals,
        /// <summary>Identifies the <c>KEYCODE_NUMPAD_LEFT_PAREN</c> Android key code.</summary>
        NumpadLeftParen = KeyEvent.KeycodeNumpadLeftParen,
        /// <summary>Identifies the <c>KEYCODE_NUMPAD_RIGHT_PAREN</c> Android key code.</summary>
        NumpadRightParen = KeyEvent.KeycodeNumpadRightParen,
        /// <summary>Identifies the <c>KEYCODE_VOLUME_MUTE</c> Android key code.</summary>
        VolumeMute = KeyEvent.KeycodeVolumeMute,
        /// <summary>Identifies the <c>KEYCODE_INFO</c> Android key code.</summary>
        Info = KeyEvent.KeycodeInfo,
        /// <summary>Identifies the <c>KEYCODE_CHANNEL_UP</c> Android key code.</summary>
        ChannelUp = KeyEvent.KeycodeChannelUp,
        /// <summary>Identifies the <c>KEYCODE_CHANNEL_DOWN</c> Android key code.</summary>
        ChannelDown = KeyEvent.KeycodeChannelDown,
        /// <summary>Identifies the <c>KEYCODE_ZOOM_IN</c> Android key code.</summary>
        ZoomIn = KeyEvent.KeycodeZoomIn,
        /// <summary>Identifies the <c>KEYCODE_ZOOM_OUT</c> Android key code.</summary>
        ZoomOut = KeyEvent.KeycodeZoomOut,
        /// <summary>Identifies the <c>KEYCODE_TV</c> Android key code.</summary>
        Tv = KeyEvent.KeycodeTv,
        /// <summary>Identifies the <c>KEYCODE_WINDOW</c> Android key code.</summary>
        Window = KeyEvent.KeycodeWindow,
        /// <summary>Identifies the <c>KEYCODE_GUIDE</c> Android key code.</summary>
        Guide = KeyEvent.KeycodeGuide,
        /// <summary>Identifies the <c>KEYCODE_DVR</c> Android key code.</summary>
        Dvr = KeyEvent.KeycodeDvr,
        /// <summary>Identifies the <c>KEYCODE_BOOKMARK</c> Android key code.</summary>
        Bookmark = KeyEvent.KeycodeBookmark,
        /// <summary>Identifies the <c>KEYCODE_CAPTIONS</c> Android key code.</summary>
        Captions = KeyEvent.KeycodeCaptions,
        /// <summary>Identifies the <c>KEYCODE_SETTINGS</c> Android key code.</summary>
        Settings = KeyEvent.KeycodeSettings,
        /// <summary>Identifies the <c>KEYCODE_TV_POWER</c> Android key code.</summary>
        TvPower = KeyEvent.KeycodeTvPower,
        /// <summary>Identifies the <c>KEYCODE_TV_INPUT</c> Android key code.</summary>
        TvInput = KeyEvent.KeycodeTvInput,
        /// <summary>Identifies the <c>KEYCODE_STB_POWER</c> Android key code.</summary>
        StbPower = KeyEvent.KeycodeStbPower,
        /// <summary>Identifies the <c>KEYCODE_STB_INPUT</c> Android key code.</summary>
        StbInput = KeyEvent.KeycodeStbInput,
        /// <summary>Identifies the <c>KEYCODE_AVR_POWER</c> Android key code.</summary>
        AvrPower = KeyEvent.KeycodeAvrPower,
        /// <summary>Identifies the <c>KEYCODE_AVR_INPUT</c> Android key code.</summary>
        AvrInput = KeyEvent.KeycodeAvrInput,
        /// <summary>Identifies the <c>KEYCODE_PROG_RED</c> Android key code.</summary>
        ProgRed = KeyEvent.KeycodeProgRed,
        /// <summary>Identifies the <c>KEYCODE_PROG_GREEN</c> Android key code.</summary>
        ProgGreen = KeyEvent.KeycodeProgGreen,
        /// <summary>Identifies the <c>KEYCODE_PROG_YELLOW</c> Android key code.</summary>
        ProgYellow = KeyEvent.KeycodeProgYellow,
        /// <summary>Identifies the <c>KEYCODE_PROG_BLUE</c> Android key code.</summary>
        ProgBlue = KeyEvent.KeycodeProgBlue,
        /// <summary>Identifies the <c>KEYCODE_APP_SWITCH</c> Android key code.</summary>
        AppSwitch = KeyEvent.KeycodeAppSwitch,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_1</c> Android key code.</summary>
        Button1 = KeyEvent.KeycodeButton1,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_2</c> Android key code.</summary>
        Button2 = KeyEvent.KeycodeButton2,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_3</c> Android key code.</summary>
        Button3 = KeyEvent.KeycodeButton3,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_4</c> Android key code.</summary>
        Button4 = KeyEvent.KeycodeButton4,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_5</c> Android key code.</summary>
        Button5 = KeyEvent.KeycodeButton5,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_6</c> Android key code.</summary>
        Button6 = KeyEvent.KeycodeButton6,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_7</c> Android key code.</summary>
        Button7 = KeyEvent.KeycodeButton7,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_8</c> Android key code.</summary>
        Button8 = KeyEvent.KeycodeButton8,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_9</c> Android key code.</summary>
        Button9 = KeyEvent.KeycodeButton9,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_10</c> Android key code.</summary>
        Button10 = KeyEvent.KeycodeButton10,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_11</c> Android key code.</summary>
        Button11 = KeyEvent.KeycodeButton11,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_12</c> Android key code.</summary>
        Button12 = KeyEvent.KeycodeButton12,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_13</c> Android key code.</summary>
        Button13 = KeyEvent.KeycodeButton13,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_14</c> Android key code.</summary>
        Button14 = KeyEvent.KeycodeButton14,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_15</c> Android key code.</summary>
        Button15 = KeyEvent.KeycodeButton15,
        /// <summary>Identifies the <c>KEYCODE_BUTTON_16</c> Android key code.</summary>
        Button16 = KeyEvent.KeycodeButton16,
        /// <summary>Identifies the <c>KEYCODE_LANGUAGE_SWITCH</c> Android key code.</summary>
        LanguageSwitch = KeyEvent.KeycodeLanguageSwitch,
        /// <summary>Identifies the <c>KEYCODE_MANNER_MODE</c> Android key code.</summary>
        MannerMode = KeyEvent.KeycodeMannerMode,
        /// <summary>Identifies the <c>KEYCODE_3D_MODE</c> Android key code.</summary>
        Key3DMode = KeyEvent.Keycode3dMode,
        /// <summary>Identifies the <c>KEYCODE_CONTACTS</c> Android key code.</summary>
        Contacts = KeyEvent.KeycodeContacts,
        /// <summary>Identifies the <c>KEYCODE_CALENDAR</c> Android key code.</summary>
        Calendar = KeyEvent.KeycodeCalendar,
        /// <summary>Identifies the <c>KEYCODE_MUSIC</c> Android key code.</summary>
        Music = KeyEvent.KeycodeMusic,
        /// <summary>Identifies the <c>KEYCODE_CALCULATOR</c> Android key code.</summary>
        Calculator = KeyEvent.KeycodeCalculator,
        /// <summary>Identifies the <c>KEYCODE_ZENKAKU_HANKAKU</c> Android key code.</summary>
        ZenkakuHankaku = KeyEvent.KeycodeZenkakuHankaku,
        /// <summary>Identifies the <c>KEYCODE_EISU</c> Android key code.</summary>
        Eisu = KeyEvent.KeycodeEisu,
        /// <summary>Identifies the <c>KEYCODE_MUHENKAN</c> Android key code.</summary>
        Muhenkan = KeyEvent.KeycodeMuhenkan,
        /// <summary>Identifies the <c>KEYCODE_HENKAN</c> Android key code.</summary>
        Henkan = KeyEvent.KeycodeHenkan,
        /// <summary>Identifies the <c>KEYCODE_KATAKANA_HIRAGANA</c> Android key code.</summary>
        KatakanaHiragana = KeyEvent.KeycodeKatakanaHiragana,
        /// <summary>Identifies the <c>KEYCODE_YEN</c> Android key code.</summary>
        Yen = KeyEvent.KeycodeYen,
        /// <summary>Identifies the <c>KEYCODE_RO</c> Android key code.</summary>
        Ro = KeyEvent.KeycodeRo,
        /// <summary>Identifies the <c>KEYCODE_KANA</c> Android key code.</summary>
        Kana = KeyEvent.KeycodeKana,
        /// <summary>Identifies the <c>KEYCODE_ASSIST</c> Android key code.</summary>
        Assist = KeyEvent.KeycodeAssist,
        /// <summary>Identifies the <c>KEYCODE_BRIGHTNESS_DOWN</c> Android key code.</summary>
        BrightnessDown = KeyEvent.KeycodeBrightnessDown,
        /// <summary>Identifies the <c>KEYCODE_BRIGHTNESS_UP</c> Android key code.</summary>
        BrightnessUp = KeyEvent.KeycodeBrightnessUp,
        /// <summary>Identifies the <c>KEYCODE_MEDIA_AUDIO_TRACK</c> Android key code.</summary>
        MediaAudioTrack = KeyEvent.KeycodeMediaAudioTrack,
        /// <summary>Identifies the <c>KEYCODE_SLEEP</c> Android key code.</summary>
        Sleep = KeyEvent.KeycodeSleep,
        /// <summary>Identifies the <c>KEYCODE_WAKEUP</c> Android key code.</summary>
        Wakeup = KeyEvent.KeycodeWakeup,
        /// <summary>Identifies the <c>KEYCODE_PAIRING</c> Android key code.</summary>
        Pairing = KeyEvent.KeycodePairing,
        /// <summary>Identifies the <c>KEYCODE_MEDIA_TOP_MENU</c> Android key code.</summary>
        MediaTopMenu = KeyEvent.KeycodeMediaTopMenu,
        /// <summary>Identifies the <c>KEYCODE_11</c> Android key code.</summary>
        Num11 = KeyEvent.Keycode11,
        /// <summary>Identifies the <c>KEYCODE_12</c> Android key code.</summary>
        Num12 = KeyEvent.Keycode12,
        /// <summary>Identifies the <c>KEYCODE_LAST_CHANNEL</c> Android key code.</summary>
        LastChannel = KeyEvent.KeycodeLastChannel,
        /// <summary>Identifies the <c>KEYCODE_TV_DATA_SERVICE</c> Android key code.</summary>
        TvDataService = KeyEvent.KeycodeTvDataService,
        /// <summary>Identifies the <c>KEYCODE_VOICE_ASSIST</c> Android key code.</summary>
        VoiceAssist = KeyEvent.KeycodeVoiceAssist,
        /// <summary>Identifies the <c>KEYCODE_TV_RADIO_SERVICE</c> Android key code.</summary>
        TvRadioService = KeyEvent.KeycodeTvRadioService,
        /// <summary>Identifies the <c>KEYCODE_TV_TELETEXT</c> Android key code.</summary>
        TvTeletext = KeyEvent.KeycodeTvTeletext,
        /// <summary>Identifies the <c>KEYCODE_TV_NUMBER_ENTRY</c> Android key code.</summary>
        TvNumberEntry = KeyEvent.KeycodeTvNumberEntry,
        /// <summary>Identifies the <c>KEYCODE_TV_TERRESTRIAL_ANALOG</c> Android key code.</summary>
        TvTerrestrialAnalog = KeyEvent.KeycodeTvTerrestrialAnalog,
        /// <summary>Identifies the <c>KEYCODE_TV_TERRESTRIAL_DIGITAL</c> Android key code.</summary>
        TvTerrestrialDigital = KeyEvent.KeycodeTvTerrestrialDigital,
        /// <summary>Identifies the <c>KEYCODE_TV_SATELLITE</c> Android key code.</summary>
        TvSatellite = KeyEvent.KeycodeTvSatellite,
        /// <summary>Identifies the <c>KEYCODE_TV_SATELLITE_BS</c> Android key code.</summary>
        TvSatelliteBs = KeyEvent.KeycodeTvSatelliteBs,
        /// <summary>Identifies the <c>KEYCODE_TV_SATELLITE_CS</c> Android key code.</summary>
        TvSatelliteCs = KeyEvent.KeycodeTvSatelliteCs,
        /// <summary>Identifies the <c>KEYCODE_TV_SATELLITE_SERVICE</c> Android key code.</summary>
        TvSatelliteService = KeyEvent.KeycodeTvSatelliteService,
        /// <summary>Identifies the <c>KEYCODE_TV_NETWORK</c> Android key code.</summary>
        TvNetwork = KeyEvent.KeycodeTvNetwork,
        /// <summary>Identifies the <c>KEYCODE_TV_ANTENNA_CABLE</c> Android key code.</summary>
        TvAntennaCable = KeyEvent.KeycodeTvAntennaCable,
        /// <summary>Identifies the <c>KEYCODE_TV_INPUT_HDMI_1</c> Android key code.</summary>
        TvInputHdmi1 = KeyEvent.KeycodeTvInputHdmi1,
        /// <summary>Identifies the <c>KEYCODE_TV_INPUT_HDMI_2</c> Android key code.</summary>
        TvInputHdmi2 = KeyEvent.KeycodeTvInputHdmi2,
        /// <summary>Identifies the <c>KEYCODE_TV_INPUT_HDMI_3</c> Android key code.</summary>
        TvInputHdmi3 = KeyEvent.KeycodeTvInputHdmi3,
        /// <summary>Identifies the <c>KEYCODE_TV_INPUT_HDMI_4</c> Android key code.</summary>
        TvInputHdmi4 = KeyEvent.KeycodeTvInputHdmi4,
        /// <summary>Identifies the <c>KEYCODE_TV_INPUT_COMPOSITE_1</c> Android key code.</summary>
        TvInputComposite1 = KeyEvent.KeycodeTvInputComposite1,
        /// <summary>Identifies the <c>KEYCODE_TV_INPUT_COMPOSITE_2</c> Android key code.</summary>
        TvInputComposite2 = KeyEvent.KeycodeTvInputComposite2,
        /// <summary>Identifies the <c>KEYCODE_TV_INPUT_COMPONENT_1</c> Android key code.</summary>
        TvInputComponent1 = KeyEvent.KeycodeTvInputComponent1,
        /// <summary>Identifies the <c>KEYCODE_TV_INPUT_COMPONENT_2</c> Android key code.</summary>
        TvInputComponent2 = KeyEvent.KeycodeTvInputComponent2,
        /// <summary>Identifies the <c>KEYCODE_TV_INPUT_VGA_1</c> Android key code.</summary>
        TvInputVga1 = KeyEvent.KeycodeTvInputVga1,
        /// <summary>Identifies the <c>KEYCODE_TV_AUDIO_DESCRIPTION</c> Android key code.</summary>
        TvAudioDescription = KeyEvent.KeycodeTvAudioDescription,
        /// <summary>Identifies the <c>KEYCODE_TV_AUDIO_DESCRIPTION_MIX_UP</c> Android key code.</summary>
        TvAudioDescriptionMixUp = KeyEvent.KeycodeTvAudioDescriptionMixUp,
        /// <summary>Identifies the <c>KEYCODE_TV_AUDIO_DESCRIPTION_MIX_DOWN</c> Android key code.</summary>
        TvAudioDescriptionMixDown = KeyEvent.KeycodeTvAudioDescriptionMixDown,
        /// <summary>Identifies the <c>KEYCODE_TV_ZOOM_MODE</c> Android key code.</summary>
        TvZoomMode = KeyEvent.KeycodeTvZoomMode,
        /// <summary>Identifies the <c>KEYCODE_TV_CONTENTS_MENU</c> Android key code.</summary>
        TvContentsMenu = KeyEvent.KeycodeTvContentsMenu,
        /// <summary>Identifies the <c>KEYCODE_TV_MEDIA_CONTEXT_MENU</c> Android key code.</summary>
        TvMediaContextMenu = KeyEvent.KeycodeTvMediaContextMenu,
        /// <summary>Identifies the <c>KEYCODE_TV_TIMER_PROGRAMMING</c> Android key code.</summary>
        TvTimerProgramming = KeyEvent.KeycodeTvTimerProgramming,
        /// <summary>Identifies the <c>KEYCODE_HELP</c> Android key code.</summary>
        Help = KeyEvent.KeycodeHelp,
        /// <summary>Identifies the <c>KEYCODE_NAVIGATE_PREVIOUS</c> Android key code.</summary>
        NavigatePrevious = KeyEvent.KeycodeNavigatePrevious,
        /// <summary>Identifies the <c>KEYCODE_NAVIGATE_NEXT</c> Android key code.</summary>
        NavigateNext = KeyEvent.KeycodeNavigateNext,
        /// <summary>Identifies the <c>KEYCODE_NAVIGATE_IN</c> Android key code.</summary>
        NavigateIn = KeyEvent.KeycodeNavigateIn,
        /// <summary>Identifies the <c>KEYCODE_NAVIGATE_OUT</c> Android key code.</summary>
        NavigateOut = KeyEvent.KeycodeNavigateOut,
        /// <summary>Identifies the <c>KEYCODE_STEM_PRIMARY</c> Android key code.</summary>
        StemPrimary = KeyEvent.KeycodeStemPrimary,
        /// <summary>Identifies the <c>KEYCODE_STEM_1</c> Android key code.</summary>
        Stem1 = KeyEvent.KeycodeStem1,
        /// <summary>Identifies the <c>KEYCODE_STEM_2</c> Android key code.</summary>
        Stem2 = KeyEvent.KeycodeStem2,
        /// <summary>Identifies the <c>KEYCODE_STEM_3</c> Android key code.</summary>
        Stem3 = KeyEvent.KeycodeStem3,
        /// <summary>Identifies the <c>KEYCODE_DPAD_UP_LEFT</c> Android key code.</summary>
        DpadUpLeft = KeyEvent.KeycodeDpadUpLeft,
        /// <summary>Identifies the <c>KEYCODE_DPAD_DOWN_LEFT</c> Android key code.</summary>
        DpadDownLeft = KeyEvent.KeycodeDpadDownLeft,
        /// <summary>Identifies the <c>KEYCODE_DPAD_UP_RIGHT</c> Android key code.</summary>
        DpadUpRight = KeyEvent.KeycodeDpadUpRight,
        /// <summary>Identifies the <c>KEYCODE_DPAD_DOWN_RIGHT</c> Android key code.</summary>
        DpadDownRight = KeyEvent.KeycodeDpadDownRight,
        /// <summary>Identifies the <c>KEYCODE_MEDIA_SKIP_FORWARD</c> Android key code.</summary>
        MediaSkipForward = KeyEvent.KeycodeMediaSkipForward,
        /// <summary>Identifies the <c>KEYCODE_MEDIA_SKIP_BACKWARD</c> Android key code.</summary>
        MediaSkipBackward = KeyEvent.KeycodeMediaSkipBackward,
        /// <summary>Identifies the <c>KEYCODE_MEDIA_STEP_FORWARD</c> Android key code.</summary>
        MediaStepForward = KeyEvent.KeycodeMediaStepForward,
        /// <summary>Identifies the <c>KEYCODE_MEDIA_STEP_BACKWARD</c> Android key code.</summary>
        MediaStepBackward = KeyEvent.KeycodeMediaStepBackward,
        /// <summary>Identifies the <c>KEYCODE_SOFT_SLEEP</c> Android key code.</summary>
        SoftSleep = KeyEvent.KeycodeSoftSleep,
        /// <summary>Identifies the <c>KEYCODE_CUT</c> Android key code.</summary>
        Cut = KeyEvent.KeycodeCut,
        /// <summary>Identifies the <c>KEYCODE_COPY</c> Android key code.</summary>
        Copy = KeyEvent.KeycodeCopy,
        /// <summary>Identifies the <c>KEYCODE_PASTE</c> Android key code.</summary>
        Paste = KeyEvent.KeycodePaste,
        /// <summary>Identifies the <c>KEYCODE_SYSTEM_NAVIGATION_UP</c> Android key code.</summary>
        SystemNavigationUp = KeyEvent.KeycodeSystemNavigationUp,
        /// <summary>Identifies the <c>KEYCODE_SYSTEM_NAVIGATION_DOWN</c> Android key code.</summary>
        SystemNavigationDown = KeyEvent.KeycodeSystemNavigationDown,
        /// <summary>Identifies the <c>KEYCODE_SYSTEM_NAVIGATION_LEFT</c> Android key code.</summary>
        SystemNavigationLeft = KeyEvent.KeycodeSystemNavigationLeft,
        /// <summary>Identifies the <c>KEYCODE_SYSTEM_NAVIGATION_RIGHT</c> Android key code.</summary>
        SystemNavigationRight = KeyEvent.KeycodeSystemNavigationRight,
        /// <summary>Identifies the <c>KEYCODE_ALL_APPS</c> Android key code.</summary>
        AllApps = KeyEvent.KeycodeAllApps,
        /// <summary>Identifies the <c>KEYCODE_REFRESH</c> Android key code.</summary>
        Refresh = KeyEvent.KeycodeRefresh,
        /// <summary>Identifies the <c>KEYCODE_THUMBS_UP</c> Android key code.</summary>
        ThumbsUp = KeyEvent.KeycodeThumbsUp,
        /// <summary>Identifies the <c>KEYCODE_THUMBS_DOWN</c> Android key code.</summary>
        ThumbsDown = KeyEvent.KeycodeThumbsDown,
        /// <summary>Identifies the <c>KEYCODE_PROFILE_SWITCH</c> Android key code.</summary>
        ProfileSwitch = KeyEvent.KeycodeProfileSwitch,
        /// <summary>Identifies the <c>KEYCODE_VIDEO_APP_1</c> Android key code.</summary>
        VideoApp1 = KeyEvent.KeycodeVideoApp1,
        /// <summary>Identifies the <c>KEYCODE_VIDEO_APP_2</c> Android key code.</summary>
        VideoApp2 = KeyEvent.KeycodeVideoApp2,
        /// <summary>Identifies the <c>KEYCODE_VIDEO_APP_3</c> Android key code.</summary>
        VideoApp3 = KeyEvent.KeycodeVideoApp3,
        /// <summary>Identifies the <c>KEYCODE_VIDEO_APP_4</c> Android key code.</summary>
        VideoApp4 = KeyEvent.KeycodeVideoApp4,
        /// <summary>Identifies the <c>KEYCODE_VIDEO_APP_5</c> Android key code.</summary>
        VideoApp5 = KeyEvent.KeycodeVideoApp5,
        /// <summary>Identifies the <c>KEYCODE_VIDEO_APP_6</c> Android key code.</summary>
        VideoApp6 = KeyEvent.KeycodeVideoApp6,
        /// <summary>Identifies the <c>KEYCODE_VIDEO_APP_7</c> Android key code.</summary>
        VideoApp7 = KeyEvent.KeycodeVideoApp7,
        /// <summary>Identifies the <c>KEYCODE_VIDEO_APP_8</c> Android key code.</summary>
        VideoApp8 = KeyEvent.KeycodeVideoApp8,
        /// <summary>Identifies the <c>KEYCODE_FEATURED_APP_1</c> Android key code.</summary>
        FeaturedApp1 = KeyEvent.KeycodeFeaturedApp1,
        /// <summary>Identifies the <c>KEYCODE_FEATURED_APP_2</c> Android key code.</summary>
        FeaturedApp2 = KeyEvent.KeycodeFeaturedApp2,
        /// <summary>Identifies the <c>KEYCODE_FEATURED_APP_3</c> Android key code.</summary>
        FeaturedApp3 = KeyEvent.KeycodeFeaturedApp3,
        /// <summary>Identifies the <c>KEYCODE_FEATURED_APP_4</c> Android key code.</summary>
        FeaturedApp4 = KeyEvent.KeycodeFeaturedApp4,
        /// <summary>Identifies the <c>KEYCODE_DEMO_APP_1</c> Android key code.</summary>
        DemoApp1 = KeyEvent.KeycodeDemoApp1,
        /// <summary>Identifies the <c>KEYCODE_DEMO_APP_2</c> Android key code.</summary>
        DemoApp2 = KeyEvent.KeycodeDemoApp2,
        /// <summary>Identifies the <c>KEYCODE_DEMO_APP_3</c> Android key code.</summary>
        DemoApp3 = KeyEvent.KeycodeDemoApp3,
        /// <summary>Identifies the <c>KEYCODE_DEMO_APP_4</c> Android key code.</summary>
        DemoApp4 = KeyEvent.KeycodeDemoApp4,
        /// <summary>Identifies the <c>KEYCODE_KEYBOARD_BACKLIGHT_DOWN</c> Android key code.</summary>
        KeyboardBacklightDown = KeyEvent.KeycodeKeyboardBacklightDown,
        /// <summary>Identifies the <c>KEYCODE_KEYBOARD_BACKLIGHT_UP</c> Android key code.</summary>
        KeyboardBacklightUp = KeyEvent.KeycodeKeyboardBacklightUp,
        /// <summary>Identifies the <c>KEYCODE_KEYBOARD_BACKLIGHT_TOGGLE</c> Android key code.</summary>
        KeyboardBacklightToggle = KeyEvent.KeycodeKeyboardBacklightToggle,
        /// <summary>Identifies the <c>KEYCODE_STYLUS_BUTTON_PRIMARY</c> Android key code.</summary>
        StylusButtonPrimary = KeyEvent.KeycodeStylusButtonPrimary,
        /// <summary>Identifies the <c>KEYCODE_STYLUS_BUTTON_SECONDARY</c> Android key code.</summary>
        StylusButtonSecondary = KeyEvent.KeycodeStylusButtonSecondary,
        /// <summary>Identifies the <c>KEYCODE_STYLUS_BUTTON_TERTIARY</c> Android key code.</summary>
        StylusButtonTertiary = KeyEvent.KeycodeStylusButtonTertiary,
        /// <summary>Identifies the <c>KEYCODE_STYLUS_BUTTON_TAIL</c> Android key code.</summary>
        StylusButtonTail = KeyEvent.KeycodeStylusButtonTail,
        /// <summary>Identifies the <c>KEYCODE_RECENT_APPS</c> Android key code.</summary>
        RecentApps = KeyEvent.KeycodeRecentApps,
        /// <summary>Identifies the <c>KEYCODE_MACRO_1</c> Android key code.</summary>
        Macro1 = KeyEvent.KeycodeMacro1,
        /// <summary>Identifies the <c>KEYCODE_MACRO_2</c> Android key code.</summary>
        Macro2 = KeyEvent.KeycodeMacro2,
        /// <summary>Identifies the <c>KEYCODE_MACRO_3</c> Android key code.</summary>
        Macro3 = KeyEvent.KeycodeMacro3,
        /// <summary>Identifies the <c>KEYCODE_MACRO_4</c> Android key code.</summary>
        Macro4 = KeyEvent.KeycodeMacro4,
        /// <summary>Identifies the <c>KEYCODE_EMOJI_PICKER</c> Android key code.</summary>
        EmojiPicker = KeyEvent.KeycodeEmojiPicker,
        /// <summary>Identifies the <c>KEYCODE_SCREENSHOT</c> Android key code.</summary>
        Screenshot = KeyEvent.KeycodeScreenshot,
        /// <summary>Identifies the <c>KEYCODE_DICTATE</c> Android key code.</summary>
        Dictate = KeyEvent.KeycodeDictate,
        /// <summary>Identifies the <c>KEYCODE_NEW</c> Android key code.</summary>
        New = KeyEvent.KeycodeNew,
        /// <summary>Identifies the <c>KEYCODE_CLOSE</c> Android key code.</summary>
        Close = KeyEvent.KeycodeClose,
        /// <summary>Identifies the <c>KEYCODE_DO_NOT_DISTURB</c> Android key code.</summary>
        DoNotDisturb = KeyEvent.KeycodeDoNotDisturb,
        /// <summary>Identifies the <c>KEYCODE_PRINT</c> Android key code.</summary>
        Print = KeyEvent.KeycodePrint,
        /// <summary>Identifies the <c>KEYCODE_LOCK</c> Android key code.</summary>
        Lock = KeyEvent.KeycodeLock,
        /// <summary>Identifies the <c>KEYCODE_FULLSCREEN</c> Android key code.</summary>
        Fullscreen = KeyEvent.KeycodeFullscreen,
        /// <summary>Identifies the <c>KEYCODE_F13</c> Android key code.</summary>
        F13 = KeyEvent.KeycodeF13,
        /// <summary>Identifies the <c>KEYCODE_F14</c> Android key code.</summary>
        F14 = KeyEvent.KeycodeF14,
        /// <summary>Identifies the <c>KEYCODE_F15</c> Android key code.</summary>
        F15 = KeyEvent.KeycodeF15,
        /// <summary>Identifies the <c>KEYCODE_F16</c> Android key code.</summary>
        F16 = KeyEvent.KeycodeF16,
        /// <summary>Identifies the <c>KEYCODE_F17</c> Android key code.</summary>
        F17 = KeyEvent.KeycodeF17,
        /// <summary>Identifies the <c>KEYCODE_F18</c> Android key code.</summary>
        F18 = KeyEvent.KeycodeF18,
        /// <summary>Identifies the <c>KEYCODE_F19</c> Android key code.</summary>
        F19 = KeyEvent.KeycodeF19,
        /// <summary>Identifies the <c>KEYCODE_F20</c> Android key code.</summary>
        F20 = KeyEvent.KeycodeF20,
        /// <summary>Identifies the <c>KEYCODE_F21</c> Android key code.</summary>
        F21 = KeyEvent.KeycodeF21,
        /// <summary>Identifies the <c>KEYCODE_F22</c> Android key code.</summary>
        F22 = KeyEvent.KeycodeF22,
        /// <summary>Identifies the <c>KEYCODE_F23</c> Android key code.</summary>
        F23 = KeyEvent.KeycodeF23,
        /// <summary>Identifies the <c>KEYCODE_F24</c> Android key code.</summary>
        F24 = KeyEvent.KeycodeF24,
    }
}
