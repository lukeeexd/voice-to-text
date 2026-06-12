# Linux Port — Phase 2b: Hotkeys, Injection, Tray & Settings UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Make the Linux daemon a usable dictation app: global hotkey (X11 grab + universal IPC tier with GNOME auto-registration), clipboard-always paste injection (XTEST on X11, RemoteDesktop portal on Wayland, notification fallback), Avalonia tray icon + lean settings window + first-run dialog, XDG autostart — all headless-smoke-tested in CI.

**Architecture:** Platform backends stay in `VoiceToText.Linux/Platform` (P/Invoke X11/XTest, Tmds.DBus portals); the Avalonia shell lives in `VoiceToText.Linux/Ui`. The daemon boots Avalonia with no visible window (tray-resident, `ShutdownMode.OnExplicitShutdown`); everything degrades gracefully (no tray → `--settings` works; no injection → clipboard + notification).

**Tech Stack:** Avalonia **11.3.x** (NOT 12 — major version post-dates trusted knowledge; pin 11.3.2 and bump patch only if restore fails), Avalonia.Headless (CI smoke), Tmds.DBus 0.94.1, libX11/libXtst P/Invoke.

**Spec:** `docs/superpowers/specs/2026-06-12-linux-port-design.md` (phase 2, second half).

**Deliberate scope cuts (spec-consistent):** GlobalShortcuts portal (tier 2, KDE/Hyprland) is deferred — tier 3 (IPC + DE keybinding) covers those users; revisit after VM validation. No in-app hotkey capture UI — the default Ctrl+Shift+Space shows in settings, GNOME auto-registration handles binding, other DEs get copy-paste instructions. Hold-to-talk ships on tier 1 (X11) only, exactly as the spec's recording-mode note says.

---

### Task 1: X11 native layer (hotkey grab + XTEST)

**Files:**
- Create: `src/VoiceToText.Linux/Platform/X11Native.cs`
- Create: `src/VoiceToText.Linux/Platform/VkKeys.cs`

- [ ] **Step 1.1:** `X11Native.cs` — P/Invoke for libX11/libXtst. CRITICAL: install an X error handler before `XGrabKey` — the default handler kills the process on `BadAccess` (combo already grabbed by another app):

```csharp
using System.Runtime.InteropServices;

namespace VoiceToText.Linux.Platform;

internal static partial class X11Native
{
    private const string LibX11 = "libX11.so.6";
    private const string LibXtst = "libXtst.so.6";

    public const int KeyPress = 2;
    public const int KeyRelease = 3;
    public const int GrabModeAsync = 1;

    public const uint ShiftMask = 1 << 0;
    public const uint LockMask = 1 << 1;    // CapsLock
    public const uint ControlMask = 1 << 2;
    public const uint Mod1Mask = 1 << 3;    // Alt
    public const uint Mod2Mask = 1 << 4;    // NumLock
    public const uint Mod4Mask = 1 << 6;    // Super/Win

    [LibraryImport(LibX11, StringMarshalling = StringMarshalling.Utf8)]
    public static partial IntPtr XOpenDisplay(string? display);

    [LibraryImport(LibX11)]
    public static partial int XCloseDisplay(IntPtr display);

    [LibraryImport(LibX11)]
    public static partial nuint XDefaultRootWindow(IntPtr display);

    [LibraryImport(LibX11)]
    public static partial byte XKeysymToKeycode(IntPtr display, nuint keysym);

    [LibraryImport(LibX11)]
    public static partial int XGrabKey(IntPtr display, int keycode, uint modifiers,
        nuint grabWindow, int ownerEvents, int pointerMode, int keyboardMode);

    [LibraryImport(LibX11)]
    public static partial int XUngrabKey(IntPtr display, int keycode, uint modifiers, nuint grabWindow);

    [LibraryImport(LibX11)]
    public static partial int XPending(IntPtr display);

    [LibraryImport(LibX11)]
    public static partial int XNextEvent(IntPtr display, out XEvent ev);

    [LibraryImport(LibX11)]
    public static partial int XFlush(IntPtr display);

    [LibraryImport(LibX11)]
    public static partial int XSync(IntPtr display, int discard);

    // Detectable auto-repeat: repeats arrive as KeyPress-only (no fake KeyRelease),
    // so press/release state tracking is reliable.
    [LibraryImport(LibX11)]
    public static partial int XkbSetDetectableAutoRepeat(IntPtr display, int detectable, IntPtr supportedOut);

    public delegate int XErrorHandler(IntPtr display, IntPtr errorEvent);

    [LibraryImport(LibX11)]
    public static partial IntPtr XSetErrorHandler(XErrorHandler handler);

    [LibraryImport(LibXtst)]
    public static partial int XTestFakeKeyEvent(IntPtr display, uint keycode, int isPress, nuint delay);

    /// <summary>x11 XKeyEvent (64-bit layout); embedded in the 192-byte XEvent union.</summary>
    [StructLayout(LayoutKind.Sequential)]
    public struct XKeyEvent
    {
        public int type;
        public nuint serial;
        public int send_event;
        public IntPtr display;
        public nuint window;
        public nuint root;
        public nuint subwindow;
        public nuint time;
        public int x, y, x_root, y_root;
        public uint state;
        public uint keycode;
        public int same_screen;
    }

    [StructLayout(LayoutKind.Explicit, Size = 192)]
    public struct XEvent
    {
        [FieldOffset(0)] public int type;
        [FieldOffset(0)] public XKeyEvent xkey;
    }
}
```

- [ ] **Step 1.2:** `VkKeys.cs` — Win32 VK ↔ X11 keysym ↔ evdev keycode ↔ GNOME binding-name mapping for the supported hotkey set (settings store Win32 VKs on all OSes):

```csharp
namespace VoiceToText.Linux.Platform;

/// <summary>
/// Maps the Win32 virtual-key codes stored in settings to X11 keysyms, Linux evdev
/// keycodes (for the RemoteDesktop portal), and GNOME accelerator names. Covers the
/// keys the app supports as hotkeys: A–Z, 0–9, F1–F24, Space and a few navigation keys.
/// </summary>
internal static class VkKeys
{
    public static nuint? ToKeysym(uint vk) => vk switch
    {
        >= 0x41 and <= 0x5A => 0x61 + (vk - 0x41),      // XK_a..XK_z (lowercase)
        >= 0x30 and <= 0x39 => 0x30 + (vk - 0x30),      // XK_0..XK_9
        >= 0x70 and <= 0x87 => 0xFFBE + (vk - 0x70),    // XK_F1..XK_F24
        0x20 => 0x0020,                                  // XK_space
        0x0D => 0xFF0D,                                  // XK_Return
        0x09 => 0xFF09,                                  // XK_Tab
        0x08 => 0xFF08,                                  // XK_BackSpace
        0x2E => 0xFFFF,                                  // XK_Delete
        0x24 => 0xFF50,                                  // XK_Home
        0x23 => 0xFF57,                                  // XK_End
        _ => null,
    };

    /// <summary>GNOME accelerator string, e.g. "&lt;Control&gt;&lt;Shift&gt;space".</summary>
    public static string? ToGnomeBinding(Hotkeys.HotkeyDefinition hotkey)
    {
        var key = hotkey.VirtualKey switch
        {
            >= 0x41 and <= 0x5A => ((char)(hotkey.VirtualKey + 32)).ToString(), // a-z
            >= 0x30 and <= 0x39 => ((char)hotkey.VirtualKey).ToString(),
            >= 0x70 and <= 0x87 => $"F{hotkey.VirtualKey - 0x6F}",
            0x20 => "space",
            0x0D => "Return",
            0x09 => "Tab",
            _ => null,
        };
        if (key is null) return null;

        var mods = "";
        if ((hotkey.Modifiers & Hotkeys.HotkeyDefinition.ModControl) != 0) mods += "<Control>";
        if ((hotkey.Modifiers & Hotkeys.HotkeyDefinition.ModShift) != 0) mods += "<Shift>";
        if ((hotkey.Modifiers & Hotkeys.HotkeyDefinition.ModAlt) != 0) mods += "<Alt>";
        if ((hotkey.Modifiers & Hotkeys.HotkeyDefinition.ModWin) != 0) mods += "<Super>";
        return mods + key;
    }

    public static uint ToX11Modifiers(uint vkModifiers)
    {
        uint m = 0;
        if ((vkModifiers & Hotkeys.HotkeyDefinition.ModControl) != 0) m |= X11Native.ControlMask;
        if ((vkModifiers & Hotkeys.HotkeyDefinition.ModShift) != 0) m |= X11Native.ShiftMask;
        if ((vkModifiers & Hotkeys.HotkeyDefinition.ModAlt) != 0) m |= X11Native.Mod1Mask;
        if ((vkModifiers & Hotkeys.HotkeyDefinition.ModWin) != 0) m |= X11Native.Mod4Mask;
        return m;
    }
}
```

- [ ] **Step 1.3:** Build solution (compiles on Windows). Commit: `feat(linux): X11 native layer + VK/keysym/GNOME key mapping`

### Task 2: X11 hotkey backend + tier selection

**Files:**
- Create: `src/VoiceToText.Linux/Platform/X11HotkeyService.cs`
- Create: `src/VoiceToText.Linux/Platform/HotkeyTier.cs`

- [ ] **Step 2.1:** `X11HotkeyService.cs` — its own display connection + poll thread (30 ms cadence via `XPending`, so Stop never blocks). Grabs the 4 lock-mask combos, tracks pressed state for detectable-autorepeat-safe toggling, raises Pressed/Released for hold-to-talk:

```csharp
using VoiceToText.Diagnostics;
using VoiceToText.Hotkeys;

namespace VoiceToText.Linux.Platform;

/// <summary>Tier-1 hotkey: XGrabKey on the X11 root window. Used on X11 sessions only
/// (under Wayland an X grab never fires while native windows have focus).</summary>
public sealed class X11HotkeyService : IDisposable
{
    private IntPtr _display;
    private Thread? _thread;
    private volatile bool _stop;
    private int _keycode;
    private uint _x11Mods;
    private bool _keyDown;
    private static X11Native.XErrorHandler? _errorHandler; // GC anchor

    public event Action? Pressed;
    public event Action? Released;

    /// <summary>Try to start grabbing. False when X11/keysym is unavailable.</summary>
    public bool Start(HotkeyDefinition hotkey)
    {
        var keysym = VkKeys.ToKeysym(hotkey.VirtualKey);
        if (keysym is null) return false;

        _display = X11Native.XOpenDisplay(null);
        if (_display == IntPtr.Zero) return false;

        _errorHandler = static (_, _) => 0; // swallow BadAccess (combo grabbed elsewhere)
        X11Native.XSetErrorHandler(_errorHandler);
        X11Native.XkbSetDetectableAutoRepeat(_display, 1, IntPtr.Zero);

        _keycode = X11Native.XKeysymToKeycode(_display, keysym.Value);
        if (_keycode == 0) { X11Native.XCloseDisplay(_display); _display = IntPtr.Zero; return false; }

        _x11Mods = VkKeys.ToX11Modifiers(hotkey.Modifiers);
        var root = X11Native.XDefaultRootWindow(_display);
        foreach (var extra in new uint[] { 0, X11Native.LockMask, X11Native.Mod2Mask, X11Native.LockMask | X11Native.Mod2Mask })
            X11Native.XGrabKey(_display, _keycode, _x11Mods | extra, root, 0,
                X11Native.GrabModeAsync, X11Native.GrabModeAsync);
        X11Native.XSync(_display, 0);

        _stop = false;
        _thread = new Thread(EventLoop) { IsBackground = true, Name = "x11-hotkey" };
        _thread.Start();
        Log.Info($"X11 hotkey grab active: {hotkey.Describe()}");
        return true;
    }

    private void EventLoop()
    {
        while (!_stop)
        {
            while (!_stop && X11Native.XPending(_display) > 0)
            {
                X11Native.XNextEvent(_display, out var ev);
                if (ev.type == X11Native.KeyPress && ev.xkey.keycode == _keycode && !_keyDown)
                {
                    _keyDown = true;
                    Pressed?.Invoke();
                }
                else if (ev.type == X11Native.KeyRelease && ev.xkey.keycode == _keycode && _keyDown)
                {
                    _keyDown = false;
                    Released?.Invoke();
                }
            }
            Thread.Sleep(30);
        }
    }

    public void Dispose()
    {
        _stop = true;
        _thread?.Join(500);
        if (_display != IntPtr.Zero)
        {
            var root = X11Native.XDefaultRootWindow(_display);
            foreach (var extra in new uint[] { 0, X11Native.LockMask, X11Native.Mod2Mask, X11Native.LockMask | X11Native.Mod2Mask })
                X11Native.XUngrabKey(_display, _keycode, _x11Mods | extra, root);
            X11Native.XCloseDisplay(_display);
            _display = IntPtr.Zero;
        }
    }
}
```

- [ ] **Step 2.2:** `HotkeyTier.cs` — session detection + the user-facing description used by the settings window:

```csharp
namespace VoiceToText.Linux.Platform;

public enum HotkeyTier { X11Grab, IpcBinding }

public static class SessionInfo
{
    public static bool IsWayland =>
        string.Equals(Environment.GetEnvironmentVariable("XDG_SESSION_TYPE"), "wayland", StringComparison.OrdinalIgnoreCase);

    public static bool IsGnome =>
        (Environment.GetEnvironmentVariable("XDG_CURRENT_DESKTOP") ?? "")
            .Contains("GNOME", StringComparison.OrdinalIgnoreCase);

    public static bool HasX11Display =>
        !string.IsNullOrEmpty(Environment.GetEnvironmentVariable("DISPLAY"));

    /// <summary>X11 grab only on real X11 sessions; everywhere else the IPC tier.</summary>
    public static HotkeyTier PickHotkeyTier()
        => !IsWayland && HasX11Display ? HotkeyTier.X11Grab : HotkeyTier.IpcBinding;
}
```

- [ ] **Step 2.3:** Build; commit: `feat(linux): X11 hotkey grab (tier 1) + session tier detection`

### Task 3: GNOME shortcut auto-registration (tier 3 helper)

**Files:**
- Create: `src/VoiceToText.Linux/Platform/GnomeShortcuts.cs`

- [ ] **Step 3.1:** Registers `<exe> --toggle` as a GNOME custom shortcut via the `gsettings` CLI (present by definition on GNOME). Idempotent; returns false (with log) on any failure:

```csharp
using System.Diagnostics;
using VoiceToText.Hotkeys;

namespace VoiceToText.Linux.Platform;

/// <summary>Writes a GNOME custom keyboard shortcut running `voicetotext --toggle` —
/// the supported hotkey path on GNOME Wayland, which has no GlobalShortcuts portal.</summary>
public static class GnomeShortcuts
{
    private const string Schema = "org.gnome.settings-daemon.plugins.media-keys";
    private const string KeyPath = "/org/gnome/settings-daemon/plugins/media-keys/custom-keybindings/voicetotext/";
    private const string KeySchema = "org.gnome.settings-daemon.plugins.media-keys.custom-keybinding";

    public static string ExecutablePath =>
        Environment.GetEnvironmentVariable("APPIMAGE") ?? Environment.ProcessPath ?? "voicetotext";

    public static bool Register(HotkeyDefinition hotkey)
    {
        var binding = VkKeys.ToGnomeBinding(hotkey);
        if (binding is null) return false;
        try
        {
            var list = Run("get", Schema, "custom-keybindings")?.Trim() ?? "@as []";
            if (!list.Contains(KeyPath))
            {
                // list looks like: @as [] | ['/path/a/', '/path/b/']
                var newList = list.StartsWith("@as") || list == "[]"
                    ? $"['{KeyPath}']"
                    : list.TrimEnd(']') + $", '{KeyPath}']";
                if (Run("set", Schema, "custom-keybindings", newList) is null) return false;
            }
            return Run("set", $"{KeySchema}:{KeyPath}", "name", "'VoiceToText dictation'") is not null
                && Run("set", $"{KeySchema}:{KeyPath}", "command", $"'{ExecutablePath} --toggle'") is not null
                && Run("set", $"{KeySchema}:{KeyPath}", "binding", $"'{binding}'") is not null;
        }
        catch (Exception ex)
        {
            Diagnostics.Log.Error("GNOME shortcut registration failed", ex);
            return false;
        }
    }

    private static string? Run(params string[] args)
    {
        var psi = new ProcessStartInfo("gsettings")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        foreach (var a in args) psi.ArgumentList.Add(a);
        using var p = Process.Start(psi);
        if (p is null) return null;
        var stdout = p.StandardOutput.ReadToEnd();
        p.WaitForExit(5000);
        return p.ExitCode == 0 ? stdout : null;
    }
}
```

- [ ] **Step 3.2:** Build; commit: `feat(linux): GNOME custom-shortcut auto-registration via gsettings`

### Task 4: Text injection — clipboard-always, XTEST / portal / notify

**Files:**
- Create: `src/VoiceToText.Linux/Platform/LinuxTextInjector.cs`
- Create: `src/VoiceToText.Linux/Platform/XTestPaste.cs`
- Create: `src/VoiceToText.Linux/Platform/PortalRemoteDesktop.cs`
- Create: `src/VoiceToText.Linux/Platform/Notifications.cs`
- Modify: `src/VoiceToText.Core/Settings/AppSettings.cs` (additive `PortalRestoreToken`)

- [ ] **Step 4.1:** `XTestPaste.cs` — synthesize Ctrl+V on X11 (short delay lets the user's hotkey modifiers clear; matches Windows-injector timing semantics):

```csharp
namespace VoiceToText.Linux.Platform;

internal static class XTestPaste
{
    /// <summary>Fake Ctrl+V via XTEST. Returns false when X11 is unavailable.</summary>
    public static bool PasteCtrlV()
    {
        var display = X11Native.XOpenDisplay(null);
        if (display == IntPtr.Zero) return false;
        try
        {
            Thread.Sleep(80); // let physical hotkey modifiers clear
            var ctrl = X11Native.XKeysymToKeycode(display, 0xFFE3); // XK_Control_L
            var v = X11Native.XKeysymToKeycode(display, 0x76);      // XK_v
            if (ctrl == 0 || v == 0) return false;
            X11Native.XTestFakeKeyEvent(display, ctrl, 1, 0);
            X11Native.XTestFakeKeyEvent(display, v, 1, 0);
            X11Native.XTestFakeKeyEvent(display, v, 0, 0);
            X11Native.XTestFakeKeyEvent(display, ctrl, 0, 0);
            X11Native.XFlush(display);
            return true;
        }
        finally
        {
            X11Native.XCloseDisplay(display);
        }
    }
}
```

- [ ] **Step 4.2:** `PortalRemoteDesktop.cs` — the Wayland injection path over Tmds.DBus. Implements the portal Request/Response dance (subscribe to the request path's `Response` signal BEFORE the call returns results), `persist_mode=2` with the token stored in settings (`PortalRestoreToken`, new nullable string on AppSettings — additive, invisible to Windows). `NotifyKeyboardKeycode` uses evdev codes: KEY_LEFTCTRL=29, KEY_V=47. EVERY failure path logs and returns false (caller falls back to notification). Define the three D-Bus interfaces (`org.freedesktop.portal.RemoteDesktop`, `org.freedesktop.portal.Request`, `org.freedesktop.portal.Session`) as `[DBusInterface]` proxies; full file written at execution following Tmds.DBus's documented portal pattern; structure:

```csharp
// Key shape (full implementation at execution):
// 1. Connection.Session; proxy of org.freedesktop.portal.Desktop at /org/freedesktop/portal/desktop
// 2. CreateSession({ handle_token, session_handle_token }) → await Response on the returned request path
// 3. SelectDevices(session, { types: 1u /*KEYBOARD*/, persist_mode: 2u, restore_token? }) → await Response
// 4. Start(session, "", { handle_token }) → Response gives restore_token → save to settings
// 5. PasteCtrlV(): NotifyKeyboardKeycode(session, {}, 29, 1); (47,1); (47,0); (29,0)
// 6. Session kept open for the daemon lifetime; rebuilt (one re-prompt) if it dies.
```

- [ ] **Step 4.3:** `Notifications.cs` — `org.freedesktop.Notifications.Notify` via Tmds.DBus (interface + one static `ShowAsync(title, body)`; failures logged, never thrown).
- [ ] **Step 4.4:** `LinuxTextInjector.cs` — orchestrates: set clipboard (delegate injected from the Avalonia shell: `Func<string, Task>`), then X11 → XTEST, Wayland → portal, else/failure → notification "Transcript copied — press Ctrl+V."; exposes `event Action<bool /*pasted*/>? Completed` for the tray status.
- [ ] **Step 4.5:** AppSettings addition (additive, with doc comment): `public string? PortalRestoreToken { get; set; }` — verify Windows battery still green after (it must: unknown-field tolerance was already proven, this is a known field).
- [ ] **Step 4.6:** Build both heads + Windows battery; commit: `feat(linux): clipboard-always injection — XTEST, RemoteDesktop portal, notify fallback`

### Task 5: Avalonia shell — tray, settings window, first-run

**Files:**
- Modify: `src/VoiceToText.Linux/VoiceToText.Linux.csproj` (Avalonia 11.3.2, Avalonia.Themes.Fluent, Avalonia.Headless, Tmds.DBus)
- Create: `src/VoiceToText.Linux/Ui/App.axaml` + `App.axaml.cs`
- Create: `src/VoiceToText.Linux/Ui/TrayIcons.cs` (runtime-drawn WriteableBitmap icons: gray idle / red recording / amber transcribing — no asset pipeline)
- Create: `src/VoiceToText.Linux/Ui/SettingsWindow.axaml` + `.cs`
- Create: `src/VoiceToText.Linux/Ui/FirstRunWindow.axaml` + `.cs`
- Modify: `src/VoiceToText.Linux/Daemon.cs` (boot Avalonia, wire tray ↔ controller ↔ injector ↔ hotkey tier)

Settings window contents (lean, per spec): model picker + download progress, language, auto-stop toggle + seconds, sound cues toggle + volume slider (0–100), hotkey section (active tier, binding text, GNOME auto-register button OR instructions), autostart toggle (XDG `.desktop`), Force CPU toggle (`RuntimeOptions.RuntimeLibraryOrder`), "uses the system default microphone" note. First-run: welcome + model download + hotkey setup, sets `OnboardingCompleted`.

- [ ] **Step 5.1–5.6:** implement per the file list (full XAML/code authored at execution — markup kept minimal, every control bound in code-behind, no MVVM framework). Daemon: `BuildAvaloniaApp().StartWithClassicDesktopLifetime(args, ShutdownMode.OnExplicitShutdown)`; IPC `settings`/`show` commands open the window via `Dispatcher.UIThread`.
- [ ] **Step 5.7:** Build; commit per sub-component (`feat(linux): tray shell`, `feat(linux): settings window`, ...).

### Task 6: XDG autostart

**Files:**
- Create: `src/VoiceToText.Linux/Platform/XdgAutostart.cs` (write/remove `~/.config/autostart/voicetotext.desktop`, `Exec=<ExecutablePath> --minimized`)

- [ ] Build; commit: `feat(linux): XDG autostart`

### Task 7: Headless UI smoke + CI extension

**Files:**
- Modify: `src/VoiceToText.Linux/Program.cs` (`--uitest`)
- Create: `src/VoiceToText.Linux/Ui/UiSelfTest.cs` (Avalonia.Headless session constructs SettingsWindow + FirstRunWindow, exit 0/1 — the `--dashwindow` equivalent)
- Modify: `.github/workflows/linux.yml` (add `--uitest` to the battery loop)

- [ ] Build; push; **CI gate: both ubuntu legs green including `--uitest`**. Iterate as needed.

### Task 8: Windows regression + reviews

- [ ] Clean `--no-incremental` build → 0 warnings; full 11-flag Windows battery → green.
- [ ] Fan out parallel reviews (adversarial on the 2b diff; special focus: X11 struct layouts/error handler, portal call shapes, thread marshaling between capture/IPC/UI threads).
- [ ] No version bump (Windows unchanged); phase 3 bumps + ships both platforms.

---

## Self-review

- Spec coverage: hotkey tiers 1+3 with explicit tier-2 deferral note ✔, clipboard-always + XTEST/portal/notify ✔, tray-optional UI ✔, autostart ✔, headless UI test in CI ✔, push-to-talk X11-only ✔.
- Placeholder check: Tasks 4.2 and 5.x intentionally specify structure + exact protocol/UI contents rather than full listings (the portal dance and XAML are authored at execution against this file's stated shapes); all constants, signatures, and risky layouts ARE fully specified (X11 structs, masks, keysyms, evdev codes, gsettings schema paths).
- Type consistency: `VkKeys` referenced by X11HotkeyService/GnomeShortcuts/portal evdev path; `SessionInfo.PickHotkeyTier` drives Daemon wiring; injector's clipboard delegate provided by the Avalonia shell.
