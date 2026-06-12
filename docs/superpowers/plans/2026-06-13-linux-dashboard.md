# Linux Dashboard Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** The Windows five-page dashboard (Dashboard, History, Text rules, Settings, About), visually cloned, in the Linux Avalonia head — replacing the lean settings window.

**Architecture:** Pure view-models (`DashboardModel`, `DiagnosticsInfo`) move to Core behind the proven extraction pattern (namespaces unchanged, battery-verified, Windows untouched). The Linux UI stays 100% code-built Avalonia: a `ThemeTokens` port of `Theme.cs`, five custom-drawn controls (`Render` overrides mirroring the GDI+ originals), absolute-positioned page layout mirroring `DashboardPage.DoLayout`. X11 sessions gain real per-app stats attribution; Wayland attributes to "Desktop".

**Tech Stack:** Avalonia 11.3 (existing pin), `FormattedText`/`DrawingContext` for custom drawing, libX11 P/Invoke for focus tracking.

**Spec:** `docs/superpowers/specs/2026-06-12-linux-dashboard-design.md`.

---

## Conventions

- Build/battery/CI commands identical to prior plans (clean `--no-incremental`, 11-flag Windows battery, linux.yml).
- Reference sources for cloning (read before each task): `src/VoiceToText/Dashboard/Theme.cs`, `Controls/{BarChart,HeroPanel,StatTile,BreakdownBars}.cs`, `NavButton.cs`, `DashboardForm.cs`, `DashboardPage.cs`, `HistoryPage.cs`, `TextRulesPage.cs`, `AboutPage.cs`.
- Fonts: Segoe UI does not exist on Linux — `ThemeTokens` uses `FontFamily.Default` at the same sizes/weights (30/18/14/11.5/11/10.5/10/8.5; bold where Theme.cs says bold).

### Task 1: Core moves — DashboardModel, DiagnosticsInfo (+ self-tests)

**Files:**
- Move: `src/VoiceToText/Dashboard/DashboardModel.cs` → `src/VoiceToText.Core/Dashboard/DashboardModel.cs` (verbatim — file is pure, only `using VoiceToText.Stats`)
- Move: `src/VoiceToText/Diagnostics/DiagnosticsInfo.cs` → `src/VoiceToText.Core/Diagnostics/DiagnosticsInfo.cs` (one seam below)
- Modify: `src/VoiceToText/Program.cs` (provider registration)
- Modify: `src/VoiceToText.Core/Diagnostics/CoreSelfTest.cs` (gain `RunDashTest`, `RunAboutTest` — moved verbatim from head `SelfTest.cs`; both touch only Core types after the moves)
- Modify: `src/VoiceToText/Diagnostics/SelfTest.cs` (delegations: `RunDashTest`/`RunAboutTest` → CoreSelfTest; delete moved bodies)
- Modify: `src/VoiceToText.Linux/Program.cs` (dispatch `--dashtest`, `--abouttest`)
- Modify: `.github/workflows/linux.yml` (add both flags to the battery loop)

- [ ] **Step 1.1:** `git mv` DashboardModel.cs (create `src/VoiceToText.Core/Dashboard/`). No edits.
- [ ] **Step 1.2:** `git mv` DiagnosticsInfo.cs, then replace the one Windows-bound line in `Current()` (`var gpu = GpuInfo.PrimaryGpuName();`) with a pluggable provider (the `KeyNameResolver` pattern):

```csharp
    /// <summary>Platform hook returning the primary GPU's display name. Set at startup
    /// by heads that can probe it (Windows: EnumDisplayDevices); default "Unknown".</summary>
    public static Func<string>? GpuNameProvider { get; set; }
```

and in `Current()`: `var gpu = GpuNameProvider?.Invoke() ?? "Unknown";`

- [ ] **Step 1.3:** `src/VoiceToText/Program.cs`, beside `WinHotkeys.RegisterKeyNames()`:

```csharp
        Diagnostics.DiagnosticsInfo.GpuNameProvider = Diagnostics.GpuInfo.PrimaryGpuName;
```

- [ ] **Step 1.4:** Move `RunDashTest` and `RunAboutTest` bodies verbatim from head `SelfTest.cs` into `CoreSelfTest.cs` (they reference only `DashboardModel`, `StatsData`, `StatsFormat`, `ChartRange`, `DiagnosticsInfo` — all Core after 1.1/1.2). Head keeps one-line delegations:

```csharp
    public static int RunDashTest(string outputPath) => CoreSelfTest.RunDashTest(outputPath);
    public static int RunAboutTest(string outputPath) => CoreSelfTest.RunAboutTest(outputPath);
```

- [ ] **Step 1.5:** Linux `Program.cs` switch gains:

```csharp
                case "--dashtest": return CoreSelfTest.RunDashTest(Out(args, "dashtest"));
                case "--abouttest": return CoreSelfTest.RunAboutTest(Out(args, "abouttest"));
```

- [ ] **Step 1.6:** `linux.yml` battery loop: `--vadtest --statstest --dashtest --abouttest --historytest --textrulestest --logtest --controllertest --uitest --updatecheck`.
- [ ] **Step 1.7:** Build clean; full Windows battery; diff `dashtest-output.txt`/`abouttest-output.txt` against fresh pre-change runs (must be identical). Run the Linux exe's `--dashtest`/`--abouttest` on Windows (both must pass).
- [ ] **Step 1.8:** Commit: `refactor(core): DashboardModel + DiagnosticsInfo move to Core (GPU name via provider hook)`

### Task 2: `ThemeTokens`

**Files:** Create `src/VoiceToText.Linux/Ui/Dashboard/ThemeTokens.cs`

- [ ] **Step 2.1:** Full file (colors are the exact `Theme.cs` values):

```csharp
using Avalonia.Media;

namespace VoiceToText.Linux.Ui.Dashboard;

/// <summary>The Windows dashboard palette (Theme.cs) as Avalonia brushes, plus the
/// type scale. Fonts: the platform default family at Segoe-equivalent sizes.</summary>
internal static class ThemeTokens
{
    public static readonly Color WindowBg      = Color.FromRgb(0x17, 0x18, 0x1C);
    public static readonly Color SidebarBg     = Color.FromRgb(0x12, 0x13, 0x17);
    public static readonly Color CardBg        = Color.FromRgb(0x20, 0x22, 0x29);
    public static readonly Color CardBorder    = Color.FromRgb(0x2C, 0x2E, 0x36);
    public static readonly Color Accent        = Color.FromRgb(0x4C, 0x8D, 0xFF);
    public static readonly Color AccentLight   = Color.FromRgb(0x6A, 0xA0, 0xFF);
    public static readonly Color AccentDeep    = Color.FromRgb(0x27, 0x45, 0x7E);
    public static readonly Color HeroFrom      = Color.FromRgb(0x1D, 0x28, 0x40);
    public static readonly Color HeroTo        = Color.FromRgb(0x1B, 0x20, 0x30);
    public static readonly Color HeroBorder    = Color.FromRgb(0x2B, 0x35, 0x50);
    public static readonly Color NavActiveBg   = Color.FromRgb(0x22, 0x2B, 0x3D);
    public static readonly Color NavHoverBg    = Color.FromRgb(0x1B, 0x1C, 0x22);
    public static readonly Color NavActiveText = Color.FromRgb(0xCF, 0xE0, 0xFF);
    public static readonly Color TextPrimary   = Color.FromRgb(0xE8, 0xE9, 0xED);
    public static readonly Color TextSecondary = Color.FromRgb(0x8A, 0x8C, 0x95);
    public static readonly Color TextMuted     = Color.FromRgb(0x54, 0x56, 0x5F);
    public static readonly Color Gold          = Color.FromRgb(0xFF, 0xCE, 0x6B);
    public static readonly Color InputBg       = Color.FromRgb(0x2A, 0x2C, 0x34);
    public static readonly Color InputBorder   = Color.FromRgb(0x3A, 0x3D, 0x47);

    public static readonly IBrush WindowBgBrush      = new SolidColorBrush(WindowBg);
    public static readonly IBrush SidebarBgBrush     = new SolidColorBrush(SidebarBg);
    public static readonly IBrush CardBgBrush        = new SolidColorBrush(CardBg);
    public static readonly IBrush CardBorderBrush    = new SolidColorBrush(CardBorder);
    public static readonly IBrush AccentBrush        = new SolidColorBrush(Accent);
    public static readonly IBrush NavActiveBgBrush   = new SolidColorBrush(NavActiveBg);
    public static readonly IBrush NavHoverBgBrush    = new SolidColorBrush(NavHoverBg);
    public static readonly IBrush NavActiveTextBrush = new SolidColorBrush(NavActiveText);
    public static readonly IBrush TextPrimaryBrush   = new SolidColorBrush(TextPrimary);
    public static readonly IBrush TextSecondaryBrush = new SolidColorBrush(TextSecondary);
    public static readonly IBrush TextMutedBrush     = new SolidColorBrush(TextMuted);
    public static readonly IBrush GoldBrush          = new SolidColorBrush(Gold);
    public static readonly IBrush HeroBorderBrush    = new SolidColorBrush(HeroBorder);

    public static readonly IBrush HeroGradient = new LinearGradientBrush
    {
        StartPoint = new Avalonia.RelativePoint(0, 0.5, Avalonia.RelativeUnit.Relative),
        EndPoint = new Avalonia.RelativePoint(1, 0.5, Avalonia.RelativeUnit.Relative),
        GradientStops = { new GradientStop(HeroFrom, 0), new GradientStop(HeroTo, 1) },
    };
    public static readonly IBrush BarGradient = new LinearGradientBrush
    {
        StartPoint = new Avalonia.RelativePoint(0.5, 0, Avalonia.RelativeUnit.Relative),
        EndPoint = new Avalonia.RelativePoint(0.5, 1, Avalonia.RelativeUnit.Relative),
        GradientStops = { new GradientStop(Accent, 0), new GradientStop(AccentDeep, 1) },
    };
    public static readonly IBrush TrackGradient = new LinearGradientBrush
    {
        StartPoint = new Avalonia.RelativePoint(0, 0.5, Avalonia.RelativeUnit.Relative),
        EndPoint = new Avalonia.RelativePoint(1, 0.5, Avalonia.RelativeUnit.Relative),
        GradientStops = { new GradientStop(Accent, 0), new GradientStop(AccentLight, 1) },
    };

    public static readonly Avalonia.Media.FontFamily Family = Avalonia.Media.FontFamily.Default;
    public static readonly Typeface Regular = new(Family);
    public static readonly Typeface Bold = new(Family, weight: FontWeight.Bold);

    // Theme.cs sizes: HeroNumber 30b, TileNumber 18b, Heading 14b, Brand 11.5b,
    // Empty 11, NavItem 10.5, LabelBold 10b, Caption 8.5.
    public const double HeroNumberSize = 30, TileNumberSize = 18, HeadingSize = 14,
        BrandSize = 11.5, EmptySize = 11, NavItemSize = 10.5, LabelBoldSize = 10, CaptionSize = 8.5;
}
```

- [ ] **Step 2.2:** Build; commit: `feat(linux-dash): ThemeTokens — the Windows palette for Avalonia`

### Task 3: The five custom-drawn controls

**Files:** Create under `src/VoiceToText.Linux/Ui/Dashboard/`: `VttNavButton.cs`, `VttHeroPanel.cs`, `VttStatTile.cs`, `VttBarChart.cs`, `VttBreakdownBars.cs`

All five: `Control` subclasses overriding `Render(DrawingContext)`; data via `SetData(...)` + `InvalidateVisual()`, mirroring the GDI+ originals 1:1 (same paddings, radii, and text positions — see the reference sources). Helper used by all (put in `VttNavButton.cs` or a small `DrawText.cs`):

```csharp
    internal static class Draw
    {
        public static FormattedText Text(string s, Typeface tf, double size, IBrush brush) =>
            new(s, System.Globalization.CultureInfo.CurrentCulture,
                FlowDirection.LeftToRight, tf, size, brush);
    }
```

- [ ] **Step 3.1:** `VttNavButton` — 40px tall pill (`Margin 10/4`, radius 7); active: `NavActiveBg` fill + 3px accent bar at x=10,y+6,h-12 + `NavActiveText`; hover: `NavHoverBg`; text at pill.X+14 vertically centered, `NavItemSize`. Implements `PointerEntered/Exited` for hover, `PointerPressed` raises a `Clicked` event, `bool Active` property re-renders.
- [ ] **Step 3.2:** `VttHeroPanel` — rounded 10, `HeroGradient` fill + `HeroBorder` stroke; "TIME SAVED" caption (`Accent`, 8.5) at 20,18; value (`HeroNumberSize` bold, `TextPrimary`) at 18,38; subtext (8.5, `TextSecondary`) at 20,Height−28; right-aligned gold `"{n}-day streak"` (`LabelBoldSize` bold) vertically centered, 24 from the right edge.
- [ ] **Step 3.3:** `VttStatTile` — rounded 9 card (`CardBg`/`CardBorder`); number (18 bold, primary) at 13,10; caption (8.5, secondary) at 14,42.
- [ ] **Step 3.4:** `VttBarChart` — rounded 9 card; title (8.5, secondary) at 14,12; plot rect = (14, 36, W−28, H−62); per bar: slot = plotW/n, barW = max(2, slot−2), h = words/max·plotH (min 2 when words>0), `BarGradient` fill; axis labels: first date "MMM d" left, middle date centered, "Today" right-aligned at plotBottom+6 (`TextMuted`, 8.5). `SetData(IReadOnlyList<DayBar> series, long max, string title)`.
- [ ] **Step 3.5:** `VttBreakdownBars` — rounded 9 card; "Top apps" title at 14,12; rows from y=40, rowH=30: name left + count right (8.5), 7px track at y+18 width W−28 in `CardBorder`, fill = `TrackGradient` × `Fraction`. `SetData(IReadOnlyList<AppBar> apps)`.
- [ ] **Step 3.6:** Build; commit: `feat(linux-dash): five custom-drawn dashboard controls`

### Task 4: `LinuxDashboardPage` (the stats page)

**Files:** Create `src/VoiceToText.Linux/Ui/Dashboard/LinuxDashboardPage.cs`

A `Canvas` subclass with absolute placement mirroring `DashboardPage.DoLayout` exactly: pad 20, gap 10; hero 116 tall; five tiles (w−4·gap)/5 × 64; chart 63% of remaining width × full remaining height, breakdown the rest; range segment (7/30/All as styled `Button`s, 32/32/40×22) in the chart's top-right (chartRight−12, top+9); records caption strip (22) at the bottom; centered empty-state TextBlock when `!HasData`. `Bind(DashboardModel m)` + `SetRange(ChartRange)` reproduce the Windows `Bind`/`ApplyRange` logic verbatim (tile captions: "Words dictated", "Dictations", "Avg words/dictation", "Speaking WPM", "Speaking time"; chart titles "Activity — last 7 days/last 30 days/all time"; records: `Best dictation: …` + 8 spaces + `Busiest day: …`). Layout re-runs on `SizeChanged`.

- [ ] **Step 4.1:** Implement per the above against the reference source. Build.
- [ ] **Step 4.2:** Commit: `feat(linux-dash): dashboard stats page`

### Task 5: Remaining pages

**Files:** Create `LinuxHistoryPage.cs`, `LinuxTextRulesPage.cs`, `LinuxSettingsPage.cs`, `LinuxAboutPage.cs` under `Ui/Dashboard/`

- [ ] **Step 5.1:** `LinuxSettingsPage` — MOVE the body of today's `SettingsWindow` (`src/VoiceToText.Linux/Ui/SettingsWindow.cs`) into a `UserControl`, restyled with ThemeTokens (section headers `HeadingSize`, notes `CaptionSize`/`TextSecondary`, page background `WindowBg`). Same controls, same save-on-change handlers, same hotkey/GNOME section, same updates section.
- [ ] **Step 5.2:** `LinuxHistoryPage` — header row ("History" heading + "Clear all" button right + enable-toggle when `HistoryEnabled` false shows the Windows-style hint); `ScrollViewer` > `StackPanel` of row cards (`CardBg`, radius 8, padding 12): wrapped text (`TextPrimary`, 11), meta line `"{Today/date} {HH:mm}   ·   {App}   ·   {N words}   ·   {model short label}   ·   {x.x}s"` (`TextSecondary`, 8.5, using `ModelOption.ShortLabel`), Copy button per row (writes `ClipboardHelper.SetTextAsync`). `Reload()` is idempotent: compute a signature `(count, newest.Time.Ticks)` and skip rebuild when unchanged (the v0.8.11 no-flicker semantics); rebuild when it differs.
- [ ] **Step 5.3:** `LinuxTextRulesPage` — spoken-commands `CheckBox` (saves on change); rules editor: `StackPanel` of rows, each `[TextBox find][TextBox replace][✕]`, plus "Add rule" button; edits write through to `settings.Replacements` + `settings.Save()` on focus-loss/change; live preview: input `TextBox` + read-only output `TextBox` showing `TextRules.Apply(input, settings.Replacements, settings.SpokenCommandsEnabled)` re-evaluated on any change (line breaks rendered, the v0.8.8 semantics).
- [ ] **Step 5.4:** `LinuxAboutPage` — version heading; rows from `DiagnosticsInfo.Current(settings)` as label/value lines (acceleration row tinted `Gold` when CPU, `Accent` when GPU — mirroring the Windows green/amber intent); "Copy diagnostics" button (`ToClipboardText()` → clipboard). On Linux `Current()` reports the loaded Whisper runtime via the Core `RuntimeProbe`, model + size, OS/framework/arch; GPU row shows the provider default "Unknown" (no Linux provider in this iteration).
- [ ] **Step 5.5:** Build; commit per page or as one: `feat(linux-dash): history, text rules, settings, about pages`

### Task 6: `DashboardWindow` + wiring (replaces SettingsWindow)

**Files:**
- Create: `src/VoiceToText.Linux/Ui/Dashboard/DashboardWindow.cs`
- Modify: `src/VoiceToText.Linux/Ui/VttApp.cs` (menu + open path)
- Delete: `src/VoiceToText.Linux/Ui/SettingsWindow.cs` (absorbed by Task 5.1)
- Modify: `src/VoiceToText.Linux/Ui/UiSelfTest.cs`

- [ ] **Step 6.1:** `DashboardWindow`: 920×620 (MinWidth 900/MinHeight 620), `WindowBg`; left sidebar 172 (`SidebarBg`): brand "  Voice to Text" 54 tall (`BrandSize` bold, `TextPrimary`), five `VttNavButton`s (Dashboard, Settings, Text rules, History, About — same order as Windows), version footer 28 (`CaptionSize`, `TextMuted`); right content host swapping the five pages (`IsVisible` toggling, all constructed up front). `RefreshData()` builds `new DashboardModel(stats.Data, DateOnly.FromDateTime(DateTime.Now), settings.TypingSpeedWpm)` and binds the stats page; called on `Opened` and `Activated`; History page `Reload()` on activation when it's the active page. Subscribes `controller.Transcribed` → `Dispatcher.UIThread.Post(RefreshData + history reload)`; unsubscribes on close. (No unsaved-changes prompt: the Linux settings save on change — note this deviation from Windows in a comment.)
- [ ] **Step 6.2:** `VttApp`: menu item text "Dashboard…"; `ShowSettings()` becomes `ShowDashboard()` opening/activating a single `DashboardWindow` instance (recreate when closed — current SettingsWindow pattern); `FirstRunWindow` untouched.
- [ ] **Step 6.3:** `UiSelfTest`: construct `DashboardWindow` (with the real `AppServices`), `Show()`, switch through all five pages, `Close()`; then FirstRunWindow as today. Output text mentions "5 pages".
- [ ] **Step 6.4:** Build; run the Linux exe `--uitest` on Windows (passes headless). Commit: `feat(linux-dash): five-page DashboardWindow replaces the settings window`

### Task 7: X11 focus attribution

**Files:**
- Create: `src/VoiceToText.Linux/Platform/X11FocusTracker.cs`
- Modify: `src/VoiceToText.Linux/Platform/X11Native.cs` (new imports)
- Modify: `src/VoiceToText.Linux/AppServices.cs`

- [ ] **Step 7.1:** `X11Native` additions:

```csharp
    [LibraryImport(LibX11, StringMarshalling = StringMarshalling.Utf8)]
    public static partial nuint XInternAtom(IntPtr display, string atomName, int onlyIfExists);

    [LibraryImport(LibX11)]
    public static partial int XGetWindowProperty(IntPtr display, nuint window, nuint property,
        long longOffset, long longLength, int delete, nuint reqType,
        out nuint actualType, out int actualFormat, out nuint nItems, out nuint bytesAfter,
        out IntPtr prop);

    [LibraryImport(LibX11)]
    public static partial int XFree(IntPtr data);

    [StructLayout(LayoutKind.Sequential)]
    public struct XClassHint { public IntPtr res_name; public IntPtr res_class; }

    [LibraryImport(LibX11)]
    public static partial int XGetClassHint(IntPtr display, nuint window, out XClassHint hint);
```

- [ ] **Step 7.2:** `X11FocusTracker` — static `GetFocusedAppName()`: open display (cached connection, lazily reopened on failure); read `_NET_ACTIVE_WINDOW` (Atom via `XInternAtom`, property of the root window, type `XA_WINDOW`=33, format 32 → first item is the window id); `XGetClassHint` on it → `res_class` string (`Marshal.PtrToStringUTF8`), `XFree` both pointers; any failure → null. Whole call wrapped in try/catch; never throws.
- [ ] **Step 7.3:** `AppServices`: in the ctor set `Controller.AppNameProvider = "Desktop";`. In BOTH dictation-start paths (the X11 `Pressed` handler and `Toggle()` when the controller is Idle), when `HotkeyTier == HotkeyTier.X11Grab`, set `Controller.AppNameProvider = X11FocusTracker.GetFocusedAppName() ?? "Desktop";` immediately before `ToggleAsync()`.
- [ ] **Step 7.4:** Build; commit: `feat(linux): per-app stats attribution on X11; Wayland buckets to "Desktop"`

### Task 8: Verification gates

- [ ] **Step 8.1:** Windows: clean build 0 warnings; full battery (now incl. delegated dashtest/abouttest) green; `--dashwindow` green; `publish.ps1` version unchanged.
- [ ] **Step 8.2:** Push; linux.yml green on both legs (battery + uitest with the new window + in-AppImage tests).
- [ ] **Step 8.3:** Fan out the adversarial review (focus: Avalonia Render math vs the GDI+ originals; layout edge cases at MinSize; History reload signature; X11 property P/Invoke; Transcribed-event threading) + fix findings.
- [ ] **Step 8.4:** VM visual validation with the user: deploy the CI AppImage to the QEMU rig, open every page side-by-side with Windows, dictate once on each of History/Dashboard to see live refresh, confirm attribution shows "Desktop" (Wayland session).

### Task 9: Release

- [ ] **Step 9.1:** After the VM gate: bump `src/Version.props` → `0.10.0`; commit `v0.10.0: Linux dashboard — full five-page parity with Windows`.
- [ ] **Step 9.2:** `/release` (both platforms; Linux steps L1–L5). Windows release notes: "no Windows changes"; Linux notes lead with the dashboard.

---

## Self-review

- Spec coverage: Core moves ✔ (T1), ThemeTokens ✔ (T2), five controls ✔ (T3), stats page ✔ (T4), remaining pages ✔ (T5), window + replace SettingsWindow + tray/IPC ✔ (T6), X11 attribution + "Desktop" ✔ (T7), dashtest both platforms + uitest five pages + VM pass ✔ (T8), release ✔ (T9). Out-of-scope list respected (no onboarding/widget/hotkey-capture).
- Placeholders: control/page tasks specify exact metrics + reference sources rather than full listings (the established phase-2b pattern); all constants, signatures, and P/Invoke shapes are in the plan.
- Type consistency: `Draw.Text` helper shared; `VttNavButton.Clicked`/`Active` used by T6; `SetData` signatures in T3 match T4's `Bind` usage; `Controller.AppNameProvider` is the existing Core property.
