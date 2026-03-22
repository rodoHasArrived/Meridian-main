# Desktop UI Alternatives Evaluation

## Meridian — UX Architecture Assessment

**Date:** 2026-01-31
**Status:** Evaluation Complete
**Author:** Architecture Review

---

## Executive Summary

This document evaluates alternative approaches to the current native Windows desktop application (WinUI 3 / UWP-style XAML) used by the Meridian system. The evaluation prioritizes the needs of finance professionals—market analysts, traders, portfolio/risk professionals—who value **correctness, predictability, and efficiency** over visual experimentation.

**Key Finding:** The current WinUI 3 implementation is fundamentally sound. Investment should focus on strengthening the real-time data pipeline (replacing HTTP polling with WebSocket streaming) rather than framework migration.

---

## A. Current State Summary

### What WinUI 3 Does Well for Finance Users

**1. Data Density & Information Architecture**
- 47 specialized views covering the full operational spectrum
- Dashboard with metric cards, sparkline charts, and real-time counters
- Order book visualization with L2 depth heatmap, bid/ask ladders
- Data calendar with completeness heatmaps (green/yellow/red/gray cells)
- Storage analytics with pie/bar charts by symbol and type

**2. Keyboard-Centric Workflows**
- 20+ global keyboard shortcuts (Ctrl+D → Dashboard, Ctrl+B → Backfill, F5 → Refresh)
- Collector control shortcuts (Ctrl+Shift+S start, Ctrl+Shift+Q stop)
- Symbol management shortcuts (Ctrl+N add, Ctrl+F search, Delete remove)
- Backfill operations (Ctrl+R run, Ctrl+Shift+P pause/resume, Esc cancel)

**3. Windows Integration**
- Native MSIX packaging with enterprise deployment support
- System tray presence and notifications
- Theme integration with Windows light/dark mode
- Multi-architecture support (x86, x64, ARM64)

**4. MVVM Architecture**
- Clean separation using CommunityToolkit.Mvvm
- Observable properties with automatic binding updates
- RelayCommand pattern for user actions
- Singleton services with thread-safe initialization

**5. Comprehensive Feature Coverage**
- Real-time data viewer, charts, order book displays
- Backfill scheduling with per-symbol progress tracking
- Data quality monitoring (completeness, gaps, anomalies, latency)
- Archive management with tier migration and retention policies
- QuantConnect Lean integration for backtesting

### Where WinUI 3 Creates Friction

**1. XAML Compiler Type System Limitation**
- WinUI 3 XAML compiler rejects assemblies without WinRT metadata
- Cannot use standard `<ProjectReference>` to shared Contracts project
- **Workaround:** Source file linking (~1,300 lines included at compile time)
- Any Contracts change requires consideration of UWP linking

**2. Real-Time Data Update Limitations**
- Current implementation uses HTTP polling via `LiveDataService`, not WebSocket streaming
- No `IAsyncEnumerable<T>` visible for high-frequency data streams
- Canvas-based sparkline rendering can bottleneck with rapid updates
- `ObservableCollection<T>` lacks bulk update optimization

**3. Navigation State Loss**
- No built-in state preservation on page navigation
- Filter/sort/scroll positions reset when leaving and returning to pages
- Year selection, symbol filters do not persist across navigation

**4. Vendor Dependency**
- WinUI 3 is Windows-only with no cross-platform path
- Microsoft's desktop UI strategy has shifted multiple times (WPF → UWP → WinUI)
- Dependency on Windows App SDK release cadence

**5. Service Architecture Complexity**
- 100+ singleton services with manual initialization ordering
- Circular dependencies between some services
- No dependency injection container visible in codebase

### What Problems WinUI 3 Does Not Solve

| Problem | Current State |
|---------|--------------|
| Cross-platform deployment | Windows-only |
| Browser-based access for remote users | Requires separate web UI project |
| Mobile companion monitoring | Not supported |
| True real-time streaming (sub-100ms) | Polling architecture |
| Enterprise SSO/SAML | Not implemented |
| Multi-user collaboration | Single-user application |
| Offline-first with sync | No sync mechanism |

---

## B. Alternative Evaluations

---

### Alternative 1: WPF (.NET 9)

**Target User Fit:** Finance professionals on Windows requiring high data density, keyboard workflows, and enterprise deployment

**Best Use Cases:**
- Windows-only deployment with no cross-platform requirement
- Complex data grids with millions of rows (virtualized)
- Deep Windows integration needs
- Teams with existing WPF expertise

**Poor Use Cases:**
- Organizations planning future macOS/Linux support
- Teams wanting to unify web and desktop codebases
- Modern touch-first interfaces

---

**Strengths for Finance Users:**

| Strength | Detail |
|----------|--------|
| Battle-tested in finance | Bloomberg Terminal add-ins, trading platforms use WPF |
| Mature data grid ecosystem | Syncfusion, DevExpress, Telerik grids handle 100K+ rows |
| Full keyboard navigation | Native focus management, AccessKey, KeyBinding |
| Hardware-accelerated rendering | DirectX-based, handles complex charts smoothly |
| MVVM maturity | 15+ years of patterns, libraries, documentation |
| Stable API surface | No major breaking changes expected |

**Weaknesses for Finance Users:**

| Weakness | Detail |
|----------|--------|
| Windows-only | No path to macOS/Linux |
| Styling effort | Modern flat designs require custom templates |
| No WinRT APIs | Missing newer Windows features without interop |
| Older tooling | Visual Studio designer less capable than WinUI |

---

**Performance Notes:**
- Startup: 1-2 seconds (faster than WinUI 3)
- Memory: Moderate (50-150MB typical)
- Data grid: Virtualized grids handle 1M+ rows at 60fps
- Real-time: Can support 10K updates/second with proper virtualization

**Data Density & Grid Notes:**
- Industry-leading third-party grids (DevExpress, Syncfusion)
- Hierarchical, grouped, and banded row support
- Excel-like features (filtering, sorting, conditional formatting, column freezing)
- Native clipboard integration for finance workflows

---

**Operational Risk:** Low
- .NET 9 continues full WPF support
- No end-of-life announcements
- Large enterprise installed base ensures continuity

**Migration Cost (High-Level):** Medium
- XAML largely portable from WinUI 3 (some namespace changes)
- ViewModels port directly
- Services require no changes
- Effort: ~2-4 weeks for experienced team

**Bottom Line:**
WPF remains the most pragmatic choice for Windows-only finance applications requiring maximum data density, mature tooling, and long-term stability. The "older" perception is misleading—it receives active updates and has the richest ecosystem of finance-grade UI components.

---

### Alternative 2: Avalonia UI

**Target User Fit:** Organizations requiring cross-platform deployment while maintaining native-like desktop performance

**Best Use Cases:**
- Windows + macOS + Linux deployment from single codebase
- Teams with XAML/WPF experience wanting cross-platform
- Desktop-first applications (not mobile)
- Open-source preference

**Poor Use Cases:**
- Windows-only deployment (WPF is more mature)
- Organizations requiring enterprise support contracts
- Mobile-first requirements

---

**Strengths for Finance Users:**

| Strength | Detail |
|----------|--------|
| True cross-platform | Windows, macOS, Linux, WebAssembly from single XAML |
| XAML familiarity | Syntax similar to WPF/WinUI |
| Native rendering | Skia-based, not web wrapper |
| Growing ecosystem | DataGrid, charts available (less mature than WPF) |
| MIT license | No licensing costs or vendor lock-in |

**Weaknesses for Finance Users:**

| Weakness | Detail |
|----------|--------|
| Smaller ecosystem | Fewer third-party controls than WPF |
| Data grid gaps | Less feature-rich grids than Syncfusion/DevExpress for WPF |
| Newer framework | Less battle-tested in production finance systems |
| Support model | Community + commercial support, not Microsoft-backed |

---

**Performance Notes:**
- Startup: 1-3 seconds depending on platform
- Memory: 50-150MB (comparable to WPF)
- Skia rendering: Hardware-accelerated, handles complex UIs well
- WebAssembly: Usable but slower than native

**Data Density & Grid Notes:**
- Avalonia DataGrid exists but less feature-complete than WPF grids
- Third-party options emerging (Actipro, community grids)
- No equivalent to DevExpress/Syncfusion maturity yet
- Adequate for moderate complexity; may struggle with Bloomberg-level density

---

**Operational Risk:** Medium
- Actively developed with growing adoption
- No Microsoft backing (community + commercial entity)
- JetBrains uses Avalonia for Fleet IDE (validation)

**Migration Cost (High-Level):** Medium-High
- XAML requires adaptation (different styling system)
- Some WinUI-specific controls have no direct equivalent
- ViewModels port with minimal changes
- Platform-specific code needs abstraction layer
- Effort: ~4-8 weeks

**Bottom Line:**
Avalonia is the leading choice for cross-platform desktop XAML applications. Suitable if future macOS/Linux deployment is likely. However, for Windows-only finance applications, WPF's richer grid ecosystem may be preferable. The smaller component ecosystem is the primary concern for data-intensive finance UIs.

---

### Alternative 3: Electron + React/TypeScript

**Target User Fit:** Organizations prioritizing web developer availability and UI framework flexibility over native performance

**Best Use Cases:**
- Teams with strong web development expertise
- Applications already having a web component to share
- Rapid prototyping and iteration needs
- Applications with moderate (not extreme) real-time requirements

**Poor Use Cases:**
- High-frequency data streaming (>1K updates/second)
- Memory-constrained environments
- Finance applications requiring sub-50ms UI responsiveness
- Organizations with strict security policies against Chromium

---

**Strengths for Finance Users:**

| Strength | Detail |
|----------|--------|
| Rich ecosystem | AG Grid, React-Table handle complex finance grids |
| Developer availability | Large web developer pool |
| Cross-platform | Windows, macOS, Linux from single codebase |
| Rapid iteration | Hot reload, fast UI development cycle |
| Mature charting | TradingView, Highcharts, D3.js for financial visualization |

**Weaknesses for Finance Users:**

| Weakness | Detail |
|----------|--------|
| Memory overhead | 150-400MB baseline (Chromium) |
| Startup latency | 2-5 seconds typical |
| Input latency | 10-30ms additional vs native (noticeable in rapid workflows) |
| Security surface | Chromium updates required; larger attack surface |
| Not truly native | Keyboard shortcuts, focus management require extra work |

---

**Performance Notes:**
- Startup: 2-5 seconds (Chromium initialization)
- Memory: 200-500MB (problematic for multi-instance use)
- Real-time: ~1K updates/second practical ceiling before frame drops
- IPC overhead: Node.js ↔ Renderer adds latency

**Data Density & Grid Notes:**
- AG Grid (Enterprise) is finance-industry standard for web
- Handles 100K rows with virtualization
- Excel-like features available
- Chart libraries mature and feature-rich
- Requires careful optimization for high update rates

---

**Operational Risk:** Medium-High
- Electron is stable but Chromium security updates are constant
- React ecosystem churn (though React itself stable)
- Dependency management complexity (node_modules)

**Migration Cost (High-Level):** High
- Complete rewrite—XAML/C# → React/TypeScript
- Different architecture (not MVVM typically)
- Backend API integration may be reusable
- Effort: ~8-16 weeks

**Bottom Line:**
Electron + React is viable for finance applications but carries inherent trade-offs in performance and resource usage. Suitable if the organization has web expertise and accepts the overhead. Not recommended for applications requiring maximum responsiveness or deployment alongside resource-intensive trading software.

---

### Alternative 4: Tauri (Rust + Web Frontend)

**Target User Fit:** Organizations wanting web UI flexibility with lower resource consumption than Electron

**Best Use Cases:**
- Cross-platform requirement with resource efficiency priority
- Teams comfortable with Rust for backend logic
- Applications with moderate complexity
- Security-conscious deployments (smaller attack surface)

**Poor Use Cases:**
- Teams without Rust expertise for critical path debugging
- Windows-only deployments where WPF suffices
- Complex native integrations beyond Tauri's current API

---

**Strengths for Finance Users:**

| Strength | Detail |
|----------|--------|
| Low memory footprint | 20-80MB vs Electron's 200-500MB |
| Fast startup | Sub-second typical |
| Small binary size | 3-10MB vs Electron's 150MB+ |
| Web UI flexibility | Use any frontend framework |
| Security | Rust backend, no Node.js vulnerabilities |

**Weaknesses for Finance Users:**

| Weakness | Detail |
|----------|--------|
| Webview limitations | Uses system webview (Edge WebView2 on Windows) |
| Rust learning curve | Backend debugging requires Rust knowledge |
| Younger ecosystem | Fewer plugins and patterns than Electron |
| Webview inconsistency | Different rendering engines per platform |

---

**Performance Notes:**
- Startup: <1 second (significant advantage)
- Memory: 30-100MB (major advantage)
- Rendering: Depends on system webview quality
- IPC: Rust ↔ JavaScript calls fast but still serialization overhead

**Data Density & Grid Notes:**
- Same web-based grids as Electron (AG Grid, etc.)
- Webview rendering quality varies (generally good on Windows with Edge WebView2)
- Less mature than Electron for complex finance UIs

---

**Operational Risk:** Medium-High
- Tauri 2.0 released but ecosystem still maturing
- Smaller community than Electron
- Webview updates tied to OS updates (less control)

**Migration Cost (High-Level):** High
- Complete rewrite like Electron
- Rust backend adds complexity
- Same effort as Electron plus Rust learning curve
- Effort: ~10-18 weeks

**Bottom Line:**
Tauri offers compelling resource efficiency but the combination of Rust backend requirements and younger ecosystem makes it a riskier choice for mission-critical finance applications. Better suited for simpler tools or organizations with existing Rust expertise.

---

### Alternative 5: .NET MAUI Blazor Hybrid

**Target User Fit:** .NET teams wanting to share UI code between desktop and mobile while leveraging existing Blazor/web skills

**Best Use Cases:**
- Organizations invested in Blazor for web
- Desktop + mobile companion app requirement
- Teams preferring C#/Razor over XAML
- Moderate UI complexity applications

**Poor Use Cases:**
- High-frequency real-time data grids
- Maximum data density requirements
- Windows-only deployments where WPF is more mature

---

**Strengths for Finance Users:**

| Strength | Detail |
|----------|--------|
| Code sharing | Desktop, mobile, web from single Blazor codebase |
| C# throughout | No JavaScript/TypeScript context switching |
| .NET ecosystem | Existing libraries, NuGet packages work |
| Microsoft support | Official product with enterprise backing |
| Native shell | Uses platform WebView for UI rendering |

**Weaknesses for Finance Users:**

| Weakness | Detail |
|----------|--------|
| WebView rendering | Not truly native controls |
| Data grid immaturity | Blazor grids less capable than WPF/web equivalents |
| Performance ceiling | WebView adds latency vs pure native |
| MAUI stability | Still maturing; reported bugs in production |
| Limited desktop focus | Primarily mobile-oriented framework |

---

**Performance Notes:**
- Startup: 1-3 seconds
- Memory: 100-200MB (WebView overhead)
- Real-time: Adequate for ~500 updates/second; ceiling lower than native
- Hot reload: Supported but can be flaky

**Data Density & Grid Notes:**
- Blazor DataGrid components exist (Radzen, MudBlazor, Syncfusion)
- Less mature than AG Grid or WPF equivalents
- Virtualization support varies by component
- Not yet proven for Bloomberg-level density

---

**Operational Risk:** Medium
- Microsoft-backed but MAUI has had rocky releases
- Blazor Hybrid is newer pattern with less production validation
- Android/iOS WebView quirks can cause inconsistencies

**Migration Cost (High-Level):** High
- Complete rewrite from XAML to Razor
- Different architectural patterns
- Services potentially reusable
- Effort: ~8-14 weeks

**Bottom Line:**
MAUI Blazor Hybrid is compelling for organizations wanting one codebase across desktop and mobile. However, for desktop-focused finance applications, the WebView layer adds friction compared to WPF or WinUI 3. Best considered when mobile companion app is a hard requirement.

---

### Alternative 6: Qt (C++ or Python)

**Target User Fit:** Organizations requiring maximum native performance across platforms with long-term stability guarantees

**Best Use Cases:**
- Cross-platform with native performance requirements
- C++ shops or HFT firms with existing Qt investment
- Applications requiring GPU-accelerated custom rendering
- 20+ year deployment horizons

**Poor Use Cases:**
- Teams without C++ expertise
- Rapid iteration/prototyping needs
- Budgets constrained by Qt licensing costs

---

**Strengths for Finance Users:**

| Strength | Detail |
|----------|--------|
| True native performance | C++ compiled, no runtime overhead |
| Proven in finance | Trading systems, Bloomberg integrations |
| Cross-platform excellence | Windows, macOS, Linux, embedded |
| Long-term stability | 25+ year track record |
| GPU rendering | QML/Quick for custom visualizations |

**Weaknesses for Finance Users:**

| Weakness | Detail |
|----------|--------|
| Licensing costs | Commercial license required for proprietary apps |
| C++ complexity | Longer development cycles than C#/TypeScript |
| Developer scarcity | Harder to hire Qt developers |
| Steeper learning curve | Signals/slots, MOC compiler |

---

**Performance Notes:**
- Startup: Sub-second
- Memory: 30-80MB (very efficient)
- Real-time: Can handle 100K+ updates/second
- Rendering: Hardware-accelerated, excellent for charts

**Data Density & Grid Notes:**
- QTableView handles large datasets well
- Commercial components available (Qt for Application Development)
- Custom rendering possible for specialized views
- Less "plug and play" than WPF third-party grids

---

**Operational Risk:** Low
- Qt Company financially stable
- Long history of backwards compatibility
- Wide industry adoption ensures continuity

**Migration Cost (High-Level):** Very High
- Complete rewrite in C++ or Python
- Different paradigm (signals/slots vs MVVM)
- Custom component development likely needed
- Effort: ~16-24+ weeks

**Bottom Line:**
Qt offers unmatched native performance and cross-platform capability but at significant cost in development complexity and licensing. Justified only for organizations with extreme performance requirements or existing Qt investment. Overkill for most meridian scenarios.

---

### Alternative 7: Browser-Based Thin Client (Existing Web Dashboard Enhanced)

**Target User Fit:** Organizations prioritizing zero-install access, remote operation, and centralized deployment

**Best Use Cases:**
- Distributed teams needing remote access
- IT environments restricting local software installation
- Configuration/administration workflows (not trading)
- Supplementing desktop app for specific use cases

**Poor Use Cases:**
- Primary trading interface
- High-frequency data display
- Keyboard-intensive power users
- Offline operation requirements

---

**Strengths for Finance Users:**

| Strength | Detail |
|----------|--------|
| Zero installation | Access from any device with browser |
| Centralized updates | No client deployment needed |
| Already exists | Current web dashboard foundation in place |
| Cross-platform | Any OS with modern browser |
| IT-friendly | No local permissions needed |

**Weaknesses for Finance Users:**

| Weakness | Detail |
|----------|--------|
| Not real-time | Current polling architecture (2-5 second delays) |
| Limited data density | Browser viewport constraints |
| Keyboard limitations | Browser intercepts shortcuts |
| Latency | Network round-trip on every action |
| No offline | Requires connectivity |

---

**Performance Notes:**
- Current implementation: 2-5 second polling intervals
- Could add WebSocket/SignalR for real-time (~100ms latency achievable)
- Memory: Browser-dependent (typically 100-300MB per tab)
- Large grids: AG Grid handles well in browser

**Data Density & Grid Notes:**
- Current dashboard is configuration-focused, not data-dense
- Could add AG Grid for enhanced data display
- Charts via TradingView or similar are mature
- Would require significant enhancement for finance-grade density

---

**Operational Risk:** Low
- Browser technology stable
- No framework dependency if vanilla JS maintained
- SignalR/WebSocket well-established patterns

**Migration Cost (High-Level):** Medium (Enhancement, not replacement)
- Add WebSocket/SignalR hub for real-time
- Integrate professional data grid (AG Grid)
- Add financial charting library
- Effort: ~4-8 weeks for meaningful enhancement

**Bottom Line:**
Enhancing the existing web dashboard makes sense as a supplementary interface for remote access, configuration, and administration. Should not replace the native desktop for primary market data viewing due to inherent browser limitations around real-time performance and keyboard workflows.

---

### Alternative 8: Hybrid Model (Native Shell + Embedded Web Components)

**Target User Fit:** Organizations wanting to combine native performance for critical components with web flexibility for rapid iteration areas

**Best Use Cases:**
- Mixed requirement applications (some views need native, some don't)
- Gradual migration scenarios
- Teams with both native and web expertise
- Applications where configuration UI can be web but trading views must be native

**Poor Use Cases:**
- Small teams (maintaining two tech stacks)
- Uniform performance requirements across all views
- Simple applications where single tech stack suffices

---

**Strengths for Finance Users:**

| Strength | Detail |
|----------|--------|
| Best of both worlds | Native grids where needed, web flexibility elsewhere |
| Incremental migration | Can modernize piece by piece |
| Team utilization | Leverage both native and web developers |
| Performance where it matters | Critical paths stay native |

**Weaknesses for Finance Users:**

| Weakness | Detail |
|----------|--------|
| Complexity | Two tech stacks to maintain |
| UX consistency | Different behaviors between native/web sections |
| Build complexity | Multiple build pipelines |
| Integration overhead | Native ↔ web communication layer needed |

---

**Performance Notes:**
- Native sections: Full native performance
- Web sections: WebView2 performance (good on Windows)
- Integration: IPC overhead at boundaries

**Data Density & Grid Notes:**
- Can use native grids for data-intensive views
- Web components for configuration, reports, administration
- Charting could be either (TradingView vs native)

---

**Operational Risk:** Medium
- More moving parts to maintain
- Requires discipline to prevent complexity spiral
- Integration testing burden higher

**Migration Cost (High-Level):** Medium
- Can be done incrementally
- Keep critical native views, add web for new features
- Effort: ~4-8 weeks for initial integration layer

**Bottom Line:**
Hybrid approach is pragmatic for gradual modernization or mixed requirements. Keeps native performance for order books and real-time grids while allowing web flexibility for configuration and reporting. Requires team discipline to prevent architectural fragmentation.

---

## C. Comparative Summary Table

| Option | UX (Finance) | Perf | Risk | Reach | Migration Cost | Notes |
|--------|--------------|------|------|-------|----------------|-------|
| **WinUI 3 (Current)** | ★★★★☆ | ★★★★☆ | Low | Win | — | Solid foundation, source-linking workaround manageable |
| **WPF (.NET 9)** | ★★★★★ | ★★★★☆ | Very Low | Win | Medium | Best Windows-only option; mature grid ecosystem |
| **Avalonia UI** | ★★★★☆ | ★★★★☆ | Medium | All | Medium-High | Best cross-platform XAML; smaller ecosystem |
| **Electron + React** | ★★★☆☆ | ★★★☆☆ | Medium-High | All | High | Resource overhead; good grids available |
| **Tauri** | ★★★☆☆ | ★★★★☆ | Medium-High | All | High | Efficient but young ecosystem |
| **MAUI Blazor Hybrid** | ★★★☆☆ | ★★★☆☆ | Medium | All+Mobile | High | Best for mobile requirement |
| **Qt** | ★★★★★ | ★★★★★ | Low | All | Very High | Maximum performance; C++ complexity |
| **Web Dashboard (Enhanced)** | ★★☆☆☆ | ★★☆☆☆ | Low | All | Medium | Supplementary only; not primary |
| **Hybrid (Native + Web)** | ★★★★☆ | ★★★★☆ | Medium | Varies | Medium | Pragmatic gradual approach |

**Legend:**
- UX (Finance): Suitability for data-dense, keyboard-first finance workflows
- Perf: Raw performance and responsiveness
- Risk: Framework longevity and operational concerns
- Reach: Platform coverage (Win = Windows only, All = cross-platform)

---

## D. Decision Guidance

### When WinUI 3 Is the Correct Long-Term Choice

**Stay with WinUI 3 if:**

1. **Windows-only deployment is acceptable** — No requirement for macOS/Linux/browser access
2. **Current functionality is sufficient** — The 47 views cover operational needs
3. **Team has XAML expertise** — Existing investment should not be discarded lightly
4. **Microsoft ecosystem commitment** — Organization standardized on Windows App SDK
5. **Source-linking workaround is tolerable** — The XAML compiler issue is annoying but solved

**Strengthen WinUI 3 investment by:**
- Adding WebSocket/SignalR streaming to replace HTTP polling
- Implementing navigation state persistence
- Adding dependency injection container
- Improving accessibility coverage

---

### When a Hybrid or Supplemental UI Makes Sense

**Add web-based supplemental UI if:**

1. **Remote access needed** — Distributed teams need to check status without VPN + RDP
2. **Mobile monitoring desired** — Quick status checks from phone
3. **Configuration by non-primary users** — IT admins configuring but not trading
4. **Different permission model** — Read-only web view for stakeholders

**Implementation approach:**
- Enhance existing web dashboard with SignalR real-time updates
- Add AG Grid for data-intensive views
- Keep native desktop for primary market data operations
- Effort: ~4-8 weeks for meaningful enhancement

---

### Conditions That Would Justify Replacement

**Consider migration away from WinUI 3 only if:**

| Condition | Recommended Alternative |
|-----------|------------------------|
| Cross-platform is mandatory (macOS traders) | Avalonia UI |
| Organization abandoning Windows | Avalonia or Electron |
| Extreme performance needs (HFT) | Qt |
| Mobile companion app required | MAUI Blazor Hybrid |
| Massive web team, no native expertise | Electron + React |
| Resource-constrained deployment environment | Tauri |

**Migration is NOT justified by:**
- Developer preference for web technologies
- Perception that XAML is "old"
- Desire to use trending frameworks
- Single developer's unfamiliarity with current stack

---

### High-Risk or Low-Value Options for Finance Users

| Option | Risk/Value Assessment |
|--------|----------------------|
| **Electron** | High resource overhead; finance users often run multiple applications simultaneously. Memory bloat (300-500MB) is problematic. Not recommended unless web expertise is only available option. |
| **Tauri** | Promising but ecosystem immaturity is concerning for mission-critical finance application. Revisit in 2-3 years. |
| **MAUI Blazor Hybrid** | WebView layer adds friction for desktop-first usage. Only justified if mobile companion is hard requirement. |
| **Qt** | Overkill unless organization has C++ expertise and extreme performance needs. Licensing costs add friction. |
| **Full web replacement** | Browser limitations (keyboard shortcuts, real-time performance, offline) make this unsuitable as primary interface for finance professionals. |

---

## Final Recommendation

For the Meridian application serving finance professionals:

### Primary Path: Retain and Strengthen WinUI 3

The current WinUI 3 implementation is fundamentally sound. The source-linking workaround for the XAML compiler issue is inelegant but functional. The 47 views, keyboard shortcuts, and MVVM architecture serve finance users well.

**Priority Improvements (not replacement):**
1. Replace HTTP polling with WebSocket/SignalR streaming
2. Add navigation state persistence
3. Implement proper dependency injection
4. Complete accessibility coverage

### Secondary Path: Enhanced Web Dashboard as Supplement

Enhance the existing web dashboard for remote/mobile access scenarios. Add SignalR real-time updates and professional data grid. This complements rather than replaces the native desktop.

### Migration Paths to Keep Open

- **Avalonia UI** — If cross-platform becomes mandatory in future
- **WPF** — If WinUI 3 development stalls or Microsoft pivots again

---

## Key Insight

The primary risk is not the current technology choice but **underinvestment in the real-time data pipeline**. Addressing the polling architecture provides more user value than any framework migration.

Finance users care about:
- Data arriving quickly and correctly
- Keyboard shortcuts working predictably
- Grids handling large datasets smoothly
- System staying out of their way

All of these are achievable with the current WinUI 3 stack through targeted improvements.

---

*Evaluation Date: 2026-01-31*
