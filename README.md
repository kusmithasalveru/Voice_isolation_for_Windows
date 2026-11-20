# IsoVoice — Offline Voice Isolation for Windows

**IsoVoice** is a lightweight, 100% offline background noise suppression utility for Windows—bringing iPhone-style **Voice Isolation** to WhatsApp Desktop, Google Meet, Zoom, Microsoft Teams, Discord, and any other Windows calling or recording app.

Powered by the native **RNNoise** recurrent neural network library and **NAudio (WASAPI)**, IsoVoice captures audio from your laptop's built-in or external microphone, suppresses ambient background noise in real time on your CPU, and routes clean speech to a virtual microphone—with zero cloud dependencies and zero telemetry.

---

## Features

- **100% Offline & Private:** All audio processing happens locally on your machine. No recordings or audio streams ever leave your PC.
- **Real-Time AI Noise Suppression:** Uses native C `rnnoise.dll` (48 kHz mono, 10 ms frame size) combined with a voice-activity-detection (VAD) silence gate to filter out fans, keyboard typing, traffic, and background chatter while preserving natural speech.
- **Works With Any Calling App:** Routes cleaned audio through **VB-Audio Virtual Cable**, making it selectable as a microphone input across WhatsApp Desktop, Zoom, Meet, Teams, Discord, and OBS[cite: 1].
- **System Tray Utility:** Lives quietly in the Windows notification area with a one-click **ON/OFF** toggle, status indicator icon, state persistence across restarts, and optional **Auto-start on login**.
- **No External Hardware Needed:** Works out of the box with your laptop's built-in microphone array as well as wired, USB, or Bluetooth headsets[cite: 1].

---

## How It Works

```text
┌─────────────────────┐     ┌──────────────────────────┐     ┌─────────────────────────┐     ┌──────────────────────┐
│  Laptop / Headset   │ ──▶ │     IsoVoice Engine      │ ──▶ │  VB-Audio Virtual Cable │ ──▶ │  WhatsApp / Meet /   │
│     Microphone      │     │ (RNNoise + Silence Gate) │     │     (CABLE Input)       │     │  Zoom / Discord      │
└─────────────────────┘     └──────────────────────────┘     └─────────────────────────┘     └──────────────────────┘
