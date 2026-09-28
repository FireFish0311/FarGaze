using System;
using System.Globalization;
using System.Reflection;

namespace FarGaze;

internal static class Reflection
{
    private const BindingFlags Flags = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.IgnoreCase;

    internal static object? ReadMember(object? target, string name)
    {
        if (target == null) return null;
        try
        {
            var type = target.GetType();
            var field = type.GetField(name, Flags);
            if (field != null) return field.GetValue(target);
            return type.GetProperty(name, Flags)?.GetValue(target, null);
        }
        catch { return null; }
    }

    internal static bool WriteMember(object? target, string name, object value)
    {
        if (target == null) return false;
        try
        {
            var type = target.GetType();
            var field = type.GetField(name, Flags);
            if (field != null)
            {
                field.SetValue(target, Convert.ChangeType(value, field.FieldType, CultureInfo.InvariantCulture));
                return true;
            }
            var property = type.GetProperty(name, Flags);
            if (property?.CanWrite == true)
            {
                property.SetValue(target, Convert.ChangeType(value, property.PropertyType, CultureInfo.InvariantCulture), null);
                return true;
            }
        }
        catch { }
        return false;
    }

    internal static bool TryInvoke(object? target, string methodName, params object[] args)
    {
        if (target == null) return false;
        try
        {
            foreach (var method in target.GetType().GetMethods(Flags))
            {
                if (!method.Name.Equals(methodName, StringComparison.OrdinalIgnoreCase)) continue;
                var parameters = method.GetParameters();
                if (parameters.Length != args.Length) continue;
                var compatible = true;
                for (var i = 0; i < args.Length; i++)
                {
                    if (args[i] != null && !parameters[i].ParameterType.IsInstanceOfType(args[i]))
                    {
                        compatible = false;
                        break;
                    }
                }
                if (compatible)
                {
                    method.Invoke(target, args);
                    return true;
                }
            }
        }
        catch { return false; }
        return false;
    }

    internal static bool ReadBool(object? target, string name) => ReadMember(target, name) is bool value && value;

    internal static float AsFloat(object? value, float fallback)
    {
        if (value == null) return fallback;
        try { return Convert.ToSingle(value, CultureInfo.InvariantCulture); }
        catch { return fallback; }
    }
}
