<div align="center">

# ⏱️ ScreenPi

**The zero-footprint, native Screen Time engine for Windows.**

[![.NET 8.0](https://img.shields.io/badge/.NET_8.0-512BD4?style=for-the-badge&logo=dotnet&logoColor=white)](https://dotnet.microsoft.com/)
[![WinUI 3](https://img.shields.io/badge/WinUI_3-0078D4?style=for-the-badge&logo=windows&logoColor=white)](https://learn.microsoft.com/en-us/windows/apps/winui/winui3/)
[![SQLite](https://img.shields.io/badge/SQLite_WAL-003B57?style=for-the-badge&logo=sqlite&logoColor=white)](https://sqlite.org/)
[![Footprint](https://img.shields.io/badge/CPU_Usage-0.0%25-success?style=for-the-badge)](#)
[![Privacy](https://img.shields.io/badge/Privacy-100%25_Offline-10B981?style=for-the-badge)](#)
[![License: MIT](https://img.shields.io/badge/License-MIT-gray?style=for-the-badge)](https://opensource.org/licenses/MIT)

*ScreenPi brings the seamless, premium experience of macOS and iOS Screen Time to Windows 11. Built the way desktop applications were always meant to be built: zero Electron bloat, no constant polling loops, and strict data privacy. Just raw, unmanaged system-level performance paired with a breathtaking Fluent UI.*

[**Explore Architecture**](#-the-architecture) · [**How to Run**](#-how-to-run--install) · [**Releases**](https://github.com/octacoreweb/ScreenPi/releases) · [**Report Bug**](#)

<br/>
<img src="DashboardUI/screenshot.jpeg" alt="ScreenPi Dashboard Preview" width="850" style="border-radius: 12px; box-shadow: 0 15px 35px rgba(0,0,0,0.6); border: 1px solid #333;"/>
</div>

---

## 📜 The ScreenPi Manifesto

Most modern desktop trackers are web applications disguised as native apps. They consume hundreds of megabytes of RAM just to run a background timer, relying on brute-force `while(true)` loops that prevent your CPU from entering low-power states. 

**ScreenPi is an engineering love letter to the Windows API.** 

By physically decoupling the background telemetry engine from the presentation layer, ScreenPi achieves a microscopic resource footprint. The UI only exists in memory when you explicitly summon it. The background tracker sleeps perfectly until the OS wakes it.

---

## 🏛️ The Architecture

The solution is divided into three highly specialized, decoupled projects to guarantee absolute zero-CPU background monitoring and non-blocking data reads.

```mermaid
graph TD
    subgraph OS ["Windows 11 OS Layer"]
        Event["EVENT_SYSTEM_FOREGROUND"]
    end

    subgraph TrackerService ["1. TrackerService (Headless Background)"]
        Hook["SetWinEventHook API<br/>(Zero-CPU Listener)"]
        Idle["GetLastInputInfo<br/>(10s Poll Timer)"]
        MemBuffer["ActiveSession RAM Buffer<br/>(< 20MB Footprint)"]
        
        Event -- "OS wakes process" --> Hook
        Hook -->|"Window Switch"| MemBuffer
        Idle -->|"> 60s Inactivity"| MemBuffer
    end

    subgraph SharedData ["2. SharedData (Class Library)"]
        Repo["Thread-Safe Repository"]
        Db[("SQLite Database<br/>(WAL Mode)")]
        
        MemBuffer -- "Flush (Switch / Idle / 5m)" --> Repo
        Repo <--> Db
    end

    subgraph DashboardUI ["3. DashboardUI (WinUI 3 Frontend)"]
        Mica["Mica Backdrop / Fluent Design"]
        Summary["Daily Analytics Engine"]
        
        Db -. "Concurrent Read<br/>(Non-locking)" .-> Summary
        Summary --> Mica
    end
    
    style OS fill:#1e1e1e,stroke:#333,stroke-width:2px,color:#fff
    style TrackerService fill:#0078D4,stroke:#005a9e,stroke-width:2px,color:#fff
    style SharedData fill:#003B57,stroke:#002b40,stroke-width:2px,color:#fff
    style DashboardUI fill:#512BD4,stroke:#3b2092,stroke-width:2px,color:#fff
```

---

## ⚙️ Engineering Masterclass: Solving the Bottlenecks

ScreenPi isn't just an app; it's a blueprint for doing Windows development *right*. Here is how we solved the most common desktop development anti-patterns:

| Constraint Solved | Implementation | The Engineering Benefit |
| :--- | :--- | :--- |
| **Zero-CPU Tracking** | Win32 `SetWinEventHook` + `GetMessageW` | Abandons `Thread.Sleep` polling entirely. The app consumes **0.0% CPU** because it literally blocks and waits for the OS event loop to wake it. |
| **GC Crash Immunity** | `static NativeMethods.WinEventDelegate` | Prevents the .NET (CLR) Garbage Collector from randomly reclaiming unmanaged P/Invoke callbacks, eliminating `AccessViolationException` crashes. |
| **Micro Disk I/O** | `ActiveSession` RAM buffering | Disk thrashing kills SSD lifespans. ScreenPi holds live seconds in RAM, flushing to disk *only* upon context switch, a 60-second idle trigger, or every 5 minutes. |
| **Concurrent DB Access**| SQLite `PRAGMA journal_mode=WAL` | Write-Ahead Logging allows the background service to save data at the exact same millisecond the dashboard is reading it, preventing fatal `database is locked` errors. |
| **Native Typography** | WinUI 3 + `MicaBackdrop` | Renders with hardware acceleration, native font smoothing, and fluid acrylic materials—achieving an Apple-level aesthetic without Electron bloat. |

---

## 🚀 How to Run & Install

### ⚡ Quick Start for End-Users (No Coding Required)

ScreenPi is distributed as both a **Windows Setup Installer** and a **Portable Standalone Package**—no SDKs or build tools required.

#### Option A: Windows Installer (Recommended)
1. Head over to the **[GitHub Releases](https://github.com/octacoreweb/ScreenPi/releases)** page.
2. Download and launch **`ScreenPi_Setup_x64.exe`**.
3. Follow the setup wizard:
   - Installs files into `C:\Program Files\EchoPhase\ScreenPi` (or custom folder).
   - Automatically registers `TrackerService.exe` in Windows Startup (`HKCU\Software\Microsoft\Windows\CurrentVersion\Run`) so tracking starts silently every time you log in.
   - Places a **ScreenPi** shortcut on your Desktop and Start Menu for the dashboard.
   - Launches both the background tracker and the UI immediately upon completion.

#### Option B: Portable ZIP
1. Download **`ScreenPi-Windows-x64.zip`** from [Releases](https://github.com/octacoreweb/ScreenPi/releases).
2. Extract the archive to any directory on your computer (e.g. `C:\Tools\ScreenPi`).
3. **Start tracking:** Double-click `TrackerService.exe`. It runs silently in the background with ~0% CPU.
4. **View statistics:** Double-click `DashboardUI.exe` anytime to view your screen time dashboard.

---

### 💻 Running from Source (For Developers)

#### Prerequisites
* Windows 10 (Build 17763+) or **Windows 11** (Recommended for native Mica material)
* [.NET 8.0 SDK](https://dotnet.microsoft.com/download)

#### 1. Clone the repository
```powershell
git clone https://github.com/octacoreweb/ScreenPi.git
cd ScreenPi
```

#### 2. Build the solution
```powershell
dotnet build ScreenPi.sln
```

#### 3. Run the Background Tracker
Open a terminal and start `TrackerService`. It will initialize the SQLite database at `%LOCALAPPDATA%\ScreenPi\screenpi.db` and start listening for foreground window switches:
```powershell
dotnet run --project TrackerService\TrackerService.csproj
```

#### 4. Run the Dashboard UI
In a second terminal window, launch the WinUI 3 dashboard frontend:
```powershell
dotnet run --project DashboardUI\DashboardUI.csproj -p:Platform=x64
```

#### 5. Run Automated Tests
Execute the unit and integration test suite:
```powershell
dotnet test
```

#### 6. Build Release Binaries & Installer Locally
Run the build script to generate the self-contained single-file executables, `.zip` archive, and Inno Setup installer:
```powershell
.\build_release.ps1
```
Output artifacts are saved to `.\publish\ScreenPi\`, `.\ScreenPi-Windows-x64.zip`, and `.\ScreenPi_Setup_x64.exe`.

---

## 🧠 Code Highlight: The Zero-CPU Engine

Here is the precise P/Invoke architecture that allows ScreenPi to listen to the OS without polling. This is the heart of the application:

```csharp
// Declared as STATIC to survive the .NET Garbage Collector during unmanaged execution
private static WinEventDelegate _winEventDelegate = new WinEventDelegate(WinEventProc);

// Subscribe directly to the OS shell layer
_winEventHook = SetWinEventHook(
    EVENT_SYSTEM_FOREGROUND, EVENT_SYSTEM_FOREGROUND, 
    IntPtr.Zero, _winEventDelegate, 
    0, 0, WINEVENT_OUTOFCONTEXT
);

// Block the thread efficiently until an OS message arrives
int bRet;
while ((bRet = GetMessage(out MSG msg, IntPtr.Zero, 0, 0)) != 0)
{
    TranslateMessage(ref msg);
    DispatchMessage(ref msg);
}
```

---

## 🗺️ The Roadmap

- [x] **Phase 1:** Zero-CPU Win32 background tracking & SQLite WAL data layer.
- [x] **Phase 2:** WinUI 3 Dashboard (Total Time, App List, Date Picker).
- [ ] **Phase 3:** High-Res Icon Extraction Engine (displaying native `.exe` app logos in the UI).
- [ ] **Phase 4:** Smart Categorization (Auto-grouping into *Games, Productivity, Browsers*).
- [ ] **Phase 5:** System Tray Integration (Run completely invisible on startup).
- [ ] **Phase 6:** Digital Wellbeing Enforcer (On-screen limits and top-most window nudges).

---

## 🤝 Contributing

We welcome contributions from Windows engineers who share a passion for performance and clean UI.

1. Fork the Project
2. Create your Feature Branch (`git checkout -b feature/AmazingFeature`)
3. Commit your Changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the Branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

---

## 🔒 Privacy & License

**ScreenPi is strictly 100% offline.** 
Your screen time data is stored exclusively on your machine at `%LOCALAPPDATA%\ScreenPi\screenpi.db`. No telemetry is sent to the cloud. No analytics are tracked. You own your hardware, and you own your data.

Distributed under the **MIT License**. See `LICENSE` for more information.

<br/>

<div align="center">
  <i>Engineered for Windows. Built with C#.</i>
</div>
