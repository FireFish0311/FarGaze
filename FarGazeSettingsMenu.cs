using System;
using System.Collections.Generic;
using System.Reflection;
using BepInEx.Configuration;
using GlobalEnums;
using InControl;
using Silksong.ModMenu.Elements;
using Silksong.ModMenu.Models;
using Silksong.ModMenu.Plugin;
using Silksong.ModMenu.Screens;
using UnityEngine;
using UnityEngine.UI;

namespace FarGaze;

internal enum FarGazeInputMode
{
    Press,
    Hold
}

internal enum ControllerBinding
{
    None,
    LeftStick,
    RightStick,
    FaceBottom,
    FaceRight,
    FaceLeft,
    FaceTop,
    LeftShoulder,
    RightShoulder,
    LeftTrigger,
    RightTrigger,
    DPadUp,
    DPadDown,
    DPadLeft,
    DPadRight,
    PrimaryMenu,
    SecondaryMenu,
    TouchPad
}

public sealed partial class FarGazePlugin
{
    private static readonly MethodInfo? KeyCodeToInControlKey = typeof(KeyBindElement).Assembly
        .GetType("Silksong.ModMenu.Internal.KeyCodeUtil")?
        .GetMethod("ToKey", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(KeyCode) }, null);

    private readonly DeviceBindingSourceListener _controllerBindingListener = new();
    private readonly BindingListenOptions _controllerListenOptions = new()
    {
        IncludeControllers = true,
        IncludeUnknownControllers = true,
        IncludeNonStandardControls = false,
        IncludeMouseButtons = false,
        IncludeKeys = false,
        IncludeModifiersAsFirstClassKeys = false
    };

    private bool _capturingControllerBinding;
    private GamepadType _capturingGamepadType = GamepadType.NONE;
    private readonly Dictionary<GamepadType, ConfigEntry<ControllerBinding>> _controllerBindings = new();
    private TextButton? _controllerBindingButton;
    private Image? _controllerPromptImage;
    private Text? _controllerPromptText;
    private Text? _keyboardBindingLabel;
    private Text? _modeLabel;
    private GamepadType _displayedGamepadType = GamepadType.NONE;
    private ControllerBinding _displayedControllerBinding = ControllerBinding.None;
    private TextLabel? _bindingStatusLabel;
    private string? _lastKeyboardConflict;
    private string? _lastControllerConflict;

    public LocalizedText ModMenuName() => "远眺";

    public AbstractMenuScreen BuildCustomMenu()
    {
        var screen = new SimpleMenuScreen("远眺");

        var keyboardModel = new ValidatedConfigModel<KeyCode>(_gazeKey, ValidateKeyboardCandidate);
        var keyboard = new KeyBindElement("键盘按键", keyboardModel);
        keyboard.OnDispose += keyboardModel.Dispose;
        _keyboardBindingLabel = keyboard.LabelText;
        screen.Add(keyboard);

        var controller = new TextButton("手柄按键");
        controller.OnSubmit = () => BeginControllerBindingCapture(controller);
        CreateControllerBindingVisual(controller);
        controller.OnDispose += () =>
        {
            if (_controllerBindingButton == controller)
            {
                CancelControllerBindingCapture(showMessage: false);
                _controllerBindingButton = null;
                _controllerPromptImage = null;
                _controllerPromptText = null;
            }
        };
        _controllerBindingButton = controller;
        screen.Add(controller);

        var modeModel = ChoiceModels.ForNamedValues(new List<(FarGazeInputMode, string)>
        {
            (FarGazeInputMode.Press, "点按"),
            (FarGazeInputMode.Hold, "长按")
        });
        var mode = new ChoiceElement<FarGazeInputMode>("按键模式", modeModel);
        mode.SynchronizeWith(_inputMode);
        _modeLabel = mode.LabelText;
        screen.Add(mode);

        var status = new TextLabel("");
        status.Visibility.VisibleSelf = false;
        _bindingStatusLabel = status;
        status.OnDispose += () =>
        {
            if (_bindingStatusLabel == status)
            {
                _bindingStatusLabel = null;
                _keyboardBindingLabel = null;
                _modeLabel = null;
            }
        };
        screen.Add(status);
        RefreshControllerBindingUi(force: true);
        SynchronizeBindingLabelStyles();
        ShowCurrentConflictIfAny();
        return screen;
    }

    private void CreateControllerBindingVisual(TextButton button)
    {
        button.DescriptionText.gameObject.SetActive(false);
        button.ButtonText.text = "手柄按键";
        button.ButtonText.alignment = TextAnchor.MiddleLeft;
        var labelRect = button.ButtonText.rectTransform;
        labelRect.anchorMin = new Vector2(0f, 0f);
        labelRect.anchorMax = new Vector2(0.55f, 1f);
        labelRect.offsetMin = new Vector2(36f, 0f);
        labelRect.offsetMax = Vector2.zero;

        var promptTextObject = new GameObject("FarGaze Controller Binding Text", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        promptTextObject.transform.SetParent(button.MenuButton.transform, false);
        _controllerPromptText = promptTextObject.GetComponent<Text>();
        _controllerPromptText.font = button.ButtonText.font;
        _controllerPromptText.fontStyle = button.ButtonText.fontStyle;
        _controllerPromptText.color = button.ButtonText.color;
        _controllerPromptText.material = button.ButtonText.material;
        _controllerPromptText.supportRichText = button.ButtonText.supportRichText;
        _controllerPromptText.raycastTarget = false;
        _controllerPromptText.text = string.Empty;
        _controllerPromptText.alignment = TextAnchor.MiddleRight;
        var promptTextRect = _controllerPromptText.rectTransform;
        promptTextRect.anchorMin = new Vector2(0.55f, 0f);
        promptTextRect.anchorMax = new Vector2(1f, 1f);
        promptTextRect.offsetMin = Vector2.zero;
        promptTextRect.offsetMax = new Vector2(-38f, 0f);

        var promptImageObject = new GameObject("FarGaze Controller Binding Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        promptImageObject.transform.SetParent(button.MenuButton.transform, false);
        _controllerPromptImage = promptImageObject.GetComponent<Image>();
        _controllerPromptImage.preserveAspect = true;
        _controllerPromptImage.raycastTarget = false;
        var promptImageRect = _controllerPromptImage.rectTransform;
        promptImageRect.anchorMin = new Vector2(1f, 0.5f);
        promptImageRect.anchorMax = new Vector2(1f, 0.5f);
        promptImageRect.pivot = new Vector2(0.5f, 0.5f);
        promptImageRect.anchoredPosition = new Vector2(-92f, 0f);
        promptImageRect.sizeDelta = new Vector2(72f, 72f);
    }

    private bool ValidateKeyboardCandidate(KeyCode candidate, out string message)
    {
        if (candidate == KeyCode.None)
        {
            message = "键盘按键已设为“未绑定”。";
            SetBindingStatus(message, false);
            return true;
        }

        if (TryFindKeyboardConflict(candidate, out var action))
        {
            message = $"无法绑定 {candidate}：它已用于“{action}”。原绑定保持不变。";
            SetBindingStatus(message, true);
            return false;
        }

        message = $"键盘按键已绑定为 {candidate}。";
        SetBindingStatus(message, false);
        return true;
    }

    private void BeginControllerBindingCapture(TextButton button)
    {
        if (_capturingControllerBinding) return;

        var gamepadType = CurrentConnectedGamepadType();
        if (gamepadType == GamepadType.NONE)
        {
            SetBindingStatus("未检测到已连接的手柄，手柄绑定没有改变。", true);
            return;
        }

        if (_state != GazeState.Inactive) ForceReset(false);
        _capturingControllerBinding = true;
        _capturingGamepadType = gamepadType;
        _controllerBindingButton = button;
        _controllerBindingListener.Reset();
        if (_controllerPromptImage) _controllerPromptImage.gameObject.SetActive(false);
        if (_controllerPromptText) _controllerPromptText.text = "…";
        SetBindingStatus(string.Empty, false);

        var handler = InputHandler.SilentInstance;
        if (handler) handler.StopUIInput();
    }

    private bool ProcessControllerBindingCapture()
    {
        if (!_capturingControllerBinding) return false;

        var handler = InputHandler.SilentInstance;
        if (Input.GetKeyDown(KeyCode.Escape) || (handler && handler.inputActions != null && handler.inputActions.MenuCancel.WasPressed))
        {
            CancelControllerBindingCapture(showMessage: true);
            return true;
        }

        var device = InputManager.ActiveDevice;
        if (device == null || device == InputDevice.Null || !device.IsAttached ||
            CurrentConnectedGamepadType() != _capturingGamepadType)
        {
            EndControllerBindingCapture();
            SetBindingStatus("手柄已断开，绑定没有改变。", true);
            return true;
        }

        BindingSource source;
        try
        {
            source = _controllerBindingListener.Listen(_controllerListenOptions, device);
        }
        catch (Exception exception)
        {
            Logger.LogError($"Controller binding capture failed: {exception}");
            EndControllerBindingCapture();
            SetBindingStatus("手柄按键捕获失败；请查看 BepInEx 日志。", true);
            return true;
        }

        if (source is not DeviceBindingSource deviceBinding) return true;

        var control = deviceBinding.Control;
        if (!TryConvertCapturedControl(control, out var binding))
        {
            EndControllerBindingCapture();
            SetBindingStatus($"无法绑定 {control}：仅支持标准手柄按钮，不支持摇杆方向或轴。", true);
            return true;
        }

        if (TryFindControllerConflict(control, out var action))
        {
            EndControllerBindingCapture();
            SetBindingStatus($"无法绑定 {ControllerBindingDisplay(binding)}：它已用于“{action}”。原绑定保持不变。", true);
            return true;
        }

        var entry = ControllerBindingEntry(_capturingGamepadType);
        if (entry == null)
        {
            EndControllerBindingCapture();
            SetBindingStatus("无法识别当前手柄类型，绑定没有改变。", true);
            return true;
        }

        entry.Value = binding;
        EndControllerBindingCapture();
        SetBindingStatus(string.Empty, false);
        return true;
    }

    private void CancelControllerBindingCapture(bool showMessage)
    {
        if (!_capturingControllerBinding) return;
        EndControllerBindingCapture();
        if (showMessage) SetBindingStatus(string.Empty, false);
    }

    private void EndControllerBindingCapture()
    {
        _capturingControllerBinding = false;
        _capturingGamepadType = GamepadType.NONE;
        var handler = InputHandler.SilentInstance;
        if (handler) handler.StartUIInput();
        RefreshControllerBindingUi(force: true);
    }

    private GamepadType CurrentConnectedGamepadType()
    {
        var device = InputManager.ActiveDevice;
        if (device == null || device == InputDevice.Null || !device.IsAttached) return GamepadType.NONE;

        var handler = InputHandler.SilentInstance;
        if (!handler) return GamepadType.UNKNOWN;
        return handler.activeGamepadType == GamepadType.NONE ? GamepadType.UNKNOWN : handler.activeGamepadType;
    }

    private ConfigEntry<ControllerBinding>? ControllerBindingEntry(GamepadType gamepadType)
    {
        if (gamepadType == GamepadType.NONE) return null;
        if (_controllerBindings.TryGetValue(gamepadType, out var entry)) return entry;

        entry = Config.Bind("Controller Bindings", gamepadType.ToString(), ControllerBinding.LeftStick,
            $"{gamepadType} 手柄的远眺按键。首次使用默认为按下左摇杆。");
        _controllerBindings.Add(gamepadType, entry);
        return entry;
    }

    private ControllerBinding GetCurrentControllerBinding()
    {
        var gamepadType = CurrentConnectedGamepadType();
        return ControllerBindingEntry(gamepadType)?.Value ?? ControllerBinding.None;
    }

    private string DescribeCurrentControllerBinding()
    {
        var gamepadType = CurrentConnectedGamepadType();
        return gamepadType == GamepadType.NONE
            ? "Disconnected"
            : $"{gamepadType}:{ControllerBindingDisplay(GetCurrentControllerBinding())}";
    }

    private void RefreshControllerBindingUi(bool force = false)
    {
        if (_controllerBindingButton == null || _capturingControllerBinding) return;

        SynchronizeBindingLabelStyles();
        var gamepadType = CurrentConnectedGamepadType();
        var binding = gamepadType == GamepadType.NONE
            ? ControllerBinding.None
            : ControllerBindingEntry(gamepadType)?.Value ?? ControllerBinding.LeftStick;
        if (!force && gamepadType == _displayedGamepadType && binding == _displayedControllerBinding) return;

        _displayedGamepadType = gamepadType;
        _displayedControllerBinding = binding;
        _controllerBindingButton.Interactable = gamepadType != GamepadType.NONE;

        if (gamepadType == GamepadType.NONE)
        {
            SetControllerPrompt(ControllerPromptAssets.Disconnected, "未连接手柄", disconnected: true);
            ShowCurrentConflictIfAny();
            return;
        }

        var sprite = ControllerPromptAssets.For(gamepadType, binding) ?? GetNativeControllerSprite(binding);
        SetControllerPrompt(sprite, sprite ? string.Empty : ControllerBindingDisplay(binding), disconnected: false);
        ShowCurrentConflictIfAny();
    }

    private void SynchronizeBindingLabelStyles()
    {
        if (!_modeLabel) return;
        if (_keyboardBindingLabel)
        {
            _keyboardBindingLabel.font = _modeLabel.font;
            _keyboardBindingLabel.fontSize = _modeLabel.fontSize;
            _keyboardBindingLabel.resizeTextForBestFit = _modeLabel.resizeTextForBestFit;
        }
        if (_controllerBindingButton?.ButtonText)
        {
            _controllerBindingButton.ButtonText.font = _modeLabel.font;
            _controllerBindingButton.ButtonText.fontSize = _modeLabel.fontSize;
            _controllerBindingButton.ButtonText.resizeTextForBestFit = _modeLabel.resizeTextForBestFit;
        }
        if (_controllerPromptText)
        {
            _controllerPromptText.font = _modeLabel.font;
            _controllerPromptText.fontSize = _modeLabel.fontSize;
        }
    }

    private void SetControllerPrompt(Sprite? sprite, string text, bool disconnected)
    {
        if (_controllerPromptImage)
        {
            _controllerPromptImage.sprite = sprite;
            _controllerPromptImage.gameObject.SetActive(sprite);
            _controllerPromptImage.rectTransform.anchoredPosition =
                disconnected ? new Vector2(-205f, 0f) : new Vector2(-92f, 0f);
        }
        if (_controllerPromptText) _controllerPromptText.text = text;
    }

    private static Sprite? GetNativeControllerSprite(ControllerBinding binding)
    {
        var control = ResolveControllerControl(binding);
        if (control == InputControlType.None) return null;
        try
        {
            var skins = GameManager.instance?.ui?.uiButtonSkins;
            if (!skins) return null;
            var method = skins.GetType().GetMethod("GetButtonSkinFor",
                BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
                null, new[] { typeof(string) }, null);
            var skin = method?.Invoke(skins, new object[] { control.ToString() });
            return Reflection.ReadMember(skin, "sprite") as Sprite;
        }
        catch
        {
            return null;
        }
    }

    private static string ControllerBindingDisplay(ControllerBinding binding)
    {
        return binding switch
        {
            ControllerBinding.None => "未绑定",
            ControllerBinding.LeftStick => "L3 / LS / 左摇杆",
            ControllerBinding.RightStick => "R3 / RS / 右摇杆",
            ControllerBinding.FaceBottom => "× / A / B（下）",
            ControllerBinding.FaceRight => "○ / B / A（右）",
            ControllerBinding.FaceLeft => "□ / X / Y（左）",
            ControllerBinding.FaceTop => "△ / Y / X（上）",
            ControllerBinding.LeftShoulder => "L1 / LB / L",
            ControllerBinding.RightShoulder => "R1 / RB / R",
            ControllerBinding.LeftTrigger => "L2 / LT / ZL",
            ControllerBinding.RightTrigger => "R2 / RT / ZR",
            ControllerBinding.DPadUp => "方向键上",
            ControllerBinding.DPadDown => "方向键下",
            ControllerBinding.DPadLeft => "方向键左",
            ControllerBinding.DPadRight => "方向键右",
            ControllerBinding.PrimaryMenu => "Options / Menu / +",
            ControllerBinding.SecondaryMenu => "Share / View / −",
            ControllerBinding.TouchPad => "PS4/PS5 触摸板",
            _ => binding.ToString()
        };
    }

    private static bool TryConvertCapturedControl(InputControlType control, out ControllerBinding binding)
    {
        binding = control switch
        {
            InputControlType.LeftStickButton => ControllerBinding.LeftStick,
            InputControlType.RightStickButton => ControllerBinding.RightStick,
            InputControlType.Action1 => ControllerBinding.FaceBottom,
            InputControlType.Action2 => ControllerBinding.FaceRight,
            InputControlType.Action3 => ControllerBinding.FaceLeft,
            InputControlType.Action4 => ControllerBinding.FaceTop,
            InputControlType.LeftBumper => ControllerBinding.LeftShoulder,
            InputControlType.RightBumper => ControllerBinding.RightShoulder,
            InputControlType.LeftTrigger => ControllerBinding.LeftTrigger,
            InputControlType.RightTrigger => ControllerBinding.RightTrigger,
            InputControlType.DPadUp => ControllerBinding.DPadUp,
            InputControlType.DPadDown => ControllerBinding.DPadDown,
            InputControlType.DPadLeft => ControllerBinding.DPadLeft,
            InputControlType.DPadRight => ControllerBinding.DPadRight,
            InputControlType.Start or InputControlType.Menu or InputControlType.Options or InputControlType.Plus or InputControlType.Command => ControllerBinding.PrimaryMenu,
            InputControlType.Select or InputControlType.Back or InputControlType.View or InputControlType.Share or InputControlType.Create or InputControlType.Minus => ControllerBinding.SecondaryMenu,
            InputControlType.TouchPadButton => ControllerBinding.TouchPad,
            _ => ControllerBinding.None
        };
        return binding != ControllerBinding.None;
    }

    private bool TryFindKeyboardConflict(KeyCode keyCode, out string actionName)
    {
        actionName = string.Empty;
        if (keyCode == KeyCode.None) return false;

        var handler = InputHandler.SilentInstance;
        if (!handler || handler.inputActions == null || handler.MappableKeyboardActions == null) return false;
        if (!TryConvertKeyCode(keyCode, out var key)) return false;

        foreach (var action in handler.MappableKeyboardActions)
        {
            if (action == null) continue;
            foreach (var source in action.Bindings)
            {
                if (source is not KeyBindingSource keySource) continue;
                var combo = keySource.Control;
                for (var index = 0; index < combo.IncludeCount; index++)
                {
                    if (combo.GetInclude(index) != key) continue;
                    actionName = FriendlyActionName(action.Name);
                    return true;
                }
            }
        }

        return false;
    }

    private bool TryFindControllerConflict(InputControlType control, out string actionName)
    {
        actionName = string.Empty;
        if (control == InputControlType.None) return false;

        var handler = InputHandler.SilentInstance;
        if (!handler || handler.inputActions == null || handler.MappableControllerActions == null) return false;

        foreach (var action in handler.MappableControllerActions)
        {
            if (ActionUsesControl(action, control))
            {
                actionName = FriendlyActionName(action.Name);
                return true;
            }
        }

        if (ActionUsesControl(handler.inputActions.Pause, control))
        {
            actionName = FriendlyActionName(handler.inputActions.Pause.Name);
            return true;
        }

        return false;
    }

    private static bool ActionUsesControl(PlayerAction? action, InputControlType control)
    {
        if (action == null) return false;
        foreach (var source in action.Bindings)
        {
            if (source is DeviceBindingSource deviceSource && deviceSource.Control == control) return true;
        }
        return false;
    }

    private static bool TryConvertKeyCode(KeyCode keyCode, out Key key)
    {
        key = Key.None;
        if (KeyCodeToInControlKey == null) return false;
        try
        {
            if (KeyCodeToInControlKey.Invoke(null, new object[] { keyCode }) is Key converted)
            {
                key = converted;
                return converted != Key.None;
            }
        }
        catch
        {
            // Runtime conflict checking is a safety net. The key-bind element has
            // already filtered unsupported key codes before this conversion.
        }
        return false;
    }

    private static string FriendlyActionName(string name)
    {
        return name switch
        {
            "Jump" => "跳跃",
            "Attack" => "攻击",
            "Dash" => "冲刺",
            "Cast" => "施法",
            "Super Dash" => "蓄力冲刺",
            "Dream Nail" => "织针能力",
            "Quick Map" => "快捷地图",
            "Quick Cast" => "快捷施法",
            "Taunt" => "挑战",
            "openInventory" => "打开物品栏",
            "openInventoryMap" => "地图页",
            "openInventoryJournal" => "日志页",
            "openInventoryTools" => "工具页",
            "openInventoryQuests" => "任务页",
            "Up" => "向上",
            "Down" => "向下",
            "Left" => "向左",
            "Right" => "向右",
            "Pause" => "暂停",
            _ => name
        };
    }

    private void ShowCurrentConflictIfAny()
    {
        if (TryFindKeyboardConflict(_gazeKey.Value, out var keyboardAction))
        {
            SetBindingStatus($"当前键盘绑定与“{keyboardAction}”重复，远眺已暂时禁用该键。请重新绑定。", true);
            return;
        }

        var control = ResolveControllerControl(GetCurrentControllerBinding());
        if (TryFindControllerConflict(control, out var controllerAction))
        {
            SetBindingStatus($"当前手柄绑定与“{controllerAction}”重复，远眺已暂时禁用该键。请重新绑定。", true);
            return;
        }

        SetBindingStatus(string.Empty, false);
    }

    private void SetBindingStatus(string message, bool warning)
    {
        if (_bindingStatusLabel?.Text)
        {
            _bindingStatusLabel.Text.text = warning ? message : string.Empty;
            _bindingStatusLabel.Text.color = warning ? new Color(1f, 0.55f, 0.35f) : Color.white;
            _bindingStatusLabel.Visibility.VisibleSelf = warning && !string.IsNullOrWhiteSpace(message);
        }
        if (warning) Logger.LogWarning(message);
    }

    private static InputControlType ResolveControllerControl(ControllerBinding binding)
    {
        switch (binding)
        {
            case ControllerBinding.LeftStick: return InputControlType.LeftStickButton;
            case ControllerBinding.RightStick: return InputControlType.RightStickButton;
            case ControllerBinding.FaceBottom: return InputControlType.Action1;
            case ControllerBinding.FaceRight: return InputControlType.Action2;
            case ControllerBinding.FaceLeft: return InputControlType.Action3;
            case ControllerBinding.FaceTop: return InputControlType.Action4;
            case ControllerBinding.LeftShoulder: return InputControlType.LeftBumper;
            case ControllerBinding.RightShoulder: return InputControlType.RightBumper;
            case ControllerBinding.LeftTrigger: return InputControlType.LeftTrigger;
            case ControllerBinding.RightTrigger: return InputControlType.RightTrigger;
            case ControllerBinding.DPadUp: return InputControlType.DPadUp;
            case ControllerBinding.DPadDown: return InputControlType.DPadDown;
            case ControllerBinding.DPadLeft: return InputControlType.DPadLeft;
            case ControllerBinding.DPadRight: return InputControlType.DPadRight;
            case ControllerBinding.PrimaryMenu: return ResolvePrimaryMenuButton();
            case ControllerBinding.SecondaryMenu: return ResolveSecondaryMenuButton();
            case ControllerBinding.TouchPad: return InputControlType.TouchPadButton;
            default: return InputControlType.None;
        }
    }

    private static InputControlType ResolvePrimaryMenuButton()
    {
        var handler = InputHandler.SilentInstance;
        if (!handler) return InputControlType.Start;
        switch (handler.activeGamepadType)
        {
            case GamepadType.PS4:
            case GamepadType.PS5: return InputControlType.Options;
            case GamepadType.XBOX_ONE:
            case GamepadType.XBOX_SERIES_X: return InputControlType.Menu;
            case GamepadType.SWITCH_JOYCON_DUAL:
            case GamepadType.SWITCH_PRO_CONTROLLER:
            case GamepadType.SWITCH2_JOYCON_DUAL:
            case GamepadType.SWITCH2_PRO_CONTROLLER: return InputControlType.Plus;
            default: return InputControlType.Start;
        }
    }

    private static InputControlType ResolveSecondaryMenuButton()
    {
        var handler = InputHandler.SilentInstance;
        if (!handler) return InputControlType.Select;
        switch (handler.activeGamepadType)
        {
            case GamepadType.PS4: return InputControlType.Share;
            case GamepadType.PS5: return InputControlType.Create;
            case GamepadType.XBOX_ONE:
            case GamepadType.XBOX_SERIES_X: return InputControlType.View;
            case GamepadType.SWITCH_JOYCON_DUAL:
            case GamepadType.SWITCH_PRO_CONTROLLER:
            case GamepadType.SWITCH2_JOYCON_DUAL:
            case GamepadType.SWITCH2_PRO_CONTROLLER: return InputControlType.Minus;
            default: return InputControlType.Select;
        }
    }

    private sealed class ValidatedConfigModel<T> : AbstractValueModel<T>, IDisposable
    {
        private readonly ConfigEntry<T> _entry;
        private readonly Validator _validator;

        internal delegate bool Validator(T candidate, out string message);

        internal ValidatedConfigModel(ConfigEntry<T> entry, Validator validator)
        {
            _entry = entry;
            _validator = validator;
            _entry.SettingChanged += OnSettingChanged;
        }

        public override T GetValue() => _entry.Value;

        public override bool SetValue(T value)
        {
            if (EqualityComparer<T>.Default.Equals(_entry.Value, value)) return true;
            if (!_validator(value, out _)) return true;
            _entry.Value = value;
            return true;
        }

        private void OnSettingChanged(object sender, EventArgs args) => InvokeOnValueChanged();

        public void Dispose() => _entry.SettingChanged -= OnSettingChanged;
    }
}
