using System;
using System.Reflection;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace FarGaze;

internal sealed class BlurTransition : IDisposable
{
    private ManualLogSource _log = null!;
    private LightBlur? _lightBlur;
    private LightBlurredBackground? _background;
    private float _amount = 1f;
    private float _target = 1f;
    private bool _scanned;
    private int _blurPlaneCount;
    private int _originalRenderTextureHeight;
    private int _currentRenderTextureHeight;
    private bool _highResolutionActive;
    private bool _useHighResolution = true;
    private int _maximumRenderTextureHeight = 2160;

    private static readonly MethodInfo? RefreshBackgroundTextureMethod =
        AccessTools.Method(typeof(LightBlurredBackground), "OnCameraAspectChanged");

    internal static BlurTransition? Instance { get; private set; }
    internal bool IsAvailable => _lightBlur && _lightBlur.enabled && _blurPlaneCount > 0;
    internal float Amount => _amount;
    internal float BlurSampleScale => _highResolutionActive && _originalRenderTextureHeight > 0
        ? (float)_currentRenderTextureHeight / _originalRenderTextureHeight
        : 1f;
    internal bool NeedsRenderOverride => _amount < 0.9999f || BlurSampleScale > 1.0001f;

    internal void Initialise(ManualLogSource log)
    {
        _log = log;
        Instance = this;
    }

    internal void ConfigureResolution(bool useHighResolution, int maximumHeight)
    {
        _useHighResolution = useHighResolution;
        _maximumRenderTextureHeight = Mathf.Clamp(maximumHeight, 720, 4320);
    }

    internal void ScanSceneIfNeeded()
    {
        if (!_scanned || !_lightBlur || _blurPlaneCount <= 0) ScanScene();
    }

    internal void ScanScene()
    {
        var keepHighResolution = _target < 0.5f;
        RestoreBackgroundResolution();
        _lightBlur = null;
        _background = null;
        _blurPlaneCount = 0;
        _originalRenderTextureHeight = 0;
        _currentRenderTextureHeight = 0;
        _scanned = true;

        try
        {
            foreach (var candidate in Resources.FindObjectsOfTypeAll<LightBlur>())
            {
                if (!candidate || !candidate.gameObject.scene.IsValid() || !candidate.gameObject.activeInHierarchy) continue;
                var cameras = candidate.GetComponentInParent<GameCameras>();
                if (!candidate.name.Equals("BlurCamera", StringComparison.OrdinalIgnoreCase) && cameras == null) continue;
                _lightBlur = candidate;
                _background = cameras ? cameras.GetComponent<LightBlurredBackground>() : null;
                if (_background)
                {
                    _originalRenderTextureHeight = Math.Max(1, _background.RenderTextureHeight);
                    _currentRenderTextureHeight = _originalRenderTextureHeight;
                }
                break;
            }
            _blurPlaneCount = BlurPlane.BlurPlaneCount;
            if (keepHighResolution) EnableHighResolution();
        }
        catch (Exception ex)
        {
            _log.LogDebug($"Background blur scan failed: {ex.Message}");
            _lightBlur = null;
            _background = null;
            _blurPlaneCount = 0;
        }
    }

    internal void SetLooking(bool looking)
    {
        ScanSceneIfNeeded();
        _target = looking ? 0f : 1f;
        if (looking) EnableHighResolution();
    }

    internal void Tick(float delta, float duration)
    {
        if (Math.Abs(_amount - _target) < 0.0001f)
        {
            _amount = _target;
            if (_target > 0.5f) RestoreBackgroundResolution();
            return;
        }
        _amount = Mathf.MoveTowards(_amount, _target, delta / Math.Max(0.01f, duration));
        if (_target > 0.5f && _amount >= 0.9999f) RestoreBackgroundResolution();
    }

    internal bool Owns(LightBlur candidate)
    {
        return _lightBlur && candidate == _lightBlur && _lightBlur.enabled && _blurPlaneCount > 0;
    }

    internal void RestoreImmediately()
    {
        _amount = _target = 1f;
        RestoreBackgroundResolution();
    }

    internal string Describe()
    {
        var blur = _lightBlur ? $"LightBlur '{_lightBlur.name}' enabled={_lightBlur.enabled} passes={_lightBlur.BlurPassCount}" : "no LightBlur";
        var resolution = _originalRenderTextureHeight > 0
            ? $", background-height={_originalRenderTextureHeight}->{_currentRenderTextureHeight}"
            : string.Empty;
        return $"{blur}, blur-planes={_blurPlaneCount}, amount={_amount:0.00}{resolution}";
    }

    internal void Dump(ManualLogSource log)
    {
        ScanScene();
        log.LogInfo($"Blur: {Describe()}");
        for (var i = 0; i < BlurPlane.BlurPlaneCount; i++)
        {
            var plane = BlurPlane.GetBlurPlane(i);
            var renderer = plane ? plane.GetComponent<MeshRenderer>() : null;
            log.LogInfo($"BlurPlane[{i}]: name={plane?.name}, z={plane?.PlaneZ}, rendererEnabled={renderer?.enabled}, material={renderer?.sharedMaterial?.name}, shader={renderer?.sharedMaterial?.shader?.name}");
        }
    }

    public void Dispose()
    {
        RestoreImmediately();
        _lightBlur = null;
        _background = null;
        _blurPlaneCount = 0;
        if (Instance == this) Instance = null;
    }

    private void EnableHighResolution()
    {
        if (!_useHighResolution || !_background || !_lightBlur || _highResolutionActive || RefreshBackgroundTextureMethod == null) return;

        try
        {
            var sceneCamera = _lightBlur.transform.parent ? _lightBlur.transform.parent.GetComponent<Camera>() : null;
            var viewportHeight = sceneCamera ? sceneCamera.pixelHeight : Screen.height;
            var desiredHeight = Mathf.Clamp(viewportHeight, _originalRenderTextureHeight, _maximumRenderTextureHeight);
            if (desiredHeight <= _originalRenderTextureHeight) return;

            _currentRenderTextureHeight = desiredHeight;
            _highResolutionActive = true;
            _background.RenderTextureHeight = desiredHeight;
            RefreshBackgroundTextureMethod.Invoke(_background, new object[] { ForceCameraAspect.CurrentViewportAspect });
        }
        catch (Exception ex)
        {
            _log.LogWarning($"Could not enable high-resolution background: {ex.GetBaseException().Message}");
            RestoreBackgroundResolution();
        }
    }

    private void RestoreBackgroundResolution()
    {
        if (!_highResolutionActive)
        {
            _currentRenderTextureHeight = _originalRenderTextureHeight;
            return;
        }

        try
        {
            if (_background && RefreshBackgroundTextureMethod != null)
            {
                _background.RenderTextureHeight = _originalRenderTextureHeight;
                RefreshBackgroundTextureMethod.Invoke(_background, new object[] { ForceCameraAspect.CurrentViewportAspect });
            }
        }
        catch (Exception ex)
        {
            _log.LogDebug($"Could not restore background resolution: {ex.GetBaseException().Message}");
        }
        finally
        {
            _currentRenderTextureHeight = _originalRenderTextureHeight;
            _highResolutionActive = false;
        }
    }
}
