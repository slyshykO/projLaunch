namespace Tauri

module rec Window =

    open Fable.Core
    open Fable.Core.JsInterop
    open Tauri.Dpi
    open Tauri.Event

    [<Erase>]
    type Exports =
        /// <summary>
        /// Get an instance of <c>Window</c> for the current window.
        /// </summary>
        [<Import("getCurrentWindow", "@tauri-apps/api/window")>]
        static member getCurrentWindow() : Window = nativeOnly

        /// <summary>
        /// Gets a list of instances of <c>Window</c> for all available windows.
        /// </summary>
        [<Import("getAllWindows", "@tauri-apps/api/window")>]
        static member getAllWindows() : JS.Promise<ResizeArray<Window>> = nativeOnly

        [<Import("mapMonitor", "@tauri-apps/api/window")>]
        static member mapMonitor(m: Monitor option) : Monitor option = nativeOnly

        /// <summary>
        /// Returns the monitor on which the window currently resides.
        /// Returns <c>null</c> if current monitor can't be detected.
        /// </summary>
        /// <example>
        /// <code lang="typescript">
        /// import { currentMonitor } from '@tauri-apps/api/window';
        /// const monitor = await currentMonitor();
        /// </code>
        /// </example>
        [<Import("currentMonitor", "@tauri-apps/api/window")>]
        static member currentMonitor() : JS.Promise<Monitor option> = nativeOnly

        /// <summary>
        /// Returns the primary monitor of the system.
        /// Returns <c>null</c> if it can't identify any monitor as a primary one.
        /// </summary>
        /// <example>
        /// <code lang="typescript">
        /// import { primaryMonitor } from '@tauri-apps/api/window';
        /// const monitor = await primaryMonitor();
        /// </code>
        /// </example>
        [<Import("primaryMonitor", "@tauri-apps/api/window")>]
        static member primaryMonitor() : JS.Promise<Monitor option> = nativeOnly

        /// <summary>
        /// Returns the monitor that contains the given point. Returns <c>null</c> if can't find any.
        /// </summary>
        /// <example>
        /// <code lang="typescript">
        /// import { monitorFromPoint } from '@tauri-apps/api/window';
        /// const monitor = await monitorFromPoint(100.0, 200.0);
        /// </code>
        /// </example>
        [<Import("monitorFromPoint", "@tauri-apps/api/window")>]
        static member monitorFromPoint(x: float, y: float) : JS.Promise<Monitor option> = nativeOnly

        /// <summary>
        /// Returns the list of all the monitors available on the system.
        /// </summary>
        /// <example>
        /// <code lang="typescript">
        /// import { availableMonitors } from '@tauri-apps/api/window';
        /// const monitors = await availableMonitors();
        /// </code>
        /// </example>
        [<Import("availableMonitors", "@tauri-apps/api/window")>]
        static member availableMonitors() : JS.Promise<ResizeArray<Monitor>> = nativeOnly

        /// <summary>
        /// Get the cursor position relative to the top-left hand corner of the desktop.
        ///
        /// Note that the top-left hand corner of the desktop is not necessarily the same as the screen.
        /// If the user uses a desktop with multiple monitors,
        /// the top-left hand corner of the desktop is the top-left hand corner of the main monitor on Windows and macOS
        /// or the top-left of the leftmost monitor on X11.
        ///
        /// The coordinates can be negative if the top-left hand corner of the window is outside of the visible screen region.
        /// </summary>
        [<Import("cursorPosition", "@tauri-apps/api/window")>]
        static member cursorPosition() : JS.Promise<PhysicalPosition> = nativeOnly

        [<Import("CloseRequestedEvent", "@tauri-apps/api/window"); EmitConstructor>]
        static member CloseRequestedEvent(event: Event<obj>) : CloseRequestedEvent = nativeOnly

        /// <summary>
        /// Creates a new Window.
        /// </summary>
        /// <example>
        /// <code lang="typescript">
        /// import { Window } from '@tauri-apps/api/window';
        /// const appWindow = new Window('my-label');
        /// appWindow.once('tauri://created', function () {
        ///  // window successfully created
        /// });
        /// appWindow.once('tauri://error', function (e) {
        ///  // an error happened creating the window
        /// });
        /// </code>
        /// </example>
        /// <param name="label">
        /// The unique window label. Must be alphanumeric: <c>a-zA-Z-/:_</c>.
        /// </param>
        /// <returns>
        /// The <see href="Window">Window</see> instance to communicate with the window.
        /// </returns>
        [<Import("Window", "@tauri-apps/api/window"); EmitConstructor>]
        static member Window(label: WindowLabel, options: WindowOptions) : Window = nativeOnly




    [<AllowNullLiteral>]
    [<Interface>]
    type Monitor =
        /// <summary>
        /// Human-readable name of the monitor
        /// </summary>
        abstract member name: string option with get, set
        /// <summary>
        /// The monitor's resolution.
        /// </summary>
        abstract member size: PhysicalSize with get, set
        /// <summary>
        /// the Top-left corner position of the monitor relative to the larger full screen area.
        /// </summary>
        abstract member position: PhysicalPosition with get, set
        /// <summary>
        /// The monitor's work area.
        /// </summary>
        abstract member workArea: Monitor.workArea with get, set
        /// <summary>
        /// The scale factor that can be used to map physical pixels to logical pixels.
        /// </summary>
        abstract member scaleFactor: float with get, set

    [<RequireQualifiedAccess>]
    [<StringEnum(CaseRules.None)>]
    type Theme =
        | light
        | dark

    [<RequireQualifiedAccess>]
    [<StringEnum(CaseRules.None)>]
    type TitleBarStyle =
        | visible
        | transparent
        | overlay

    [<RequireQualifiedAccess>]
    [<StringEnum(CaseRules.None)>]
    type ResizeDirection =
        | East
        | North
        | NorthEast
        | NorthWest
        | South
        | SouthEast
        | SouthWest
        | West

    [<AllowNullLiteral>]
    [<Interface>]
    type ScaleFactorChanged =
        /// <summary>
        /// The new window scale factor.
        /// </summary>
        abstract member scaleFactor: float with get, set
        /// <summary>
        /// The new window size
        /// </summary>
        abstract member size: PhysicalSize with get, set

    [<RequireQualifiedAccess>]
    type UserAttentionType =
        | Critical = 1
        | Informational = 2

    [<AllowNullLiteral>]
    [<Interface>]
    type CloseRequestedEvent =
        /// <summary>
        /// Event name
        /// </summary>
        abstract member event: EventName with get, set
        /// <summary>
        /// Event identifier used to unlisten
        /// </summary>
        abstract member id: float with get, set
        abstract member preventDefault: unit -> unit
        abstract member isPreventDefault: unit -> bool

    [<RequireQualifiedAccess>]
    [<StringEnum(CaseRules.None)>]
    type CursorIcon =
        | ``default``
        | crosshair
        | hand
        | arrow
        | move
        | text
        | wait
        | help
        | progress
        | notAllowed
        | contextMenu
        | cell
        | verticalText
        | alias
        | copy
        | noDrop
        | grab
        | grabbing
        | allScroll
        | zoomIn
        | zoomOut
        | eResize
        | nResize
        | neResize
        | nwResize
        | sResize
        | seResize
        | swResize
        | wResize
        | ewResize
        | nsResize
        | neswResize
        | nwseResize
        | colResize
        | rowResize

    [<RequireQualifiedAccess>]
    [<StringEnum(CaseRules.None)>]
    type ProgressBarStatus =
        | [<CompiledName("none")>] None
        | [<CompiledName("normal")>] Normal
        | [<CompiledName("indeterminate")>] Indeterminate
        | [<CompiledName("paused")>] Paused
        | [<CompiledName("error")>] Error

    [<AllowNullLiteral>]
    [<Interface>]
    type WindowSizeConstraints =
        abstract member minWidth: float option with get, set
        abstract member minHeight: float option with get, set
        abstract member maxWidth: float option with get, set
        abstract member maxHeight: float option with get, set

    [<AllowNullLiteral>]
    [<Interface>]
    type ProgressBarState =
        /// <summary>
        /// The progress bar status.
        /// </summary>
        abstract member status: ProgressBarStatus option with get, set
        /// <summary>
        /// The progress bar progress. This can be a value ranging from <c>0</c> to <c>100</c>
        /// </summary>
        abstract member progress: float option with get, set


    type WindowLabel = string

    [<AllowNullLiteral>]
    [<Interface>]
    type Window =
        /// <summary>
        /// The window label. It is a unique identifier for the window, can be used to reference it later.
        /// </summary>
        abstract member label: WindowLabel with get, set
        /// <summary>
        /// Local event listeners.
        /// </summary>
        abstract member listeners: Window.listeners with get, set

        static member inline getByLabel(label: string) : JS.Promise<Window option> =
            emitJsExpr
                (label)
                $$"""
        import { Window } from "@tauri-apps/api/window";
        Window.getByLabel($0)"""

        static member inline getCurrent() : Window =
            emitJsExpr
                ()
                $$"""
        import { Window } from "@tauri-apps/api/window";
        Window.getCurrent()"""

        static member inline getAll() : JS.Promise<ResizeArray<Window>> =
            emitJsExpr
                ()
                $$"""
        import { Window } from "@tauri-apps/api/window";
        Window.getAll()"""

        static member inline getFocusedWindow() : JS.Promise<Window option> =
            emitJsExpr
                ()
                $$"""
        import { Window } from "@tauri-apps/api/window";
        Window.getFocusedWindow()"""

        abstract member listen: event: EventName * handler: EventCallback<'T> -> JS.Promise<UnlistenFn>
        abstract member once: event: EventName * handler: EventCallback<'T> -> JS.Promise<UnlistenFn>
        abstract member emit: event: string * ?payload: 'T -> JS.Promise<unit>
        abstract member emitTo: target: U2<string, EventTarget> * event: string * ?payload: 'T -> JS.Promise<unit>
        abstract member _handleTauriEvent: event: string * handler: EventCallback<'T> -> bool
        abstract member scaleFactor: unit -> JS.Promise<float>
        abstract member innerPosition: unit -> JS.Promise<PhysicalPosition>
        abstract member outerPosition: unit -> JS.Promise<PhysicalPosition>
        abstract member innerSize: unit -> JS.Promise<PhysicalSize>
        abstract member outerSize: unit -> JS.Promise<PhysicalSize>
        abstract member isFullscreen: unit -> JS.Promise<bool>
        abstract member isMinimized: unit -> JS.Promise<bool>
        abstract member isMaximized: unit -> JS.Promise<bool>
        abstract member isFocused: unit -> JS.Promise<bool>
        abstract member isDecorated: unit -> JS.Promise<bool>
        abstract member isResizable: unit -> JS.Promise<bool>
        abstract member isMaximizable: unit -> JS.Promise<bool>
        abstract member isMinimizable: unit -> JS.Promise<bool>
        abstract member isClosable: unit -> JS.Promise<bool>
        abstract member isVisible: unit -> JS.Promise<bool>
        abstract member title: unit -> JS.Promise<string>
        abstract member theme: unit -> JS.Promise<Theme option>
        abstract member isAlwaysOnTop: unit -> JS.Promise<bool>
        abstract member center: unit -> JS.Promise<unit>
        abstract member requestUserAttention: requestType: UserAttentionType option -> JS.Promise<unit>
        abstract member setResizable: resizable: bool -> JS.Promise<unit>
        abstract member setEnabled: enabled: bool -> JS.Promise<unit>
        abstract member isEnabled: unit -> JS.Promise<bool>
        abstract member setMaximizable: maximizable: bool -> JS.Promise<unit>
        abstract member setMinimizable: minimizable: bool -> JS.Promise<unit>
        abstract member setClosable: closable: bool -> JS.Promise<unit>
        abstract member setTitle: title: string -> JS.Promise<unit>
        abstract member maximize: unit -> JS.Promise<unit>
        abstract member unmaximize: unit -> JS.Promise<unit>
        abstract member toggleMaximize: unit -> JS.Promise<unit>
        abstract member minimize: unit -> JS.Promise<unit>
        abstract member unminimize: unit -> JS.Promise<unit>
        abstract member show: unit -> JS.Promise<unit>
        abstract member hide: unit -> JS.Promise<unit>
        abstract member close: unit -> JS.Promise<unit>
        abstract member destroy: unit -> JS.Promise<unit>
        abstract member setDecorations: decorations: bool -> JS.Promise<unit>
        abstract member setShadow: enable: bool -> JS.Promise<unit>
        abstract member setEffects: effects: Effects -> JS.Promise<unit>
        abstract member clearEffects: unit -> JS.Promise<unit>
        abstract member setAlwaysOnTop: alwaysOnTop: bool -> JS.Promise<unit>
        abstract member setAlwaysOnBottom: alwaysOnBottom: bool -> JS.Promise<unit>
        abstract member setContentProtected: protected_: bool -> JS.Promise<unit>
        abstract member setSize: size: U3<LogicalSize, PhysicalSize, Size> -> JS.Promise<unit>
        abstract member setMinSize: size: U3<LogicalSize, PhysicalSize, Size> option -> JS.Promise<unit>
        abstract member setMaxSize: size: U3<LogicalSize, PhysicalSize, Size> option -> JS.Promise<unit>
        abstract member setSizeConstraints: constraints: WindowSizeConstraints option -> JS.Promise<unit>
        abstract member setPosition: position: U3<LogicalPosition, PhysicalPosition, Position> -> JS.Promise<unit>
        abstract member setFullscreen: fullscreen: bool -> JS.Promise<unit>
        abstract member setFocus: unit -> JS.Promise<unit>

        //abstract member setIcon: icon: U5<string, Image, JS.Uint8Array, ArrayBuffer, ResizeArray<float>> -> JS.Promise<unit>

        abstract member setSkipTaskbar: skip: bool -> JS.Promise<unit>
        abstract member setCursorGrab: grab: bool -> JS.Promise<unit>
        abstract member setCursorVisible: visible: bool -> JS.Promise<unit>
        abstract member setCursorIcon: icon: CursorIcon -> JS.Promise<unit>
        abstract member setBackgroundColor: color: Color -> JS.Promise<unit>
        abstract member setCursorPosition: position: U3<LogicalPosition, PhysicalPosition, Position> -> JS.Promise<unit>
        abstract member setIgnoreCursorEvents: ignore: bool -> JS.Promise<unit>
        abstract member startDragging: unit -> JS.Promise<unit>
        abstract member startResizeDragging: direction: ResizeDirection -> JS.Promise<unit>
        abstract member setBadgeCount: ?count: float -> JS.Promise<unit>
        abstract member setBadgeLabel: ?label: string -> JS.Promise<unit>

        //abstract member setOverlayIcon: ?icon: U5<string, Image, JS.Uint8Array, ArrayBuffer, ResizeArray<float>> -> JS.Promise<unit>

        abstract member setProgressBar: state: ProgressBarState -> JS.Promise<unit>
        abstract member setVisibleOnAllWorkspaces: visible: bool -> JS.Promise<unit>
        abstract member setTitleBarStyle: style: TitleBarStyle -> JS.Promise<unit>
        abstract member setTheme: ?theme: Theme -> JS.Promise<unit>
        abstract member onResized: handler: EventCallback<PhysicalSize> -> JS.Promise<UnlistenFn>
        abstract member onMoved: handler: EventCallback<PhysicalPosition> -> JS.Promise<UnlistenFn>

        abstract member onCloseRequested:
            handler: (CloseRequestedEvent -> U2<unit, JS.Promise<unit>>) -> JS.Promise<UnlistenFn>

        //abstract member onDragDropEvent: handler: EventCallback<DragDropEvent> -> JS.Promise<UnlistenFn>
        abstract member onFocusChanged: handler: EventCallback<bool> -> JS.Promise<UnlistenFn>
        abstract member onScaleChanged: handler: EventCallback<ScaleFactorChanged> -> JS.Promise<UnlistenFn>
        abstract member onThemeChanged: handler: EventCallback<Theme> -> JS.Promise<UnlistenFn>

    /// <summary>
    /// An RGBA color. Each value has minimum of 0 and maximum of 255.
    ///
    /// It can be either a string <c>#ffffff</c>, an array of 3 or 4 elements or an object.
    /// </summary>
    type Color = U4<float * float * float, float * float * float * float, Color.U4.Case3, string>

    [<RequireQualifiedAccess>]
    [<StringEnum(CaseRules.None)>]
    type BackgroundThrottlingPolicy =
        | [<CompiledName("disabled")>] Disabled
        | [<CompiledName("throttle")>] Throttle
        | [<CompiledName("suspend")>] Suspend

    [<RequireQualifiedAccess>]
    [<StringEnum(CaseRules.None)>]
    type Effect =
        | [<CompiledName("appearanceBased")>] AppearanceBased
        | [<CompiledName("light")>] Light
        | [<CompiledName("dark")>] Dark
        | [<CompiledName("mediumLight")>] MediumLight
        | [<CompiledName("ultraDark")>] UltraDark
        | [<CompiledName("titlebar")>] Titlebar
        | [<CompiledName("selection")>] Selection
        | [<CompiledName("menu")>] Menu
        | [<CompiledName("popover")>] Popover
        | [<CompiledName("sidebar")>] Sidebar
        | [<CompiledName("headerView")>] HeaderView
        | [<CompiledName("sheet")>] Sheet
        | [<CompiledName("windowBackground")>] WindowBackground
        | [<CompiledName("hudWindow")>] HudWindow
        | [<CompiledName("fullScreenUI")>] FullScreenUI
        | [<CompiledName("tooltip")>] Tooltip
        | [<CompiledName("contentBackground")>] ContentBackground
        | [<CompiledName("underWindowBackground")>] UnderWindowBackground
        | [<CompiledName("underPageBackground")>] UnderPageBackground
        | [<CompiledName("mica")>] Mica
        | [<CompiledName("blur")>] Blur
        | [<CompiledName("acrylic")>] Acrylic
        | [<CompiledName("tabbed")>] Tabbed
        | [<CompiledName("tabbedDark")>] TabbedDark
        | [<CompiledName("tabbedLight")>] TabbedLight

    [<RequireQualifiedAccess>]
    [<StringEnum(CaseRules.None)>]
    type EffectState =
        | [<CompiledName("followsWindowActiveState")>] FollowsWindowActiveState
        | [<CompiledName("active")>] Active
        | [<CompiledName("inactive")>] Inactive

    [<AllowNullLiteral>]
    [<Interface>]
    type Effects =
        /// <summary>
        /// List of Window effects to apply to the Window.
        /// Conflicting effects will apply the first one and ignore the rest.
        /// </summary>
        abstract member effects: ResizeArray<Effect> with get, set
        /// <summary>
        /// Window effect state **macOS Only**
        /// </summary>
        abstract member state: EffectState option with get, set
        /// <summary>
        /// Window effect corner radius **macOS Only**
        /// </summary>
        abstract member radius: float option with get, set
        /// <summary>
        /// Window effect color. Affects <see href="Effect.Blur">Effect.Blur</see> and <see href="Effect.Acrylic">Effect.Acrylic</see> only
        /// on Windows 10 v1903+. Doesn't have any effect on Windows 7 or Windows 11.
        /// </summary>
        abstract member color: Color option with get, set

    [<AllowNullLiteral>]
    [<Interface>]
    type PreventOverflowMargin =
        abstract member width: float with get, set
        abstract member height: float with get, set

    [<AllowNullLiteral>]
    [<Interface>]
    type WindowOptions =
        /// <summary>
        /// Show window in the center of the screen..
        /// </summary>
        abstract member center: bool option with get, set
        /// <summary>
        /// The initial vertical position. Only applies if <c>y</c> is also set.
        /// </summary>
        abstract member x: float option with get, set
        /// <summary>
        /// The initial horizontal position. Only applies if <c>x</c> is also set.
        /// </summary>
        abstract member y: float option with get, set
        /// <summary>
        /// The initial width.
        /// </summary>
        abstract member width: float option with get, set
        /// <summary>
        /// The initial height.
        /// </summary>
        abstract member height: float option with get, set
        /// <summary>
        /// The minimum width. Only applies if <c>minHeight</c> is also set.
        /// </summary>
        abstract member minWidth: float option with get, set
        /// <summary>
        /// The minimum height. Only applies if <c>minWidth</c> is also set.
        /// </summary>
        abstract member minHeight: float option with get, set
        /// <summary>
        /// The maximum width. Only applies if <c>maxHeight</c> is also set.
        /// </summary>
        abstract member maxWidth: float option with get, set
        /// <summary>
        /// The maximum height. Only applies if <c>maxWidth</c> is also set.
        /// </summary>
        abstract member maxHeight: float option with get, set
        /// <summary>
        /// Prevent the window from overflowing the working area (e.g. monitor size - taskbar size)
        /// on creation, which means the window size will be limited to <c>monitor size - taskbar size</c>
        ///
        /// Can either be set to <c>true</c> or to a <see href="PreventOverflowMargin">PreventOverflowMargin</see> object to set an additional margin
        /// that should be considered to determine the working area
        /// (in this case the window size will be limited to <c>monitor size - taskbar size - margin</c>)
        ///
        /// **NOTE**: The overflow check is only performed on window creation, resizes can still overflow
        ///
        /// #### Platform-specific
        ///
        /// - **iOS / Android:** Unsupported.
        /// </summary>
        abstract member preventOverflow: U2<bool, PreventOverflowMargin> option with get, set
        /// <summary>
        /// Whether the window is resizable or not.
        /// </summary>
        abstract member resizable: bool option with get, set
        /// <summary>
        /// Window title.
        /// </summary>
        abstract member title: string option with get, set
        /// <summary>
        /// Whether the window is in fullscreen mode or not.
        /// </summary>
        abstract member fullscreen: bool option with get, set
        /// <summary>
        /// Whether the window will be initially focused or not.
        /// </summary>
        abstract member focus: bool option with get, set
        /// <summary>
        /// Whether the window is transparent or not.
        /// Note that on <c>macOS</c> this requires the <c>macos-private-api</c> feature flag, enabled under <c>tauri.conf.json > app > macOSPrivateApi</c>.
        /// WARNING: Using private APIs on <c>macOS</c> prevents your application from being accepted to the <c>App Store</c>.
        /// </summary>
        abstract member transparent: bool option with get, set
        /// <summary>
        /// Whether the window should be maximized upon creation or not.
        /// </summary>
        abstract member maximized: bool option with get, set
        /// <summary>
        /// Whether the window should be immediately visible upon creation or not.
        /// </summary>
        abstract member visible: bool option with get, set
        /// <summary>
        /// Whether the window should have borders and bars or not.
        /// </summary>
        abstract member decorations: bool option with get, set
        /// <summary>
        /// Whether the window should always be on top of other windows or not.
        /// </summary>
        abstract member alwaysOnTop: bool option with get, set
        /// <summary>
        /// Whether the window should always be below other windows.
        /// </summary>
        abstract member alwaysOnBottom: bool option with get, set
        /// <summary>
        /// Prevents the window contents from being captured by other apps.
        /// </summary>
        abstract member contentProtected: bool option with get, set
        /// <summary>
        /// Whether or not the window icon should be added to the taskbar.
        /// </summary>
        abstract member skipTaskbar: bool option with get, set
        /// <summary>
        /// Whether or not the window has shadow.
        ///
        /// #### Platform-specific
        ///
        /// - **Windows:**
        ///  - <c>false</c> has no effect on decorated window, shadows are always ON.
        ///  - <c>true</c> will make undecorated window have a 1px white border,
        /// and on Windows 11, it will have a rounded corners.
        /// - **Linux:** Unsupported.
        /// </summary>
        abstract member shadow: bool option with get, set
        /// <summary>
        /// The initial window theme. Defaults to the system theme.
        ///
        /// Only implemented on Windows and macOS 10.14+.
        /// </summary>
        abstract member theme: Theme option with get, set
        /// <summary>
        /// The style of the macOS title bar.
        /// </summary>
        abstract member titleBarStyle: TitleBarStyle option with get, set
        /// <summary>
        /// If <c>true</c>, sets the window title to be hidden on macOS.
        /// </summary>
        abstract member hiddenTitle: bool option with get, set
        /// <summary>
        /// Defines the window [tabbing identifier](https://developer.apple.com/documentation/appkit/nswindow/1644704-tabbingidentifier) on macOS.
        ///
        /// Windows with the same tabbing identifier will be grouped together.
        /// If the tabbing identifier is not set, automatic tabbing will be disabled.
        /// </summary>
        abstract member tabbingIdentifier: string option with get, set
        /// <summary>
        /// Whether the window's native maximize button is enabled or not. Defaults to <c>true</c>.
        /// </summary>
        abstract member maximizable: bool option with get, set
        /// <summary>
        /// Whether the window's native minimize button is enabled or not. Defaults to <c>true</c>.
        /// </summary>
        abstract member minimizable: bool option with get, set
        /// <summary>
        /// Whether the window's native close button is enabled or not. Defaults to <c>true</c>.
        /// </summary>
        abstract member closable: bool option with get, set
        /// <summary>
        /// Sets a parent to the window to be created. Can be either a {@linkcode Window} or a label of the window.
        ///
        /// #### Platform-specific
        ///
        /// - **Windows**: This sets the passed parent as an owner window to the window to be created.
        ///   From [MSDN owned windows docs](https://docs.microsoft.com/en-us/windows/win32/winmsg/window-features#owned-windows):
        ///     - An owned window is always above its owner in the z-order.
        ///     - The system automatically destroys an owned window when its owner is destroyed.
        ///     - An owned window is hidden when its owner is minimized.
        /// - **Linux**: This makes the new window transient for parent, see <https://docs.gtk.org/gtk3/method.Window.set_transient_for.html>
        /// - **macOS**: This adds the window as a child of parent, see <https://developer.apple.com/documentation/appkit/nswindow/1419152-addchildwindow?language=objc>
        /// </summary>
        //abstract member parent: U3<Window, WebviewWindow, string> option with get, set
        /// <summary>
        /// Whether the window should be visible on all workspaces or virtual desktops.
        ///
        /// #### Platform-specific
        ///
        /// - **Windows / iOS / Android:** Unsupported.
        /// </summary>
        abstract member visibleOnAllWorkspaces: bool option with get, set
        /// <summary>
        /// Window effects.
        ///
        /// Requires the window to be transparent.
        ///
        /// #### Platform-specific:
        ///
        /// - **Windows**: If using decorations or shadows, you may want to try this workaround <https://github.com/tauri-apps/tao/issues/72#issuecomment-975607891>
        /// - **Linux**: Unsupported
        /// </summary>
        abstract member windowEffects: Effects option with get, set
        /// <summary>
        /// Set the window background color.
        ///
        /// #### Platform-specific:
        ///
        /// - **Android / iOS:** Unsupported.
        /// - **Windows**: alpha channel is ignored.
        /// </summary>
        abstract member backgroundColor: Color option with get, set
        /// <summary>
        /// Change the default background throttling behaviour.
        ///
        /// ## Platform-specific
        ///
        /// - **Linux / Windows / Android**: Unsupported. Workarounds like a pending WebLock transaction might suffice.
        /// - **iOS**: Supported since version 17.0+.
        /// - **macOS**: Supported since version 14.0+.
        ///
        /// see https://github.com/tauri-apps/tauri/issues/5250#issuecomment-2569380578
        /// </summary>
        abstract member backgroundThrottling: BackgroundThrottlingPolicy option with get, set
        /// <summary>
        /// Whether we should disable JavaScript code execution on the webview or not.
        /// </summary>
        abstract member javascriptDisabled: bool option with get, set
        /// <summary>
        /// on macOS and iOS there is a link preview on long pressing links, this is enabled by default.
        /// see https://docs.rs/objc2-web-kit/latest/objc2_web_kit/struct.WKWebView.html#method.allowsLinkPreview
        /// </summary>
        abstract member allowLinkPreview: bool option with get, set
        /// <summary>
        /// Allows disabling the input accessory view on iOS.
        ///
        /// The accessory view is the view that appears above the keyboard when a text input element is focused.
        /// It usually displays a view with "Done", "Next" buttons.
        /// </summary>
        abstract member disableInputAccessoryView: bool option with get, set



    module Monitor =

        [<Global>]
        [<AllowNullLiteral>]
        type workArea [<ParamObject; Emit("$0")>] (position: PhysicalPosition, size: PhysicalSize) =

            member val position: PhysicalPosition = nativeOnly with get, set
            member val size: PhysicalSize = nativeOnly with get, set

    module Window =

        [<AllowNullLiteral>]
        [<Interface>]
        type listeners =
            [<EmitIndexer>]
            abstract member Item: key: string -> ResizeArray<EventCallback<obj>> with get, set

    module Color =

        module U4 =

            [<Global>]
            [<AllowNullLiteral>]
            type Case3 [<ParamObject; Emit("$0")>] (red: float, green: float, blue: float, alpha: float) =

                member val red: float = nativeOnly with get, set
                member val green: float = nativeOnly with get, set
                member val blue: float = nativeOnly with get, set
                member val alpha: float = nativeOnly with get, set
