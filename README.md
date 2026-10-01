<div align="center">
    
  <img src="assets/app-icon-4.png" width="80" height="80" alt="icon">
  
# RikkaTracker

<br>

**[中文文档](README_zh.md)**

</div>

**RikkaTracker** is a lightweight application activity tracker designed exclusively for Windows. It accurately records and analyzes every minute you spend on your PC. Featuring an intuitive timeline (Gantt chart) and multi-dimensional statistics, it helps you track time usage and boost productivity.

---

## Core Features

### 1. Three-tier Activity Tracking Model
Beyond basic runtime statistics, RikkaTracker implements an advanced three-layer state machine:
- **Foreground Active**: The app you are currently interacting with (focused window + active operation).
- **Foreground Inactive**: Visible but unfocused window, or status downgraded automatically after idle timeout.
- **Background Running**: The process remains alive while the window is minimized or hidden, keeping complete records of the entire application lifecycle.

### 2. Timeline View
Built with high-performance `DrawingVisual` hybrid rendering, the timeline runs smoothly:
- **Visual Classification**: Line weight and color vary by activity status, making it easy to distinguish focused work and idle time at a glance.
- **Smooth Zoom**: Freely zoom from hourly to minute-level view to check full details of daily activities.
- **Smart Interaction**: Responsive hover tooltips display app name, window title and duration in real time.

### 3. Ultra-lightweight & Ultra-low Resource Usage
- Optimized for ultimate lightweight operation, eliminating redundant features and inefficient rendering logic, built on native high-performance WPF rendering architecture.
- The program runs resident in the background with extremely low memory usage and negligible CPU consumption. It has no pop-up windows, redundant background processes, or persistent network requests. It perfectly supports long-term silent background operation without impacting system performance, game frame rates, or office software experience, achieving fully imperceptible time tracking.

### 4. Web Browsing Tracking & Browser Extension
- Supports real-time web activity tracking through the browser extension, capturing the currently active web page and recording browsing duration.
- Extensions available for Chrome, Edge and Firefox (`WebExtensions/` in the repository; the Firefox build loads via `about:debugging` → "Load Temporary Add-on" or from AMO).
- Website domains are displayed as user-friendly names (e.g., `google.com` → "Google"), improving interface readability.
- A dedicated view mode switch (Apps only / Web only / Combined) lets you flexibly browse different data dimensions.

---

![RikkaTracker Overview](public/概览页.png)

![RikkaTracker Timeline](public/时间轴.png)

![Low Resource Usage](public/低资源占用.png)

![Policy Rules](public/策略规则.png)

---

## Getting Started

### System Requirements
- Windows 10 / 11
- .NET 8.0 SDK
- Visual Studio 2022

### Build Instructions
1. Clone the repository:
   ```bash
   git clone https://github.com/remnant-song/RikkaTracker.git
   ```
2. Open `RikkaTracker.sln` with Visual Studio.

---
*Developed with ❤️ by [remnant-song](https://github.com/remnant-song)*
