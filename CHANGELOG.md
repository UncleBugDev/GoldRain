# Changelog

All notable changes to **Gold Rain** will be documented in this file.

## [1.0.0] - 2026-11-16
### Initial Release
- **GTA San Andreas Style Posture:**
  - Procedural bone articulation in `LateUpdate` mimicking the classic CJ posture: backward spine tilt (-6°), tucked upper arms, and folded forearms/hands at the groin.
  - Automatic interruption safety: movement (>0.1 m/s), jumping, dodging, attacking, and damage instantly stop the posture.
- **Ballistic Golden Arc Particle Physics:**
  - True parabolic trajectory with configurable launch velocity (`StreamForce`, 1.0 - 5.0, default: 3.0).
  - Accurate spawn offset from player hips/pelvis (`hips.position + hips.forward * 0.25f + Vector3.down * 0.05f`).
  - Dual-gradient golden-amber color scheme (`#FFE600` to `#FFB700`) with soft end-of-stream fade.
  - Realistic collision physics enabled for Ground, Piece, and Default environment layers.
  - Strict performance cap: 50 max particles with ~1.0s lifetime for zero FPS penalty.
- **3D Positional Audio System:**
  - Procedural/synthesized looping liquid stream audio source with full 3D spatial attenuation (`spatialBlend = 1.0`, 15m radius).
  - Configurable volume slider in in-game GUI and BepInEx config.
- **Multiplayer & Dedicated Server Synchronization:**
  - Zero-overhead network synchronization via `ZNetView` RPC (`RPC_GoldRain_Pee`).
  - Late-join state persistence using ZDO key `GoldRain_IsPeeing`.
  - Works seamlessly across P2P co-op and dedicated servers.
- **In-Game Settings & ESC Menu Integration:**
  - Native styled Valheim ESC menu button: `ᚱ Золотой Дождь`.
  - Dark wooden OnGUI settings panel with Averia typography:
    - Master mod toggle (Enabled / Disabled).
    - Mode selector (Toggle vs. Hold).
    - Interactive hotkey rebind listener supporting any keyboard key or mouse button.
    - Stream force and audio volume live sliders.
    - Safe typing suppression: hotkey ignored when chat or console is active.
- **Thunderstore & BepInEx Packaging:**
  - Automated MSBuild target creating Thunderstore-compliant `GoldRain_v1.0.0.zip` package with manifest and custom 256x256 icon.
