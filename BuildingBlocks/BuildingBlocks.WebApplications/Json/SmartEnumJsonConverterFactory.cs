using System.Text.Json;
using System.Text.Json.Serialization;
using Ardalis.SmartEnum;
using Ardalis.SmartEnum.SystemTextJson;

namespace BuildingBlocks.WebApplications.Json;

/// <summary>
/// A JsonConverterFactory that automatically handles all SmartEnum types.
/// Serializes SmartEnums by their Name property (string).
/// </summary>
public class SmartEnumJsonConverterFactory : JsonConverterFactory
{
    public override bool CanConvert(Type typeToConvert)
    {
        return IsSmartEnum(typeToConvert, out _, out _);
    }

    public override JsonConverter? CreateConverter(Type typeToConvert, JsonSerializerOptions options)
    {
        if (!IsSmartEnum(typeToConvert, out var enumType, out var valueType))
            return null;

        // Create SmartEnumNameConverter<TEnum, TValue>
        var converterType = typeof(SmartEnumNameConverter<,>).MakeGenericType(enumType!, valueType!);
        return (JsonConverter?)Activator.CreateInstance(converterType);
    }

    private static bool IsSmartEnum(Type type, out Type? enumType, out Type? valueType)
    {
        enumType = null;
        valueType = null;

        // Walk up the inheritance chain to find SmartEnum<TEnum, TValue>
        var currentType = type;
        while (currentType != null && currentType != typeof(object))
        {
            if (currentType.IsGenericType)
            {
                var genericDef = currentType.GetGenericTypeDefinition();

                // Check for SmartEnum<TEnum, TValue>
                if (genericDef == typeof(SmartEnum<,>))
                {
                    var args = currentType.GetGenericArguments();
                    enumType = args[0];
                    valueType = args[1];
                    return true;
                }

                // Check for SmartEnum<TEnum> (which uses int as value type)
                if (genericDef == typeof(SmartEnum<>))
                {
                    enumType = currentType.GetGenericArguments()[0];
                    valueType = typeof(int);
                    return true;
                }
            }

            currentType = currentType.BaseType;
        }

        return false;
    }
}
