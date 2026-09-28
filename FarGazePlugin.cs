using System;
using System.Collections;
using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using HarmonyLib;
using InControl;
using Silksong.ModMenu;
using Silksong.ModMenu.Plugin;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FarGaze;

[BepInPlugin(PluginGuid, PluginName, PluginVersion)]
[BepInDependency(ModMenuPlugin.Id, "0.7.6")]
public sealed partial class FarGazePlugin : BaseUnityPlugin, IModMenuCustomMenu
{
    public const string PluginGuid = "io.github.localdev.fargaze";
    public const string PluginName = "Far Gaze / 远眺";
    public const string PluginVersion = "0.5.1";

    private const float DefaultCameraLookDelay = 0.85f;

    private enum GazeState { Inactive, Entering, Looking, Exiting }

    private ConfigEntry<KeyCode> _gazeKey = null!;
    private ConfigEntry<FarGazeInputMode> _inputMode = null!;
    private ConfigEntry<KeyCode> _diagnosticKey = null!;
    private ConfigEntry<float> _blurTransitionSeconds = null!;
    private ConfigEntry<string> _entryClip = null!;
    private ConfigEntry<string> _holdClip = null!;
    private ConfigEntry<string> _exitClip = null!;
    private ConfigEntry<bool> _verboseLogging = null!;
    private ConfigEntry<bool> _highResolutionBackground = null!;
    private ConfigEntry<int> _maximumBackgroundHeight = null!;
    private readonly BlurTransition _blur = new();
    private Harmony _harmony = null!;
    private GazeState _state;
    private HeroController? _controlledHero;
    private HeroAnimationController? _animationController;
    private float _animationEndsAt;
    private int _animationControlVersion = -1;
    private bool _ownsAnimation;
    private bool _warnedNoBlur;
    private bool _warnedNoAnimation;
    private int _gazeLookDirection;
    private float _gazeLookTimer;

    private void Awake()
    {
        _gazeKey = Config.Bind("Input", "FarGazeKey", KeyCode.B, "远眺键盘按键。默认：B；None 表示未绑定。");
        _inputMode = Config.Bind("Input", "InputMode", FarGazeInputMode.Press,
            "点按：按下时切换；长按：保持按住时远眺，松开时退出。");
        _diagnosticKey = Config.Bind("Diagnostics", "DumpDiagnosticsKey", KeyCode.F8, "把角色状态、候选动画和虚化组件写入 BepInEx 日志。");
        _blurTransitionSeconds = Config.Bind("Visual", "BlurTransitionSeconds", 0.16f,
            new ConfigDescription("解除/恢复背景虚化的过渡秒数。", new AcceptableValueRange<float>(0.05f, 0.50f)));
        _entryClip = Config.Bind("Animation", "EntryClip", "TurnToBG", "进入远眺时播放的动画名。");
        _holdClip = Config.Bind("Animation", "HoldClip", "Idle BG", "远眺保持姿势的动画名。");
        _exitClip = Config.Bind("Animation", "ExitClip", "TurnToFG", "正常退出时播放的动画名。");
        _verboseLogging = Config.Bind("Diagnostics", "VerboseLogging", false, "记录每次成功进入、正常退出和中断退出。");
        _highResolutionBackground = Config.Bind("Visual", "HighResolutionBackground", true, "远眺期间把背景渲染纹理提升到屏幕分辨率，避免解除虚化后出现低像素感。");
        _maximumBackgroundHeight = Config.Bind("Visual", "MaximumBackgroundHeight", 2160,
            new ConfigDescription("高清背景纹理的最大高度；用于限制显存开销。", new AcceptableValueRange<int>(720, 4320)));

        _blur.Initialise(Logger);
        _blur.ConfigureResolution(_highResolutionBackground.Value, _maximumBackgroundHeight.Value);
        _harmony = new Harmony(PluginGuid);
        _harmony.PatchAll();
        SceneManager.sceneLoaded += OnSceneLoaded;
        Logger.LogInfo($"{PluginName} {PluginVersion} loaded. Keyboard={_gazeKey.Value}, Controller={DescribeCurrentControllerBinding()}, Mode={_inputMode.Value}, Diagnostics={_diagnosticKey.Value}");
    }

    private void OnSceneLoaded(Scene scene, LoadSceneMode mode)
    {
        ForceReset(true);
        StartCoroutine(ScanSceneAfterLoad(scene.name));
    }

    private IEnumerator ScanSceneAfterLoad(string sceneName)
    {
        // BlurCamera is created during the cameras' OnEnable/Start sequence.
        for (var i = 0; i < 12; i++) yield return null;
        _blur.ScanScene();
        if (_verboseLogging.Value) Logger.LogInfo($"Scene '{sceneName}': {_blur.Describe()}");
    }

    private void Update()
    {
        _blur.Tick(Time.unscaledDeltaTime, _blurTransitionSeconds.Value);
        if (Input.GetKeyDown(_diagnosticKey.Value)) DumpDiagnostics();
        RefreshControllerBindingUi();
        if (ProcessControllerBindingCapture()) return;

        var hero = HeroController.instance;
        if (_state != GazeState.Inactive && (!hero || hero != _controlledHero || !IsStandingPose(hero, requireIdleActorState: false, out _)))
        {
            Interrupt();
            return;
        }

        if (_state == GazeState.Entering && Time.unscaledTime >= _animationEndsAt)
        {
            PlayHoldAnimation();
            _state = GazeState.Looking;
        }
        else if (_state == GazeState.Exiting && Time.unscaledTime >= _animationEndsAt)
        {
            FinishNormalExit();
        }

        if ((_state == GazeState.Entering || _state == GazeState.Looking) && hero)
        {
            UpdateGazeCameraLook(hero);
        }

        var (pressed, held) = ReadFarGazeInput();
        if (_inputMode.Value == FarGazeInputMode.Press)
        {
            if (!pressed) return;
            if (_state == GazeState.Inactive) TryEnter(hero, logFailure: true);
            else if (_state == GazeState.Entering || _state == GazeState.Looking) BeginNormalExit();
        }
        else
        {
            if (_state == GazeState.Inactive && held) TryEnter(hero, logFailure: pressed);
            else if ((_state == GazeState.Entering || _state == GazeState.Looking) && !held) BeginNormalExit();
        }
    }

    private void TryEnter(HeroController? hero, bool logFailure)
    {
        string? reason = null;
        if (Time.timeScale > 0.01f && hero && IsStandingPose(hero, requireIdleActorState: true, out reason)) Enter(hero);
        else if (logFailure) LogIgnoredInput(hero, reason ?? "hero not available");
    }

    private (bool Pressed, bool Held) ReadFarGazeInput()
    {
        var keyboardKey = _gazeKey.Value;
        var keyboardAction = string.Empty;
        var keyboardConflict = keyboardKey != KeyCode.None && TryFindKeyboardConflict(keyboardKey, out keyboardAction);
        ReportRuntimeBindingConflict("keyboard", keyboardConflict ? keyboardAction : null, ref _lastKeyboardConflict);
        var keyboardPressed = keyboardKey != KeyCode.None && !keyboardConflict && Input.GetKeyDown(keyboardKey);
        var keyboardHeld = keyboardKey != KeyCode.None && !keyboardConflict && Input.GetKey(keyboardKey);

        var controlType = ResolveControllerControl(GetCurrentControllerBinding());
        var controllerAction = string.Empty;
        var controllerConflict = controlType != InputControlType.None && TryFindControllerConflict(controlType, out controllerAction);
        ReportRuntimeBindingConflict("controller", controllerConflict ? controllerAction : null, ref _lastControllerConflict);
        var device = InputManager.ActiveDevice;
        var control = controlType != InputControlType.None && !controllerConflict && device != null ? device.GetControl(controlType) : null;
        var controllerPressed = control != null && control.WasPressed;
        var controllerHeld = control != null && control.IsPressed;
        return (keyboardPressed || controllerPressed, keyboardHeld || controllerHeld);
    }

    private void ReportRuntimeBindingConflict(string device, string? action, ref string? lastConflict)
    {
        if (action == null)
        {
            lastConflict = null;
            return;
        }
        if (lastConflict == action) return;

        lastConflict = action;
        var deviceName = device == "keyboard" ? "键盘" : "手柄";
        var message = $"当前{deviceName}远眺键与游戏操作“{action}”重复；为避免同时触发，远眺已暂时忽略该绑定。";
        Logger.LogWarning(message);
        SetBindingStatus(message, true);
    }

    private void Enter(HeroController hero)
    {
        _controlledHero = hero;
        _animationController = hero.AnimCtrl;
        _blur.ScanSceneIfNeeded();
        BeginGazeCameraLook(hero);

        // StopAnimationControl hands animation ownership to callers. The versioned
        // form prevents us from re-enabling it if another system takes ownership later.
        _animationControlVersion = hero.StopAnimationControlVersioned();
        _ownsAnimation = true;

        var duration = PlayClipAndGetDuration(_entryClip.Value, 0.35f);
        _animationEndsAt = Time.unscaledTime + duration;
        _state = GazeState.Entering;
        _blur.SetLooking(true);

        if (!_blur.IsAvailable && !_warnedNoBlur)
        {
            _warnedNoBlur = true;
            Logger.LogWarning("No active background LightBlur/BlurPlane was found in this scene. The pose will still work; press F8 for diagnostics.");
        }
        if (_verboseLogging.Value) Logger.LogInfo($"Far gaze entered (entry={_entryClip.Value}, {duration:0.000}s; {_blur.Describe()}).");
    }

    private void PlayHoldAnimation()
    {
        if (!PlayClip(_holdClip.Value) && !_warnedNoAnimation)
        {
            _warnedNoAnimation = true;
            Logger.LogWarning($"Animation '{_holdClip.Value}' was not found/playable. Press F8 for candidates.");
        }
    }

    private void BeginNormalExit()
    {
        _blur.SetLooking(false);
        var clip = FindClip(_exitClip.Value) != null ? _exitClip.Value : "TurnFromBG";
        var duration = PlayClipAndGetDuration(clip, 0.35f);
        _animationEndsAt = Time.unscaledTime + duration;
        _state = GazeState.Exiting;
        if (_verboseLogging.Value) Logger.LogInfo($"Far gaze normal exit (clip={clip}, {duration:0.000}s).");
    }

    private void FinishNormalExit()
    {
        ReleaseAnimationControl();
        ClearHeroReferences();
        _state = GazeState.Inactive;
    }

    private void Interrupt()
    {
        if (_state == GazeState.Inactive) return;
        _blur.SetLooking(false);
        ReleaseAnimationControl();
        ClearHeroReferences();
        _state = GazeState.Inactive;
        if (_verboseLogging.Value) Logger.LogInfo("Far gaze interrupted; exit animation skipped.");
    }

    private void ReleaseAnimationControl()
    {
        if (_ownsAnimation && _controlledHero)
        {
            _controlledHero.StartAnimationControl(_animationControlVersion);
        }
        _ownsAnimation = false;
        _animationControlVersion = -1;
    }

    private void ClearHeroReferences()
    {
        _controlledHero = null;
        _animationController = null;
        _gazeLookDirection = 0;
        _gazeLookTimer = 0f;
    }

    private void BeginGazeCameraLook(HeroController hero)
    {
        var states = hero.cState;
        _gazeLookDirection = states.lookingUp ? 1 : states.lookingDown ? 2 : 0;
        _gazeLookTimer = Reflection.AsFloat(Reflection.ReadMember(hero, "lookDelayTimer"), 0f);
        if (_gazeLookDirection != 0 && _gazeLookTimer < DefaultCameraLookDelay)
        {
            // The camera is already looking, even if the game's private timer was
            // unavailable. Preserve that position rather than recentering on entry.
            _gazeLookTimer = DefaultCameraLookDelay;
        }
    }

    private void UpdateGazeCameraLook(HeroController hero)
    {
        var inputHandler = InputHandler.SilentInstance;
        var actions = inputHandler ? inputHandler.inputActions : null;
        if (actions == null) return;

        var rightStickUp = actions.RsUp.IsPressed;
        var rightStickDown = actions.RsDown.IsPressed;
        var upPressed = actions.Up.IsPressed || rightStickUp;
        var downPressed = actions.Down.IsPressed || rightStickDown;

        // Match the game's direction latching: a held direction must be released
        // before the opposite one takes over.
        if (_gazeLookDirection == 0)
        {
            if (upPressed) _gazeLookDirection = 1;
            else if (downPressed) _gazeLookDirection = 2;
        }
        else if (_gazeLookDirection == 1 && !upPressed)
        {
            _gazeLookDirection = 0;
        }
        else if (_gazeLookDirection == 2 && !downPressed)
        {
            _gazeLookDirection = 0;
        }

        var states = hero.cState;
        var lookDelay = Math.Max(0.01f, Reflection.AsFloat(Reflection.ReadMember(hero, "LOOK_DELAY"), DefaultCameraLookDelay));
        if (_gazeLookDirection == 1)
        {
            states.lookingDown = false;
            states.lookingUp = rightStickUp || _gazeLookTimer >= lookDelay;
            if (!states.lookingUp) _gazeLookTimer += Time.deltaTime;
        }
        else if (_gazeLookDirection == 2)
        {
            states.lookingUp = false;
            var downBlocked = Reflection.ReadBool(hero, "lookDownBlocked");
            states.lookingDown = !downBlocked && (rightStickDown || _gazeLookTimer >= lookDelay);
            if (!downBlocked && !states.lookingDown) _gazeLookTimer += Time.deltaTime;
        }
        else
        {
            states.lookingUp = false;
            states.lookingDown = false;
            _gazeLookTimer = 0f;
        }

        // CameraController reads lookingUp/lookingDown, while the animation
        // controller reads the *Anim variants. Keep those axes deliberately split.
        states.lookingUpAnim = false;
        states.lookingDownAnim = false;
    }

    private float PlayClipAndGetDuration(string clipName, float fallback)
    {
        var clip = FindClip(clipName);
        if (clip == null || !PlayClip(clipName))
        {
            if (!_warnedNoAnimation)
            {
                _warnedNoAnimation = true;
                Logger.LogWarning($"Animation '{clipName}' was not found/playable. Press F8 for candidates.");
            }
            return 0.01f;
        }
        return clip.fps > 0.01f ? Math.Max(0.01f, clip.Duration) : fallback;
    }

    private bool PlayClip(string clipName)
    {
        if (_animationController == null || FindClip(clipName) == null) return false;
        _animationController.PlayClipForced(clipName);
        return true;
    }

    private tk2dSpriteAnimationClip? FindClip(string name)
    {
        var animator = _animationController?.animator;
        return animator ? animator.GetClipByName(name) : null;
    }

    private static bool IsStandingPose(HeroController hero, bool requireIdleActorState, out string? reason)
    {
        reason = null;
        var states = Reflection.ReadMember(hero, "cState");
        if (states == null) { reason = "cState unavailable"; return false; }
        if (!Reflection.ReadBool(states, "onGround")) { reason = "not on ground"; return false; }
        // onGround + idle are authoritative for the pose. Do not also reject stale
        // aerial/directional sub-flags; the game can retain those for a frame after
        // an action, which made a visibly idle Hornet occasionally ignore B.
        string[] blockedFlags = { "dashing", "backDashing", "isSprinting", "isBackSprinting", "isBackScuttling", "attacking", "nailCharging", "casting", "focusing", "recoiling", "recoilFrozen", "dead", "hazardDeath", "hazardRespawning", "transitioning", "isToolThrowing", "parrying", "mantling", "isInCutsceneMovement", "isBinding", "needolinPlayingMemory", "evading", "whipLashing" };
        foreach (var flag in blockedFlags)
            if (Reflection.ReadBool(states, flag)) { reason = flag; return false; }

        var heroState = Reflection.ReadMember(hero, "hero_state")?.ToString();
        if (requireIdleActorState && !string.IsNullOrEmpty(heroState) && !heroState.Equals("idle", StringComparison.OrdinalIgnoreCase))
        { reason = $"hero_state={heroState}"; return false; }
        if (Math.Abs(Reflection.AsFloat(Reflection.ReadMember(hero, "move_input"), 0f)) > 0.01f)
        { reason = "movement input"; return false; }
        return true;
    }

    private void LogIgnoredInput(HeroController? hero, string reason)
    {
        if (!hero)
        {
            Logger.LogInfo($"Far gaze input ignored: {reason}.");
            return;
        }

        var states = Reflection.ReadMember(hero, "cState");
        var animator = hero.AnimCtrl ? hero.AnimCtrl.animator : null;
        Logger.LogInfo($"Far gaze input ignored: {reason}; hero_state={Reflection.ReadMember(hero, "hero_state")}; " +
            $"onGround={Reflection.ReadMember(states, "onGround")}; move_input={Reflection.ReadMember(hero, "move_input")}; " +
            $"clip={animator?.CurrentClip?.name ?? "<none>"}; animationControl={hero.HasAnimationControl}.");
    }

    private void DumpDiagnostics()
    {
        var hero = HeroController.instance;
        Logger.LogInfo("========== Far Gaze diagnostics ==========");
        Logger.LogInfo($"Scene={SceneManager.GetActiveScene().name}; state={_state}; keyboard={_gazeKey.Value}; controller={DescribeCurrentControllerBinding()}; mode={_inputMode.Value}; gazeLook={_gazeLookDirection}; {_blur.Describe()}");
        if (!hero) { Logger.LogInfo("HeroController.instance is null."); return; }
        IsStandingPose(hero, requireIdleActorState: true, out var reason);
        var states = Reflection.ReadMember(hero, "cState");
        Logger.LogInfo($"hero_state={Reflection.ReadMember(hero, "hero_state")}; standingReason={reason ?? "OK"}; onGround={Reflection.ReadMember(states, "onGround")}");
        Logger.LogInfo($"look: up={Reflection.ReadMember(states, "lookingUp")}, down={Reflection.ReadMember(states, "lookingDown")}, upAnim={Reflection.ReadMember(states, "lookingUpAnim")}, downAnim={Reflection.ReadMember(states, "lookingDownAnim")}, gazeDirection={_gazeLookDirection}");
        Logger.LogInfo($"move_input={Reflection.ReadMember(hero, "move_input")}; velocity={Reflection.ReadMember(hero, "current_velocity")}");

        var animCtrl = hero.AnimCtrl;
        var animator = animCtrl ? animCtrl.animator : null;
        Logger.LogInfo($"animationControl={hero.HasAnimationControl}; currentClip={animator?.CurrentClip?.name ?? "<none>"}; playing={animator?.Playing}");
        var matches = new List<string>();
        if (animator && animator.Library && animator.Library.clips != null)
        {
            foreach (var clip in animator.Library.clips)
            {
                var name = clip?.name;
                if (name != null && (name.IndexOf("BG", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Turn", StringComparison.OrdinalIgnoreCase) >= 0 || name.IndexOf("Challenge", StringComparison.OrdinalIgnoreCase) >= 0)) matches.Add(name);
            }
        }
        Logger.LogInfo($"Animation candidates ({matches.Count}): {string.Join(", ", matches)}");
        _blur.Dump(Logger);
        Logger.LogInfo("==========================================");
    }

    private void ForceReset(bool immediate)
    {
        ReleaseAnimationControl();
        ClearHeroReferences();
        _state = GazeState.Inactive;
        if (immediate) _blur.RestoreImmediately(); else _blur.SetLooking(false);
    }

    private void OnDestroy()
    {
        CancelControllerBindingCapture(showMessage: false);
        SceneManager.sceneLoaded -= OnSceneLoaded;
        ForceReset(true);
        _blur.Dispose();
        _harmony?.UnpatchSelf();
    }
}
