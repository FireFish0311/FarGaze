using HarmonyLib;
using UnityEngine;

namespace FarGaze;

// LightBlur normally uses a fixed one-pixel sampling offset on every pass.
// Scaling that offset continuously to zero preserves the game's composition
// while smoothly transitioning from blurred to sharp background rendering.
[HarmonyPatch(typeof(LightBlur), "OnRenderImage")]
internal static class LightBlurPatch
{
    private static readonly int BlurInfoId = Shader.PropertyToID("_BlurInfo");

    private static bool Prefix(
        LightBlur __instance,
        RenderTexture source,
        RenderTexture destination,
        Material ___blurMaterial,
        bool ___effectIsSupported)
    {
        var transition = BlurTransition.Instance;
        if (transition == null || !transition.Owns(__instance) || !transition.NeedsRenderOverride || !___effectIsSupported || !___blurMaterial)
        {
            return true;
        }

        var amount = transition.Amount;
        if (amount <= 0.0001f)
        {
            Graphics.Blit(source, destination);
            return false;
        }

        var passCount = __instance.BlurPassCount;
        if (passCount <= 0)
        {
            Graphics.Blit(source, destination);
            return false;
        }

        var temporary1 = RenderTexture.GetTemporary(source.width, source.height, 32, source.format);
        var temporary2 = RenderTexture.GetTemporary(source.width, source.height, 32, source.format);
        try
        {
            var input = source;
            var useFirst = true;
            // A full-resolution source needs proportionally wider sampling to look
            // identical to the game's original low-resolution blur at amount=1.
            var sampleAmount = amount * transition.BlurSampleScale;
            var blurInfo = new Vector4(sampleAmount / source.width, sampleAmount / source.height, 0f, 0f);
            for (var i = 0; i < passCount; i++)
            {
                RenderTexture output;
                if (i == passCount - 1) output = destination;
                else if (useFirst) { output = temporary1; useFirst = false; }
                else { output = temporary2; useFirst = true; }

                ___blurMaterial.SetVector(BlurInfoId, blurInfo);
                input.filterMode = FilterMode.Bilinear;
                Graphics.Blit(input, output, ___blurMaterial, i % 2);
                input = output;
            }
        }
        finally
        {
            RenderTexture.ReleaseTemporary(temporary1);
            RenderTexture.ReleaseTemporary(temporary2);
        }
        return false;
    }
}
