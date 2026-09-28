# Changelog

## 0.5.1

- Replaced the controller preset carousel with direct button capture: select the controller row, then press the desired button.
- Added live conflict validation against the player's current game bindings for both keyboard and controller.
- Conflicting candidates are rejected without replacing the previous Far Gaze binding, including the game's Taunt/Challenge action.
- Re-checks conflicts while playing, so a later change in the game's own control menu cannot make Far Gaze and another action fire together.
- Simplified the settings page to its three functional rows; explanatory text is hidden unless a binding conflict needs attention.
- The controller row now shows a disconnected state or the connected controller family's button icon, and captures the next physical button directly.
- Controller bindings are stored separately per detected controller family; each newly seen family defaults to left-stick click.
- Normalized the keyboard/controller row typography and shortened the mode names to “点按” and “长按”.

## 0.5.0

- Added a native-styled “远眺” settings page under Options → Mods, powered by Silksong.ModMenu 0.7.6.
- Added live keyboard rebinding; the default remains B.
- Added controller binding with cross-platform names for PS4/PS5, Xbox, and Nintendo layouts, plus an unbound option.
- The default controller binding is the otherwise-unused left-stick click: L3 on PlayStation, LS on Xbox, and left-stick click on Nintendo controllers.
- Added Press mode (rising-edge toggle) and Hold mode (active while the combined keyboard/controller signal is high).
- Keyboard and controller bindings work simultaneously and changes are saved through the existing BepInEx configuration.

## 0.4.0

- Made vertical camera looking orthogonal to far gaze: Up/Down and right-stick vertical input continue moving the camera while Hornet remains in the background-facing pose.
- Uses the game's remapped input actions, native 0.85-second keyboard/controller look delay, immediate right-stick behavior, and room look limits.
- Separates camera flags from look-animation flags so `TurnToBG` / `Idle BG` always retain visual priority.
- Preserves an existing up/down camera position when far gaze is entered while already looking vertically.

## 0.3.0

- Renders the distant background at the current viewport height while far gaze is active, capped at a configurable 2160p by default.
- Compensates the blur sampling radius for the higher-resolution texture so the transition still begins at approximately the original blur strength.
- Keeps the high-resolution texture until blur restoration finishes, then returns to the game's original resolution and memory usage.
- Reduced false input rejection by trusting the authoritative `onGround + idle + zero movement` state instead of transient aerial and directional sub-flags.
- Every rejected B-key press now writes its exact reason, current animation, and relevant state to the log.

## 0.2.0

- Fixed animation ownership: the mod now stops the game's automatic animation controller before using `PlayClipForced`, then restores control with the matching version token.
- Replaced the invalid `LightBlur.blurSize` assumption with a Harmony patch that continuously scales the game's `_BlurInfo` sampling offset.
- Detects the real runtime `LightBlur` and `BlurPlane` objects used by `LightBlurredBackground`.
- Improved F8 diagnostics with animation-control state, current clip, blur pass count, and blur amount.
- Built directly against the installed game assemblies (Steam build 22479045).

## 0.1.0-preview

- First offline-buildable prototype.
- Added B-key rising-edge toggle with strict standing-state gate.
- Added `TurnToBG` / `Idle BG` / `TurnToFG` animation flow and interrupted exit.
- Added smooth camera blur-size and BlurPlane fallback transitions.
- Added F8 runtime diagnostics and scene/unload restoration safeguards.
