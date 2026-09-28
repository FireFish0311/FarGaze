using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using GlobalEnums;
using UnityEngine;

namespace FarGaze;

internal static class ControllerPromptAssets
{
    private const string ResourcePrefix = "FarGaze.Assets.ControllerPrompts.";
    private static readonly Dictionary<string, Sprite> Cache = new(StringComparer.OrdinalIgnoreCase);

    internal static Sprite? Disconnected => Load("disconnected");

    internal static Sprite? For(GamepadType gamepadType, ControllerBinding binding)
    {
        var platform = gamepadType switch
        {
            GamepadType.PS4 or GamepadType.PS3_WIN => "ps4",
            GamepadType.PS5 => "ps5",
            GamepadType.XBOX_SERIES_X => "xbox_series",
            GamepadType.XBOX_ONE or GamepadType.XBOX_360 => "xbox_one",
            GamepadType.SWITCH_JOYCON_DUAL or GamepadType.SWITCH_PRO_CONTROLLER or
                GamepadType.SWITCH2_JOYCON_DUAL or GamepadType.SWITCH2_PRO_CONTROLLER => "switch",
            _ => null
        };
        if (platform == null) return null;

        // The CC0 pack deliberately omits Sony's four trademarked face symbols.
        // Those controls fall back to Silksong's already-loaded runtime sprites.
        if ((platform == "ps4" || platform == "ps5") &&
            binding is ControllerBinding.FaceBottom or ControllerBinding.FaceRight or
                ControllerBinding.FaceLeft or ControllerBinding.FaceTop)
            return null;

        var control = binding switch
        {
            ControllerBinding.LeftStick => "left_stick",
            ControllerBinding.RightStick => "right_stick",
            ControllerBinding.FaceBottom => "face_bottom",
            ControllerBinding.FaceRight => "face_right",
            ControllerBinding.FaceLeft => "face_left",
            ControllerBinding.FaceTop => "face_top",
            ControllerBinding.LeftShoulder => "l1",
            ControllerBinding.RightShoulder => "r1",
            ControllerBinding.LeftTrigger => "l2",
            ControllerBinding.RightTrigger => "r2",
            ControllerBinding.DPadUp => "dpad_up",
            ControllerBinding.DPadDown => "dpad_down",
            ControllerBinding.DPadLeft => "dpad_left",
            ControllerBinding.DPadRight => "dpad_right",
            ControllerBinding.PrimaryMenu => "primary",
            ControllerBinding.SecondaryMenu => "secondary",
            ControllerBinding.TouchPad => "touchpad",
            _ => null
        };
        return control == null ? null : Load($"{platform}_{control}");
    }

    private static Sprite? Load(string assetName)
    {
        if (Cache.TryGetValue(assetName, out var cached)) return cached;

        var assembly = Assembly.GetExecutingAssembly();
        using var stream = assembly.GetManifestResourceStream($"{ResourcePrefix}{assetName}.png");
        if (stream == null) return null;

        var bytes = new byte[stream.Length];
        var offset = 0;
        while (offset < bytes.Length)
        {
            var read = stream.Read(bytes, offset, bytes.Length - offset);
            if (read <= 0) return null;
            offset += read;
        }

        var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false)
        {
            name = $"FarGaze.{assetName}",
            filterMode = FilterMode.Bilinear,
            wrapMode = TextureWrapMode.Clamp
        };
        if (!ImageConversion.LoadImage(texture, bytes, true))
        {
            UnityEngine.Object.Destroy(texture);
            return null;
        }

        var sprite = Sprite.Create(texture, new Rect(0f, 0f, texture.width, texture.height),
            new Vector2(0.5f, 0.5f), 100f);
        sprite.name = $"FarGaze.{assetName}";
        Cache[assetName] = sprite;
        return sprite;
    }
}
