using Ardalis.SmartEnum;

namespace BuildingBlocks.Enumerations;

/// <summary>
/// Base class for SmartEnums with int value.
/// Provides IsInEnum for FluentValidation: .Must(MyEnum.IsInEnum)
/// </summary>
public abstract class SmartEnumBase<TEnum> : SmartEnum<TEnum>
    where TEnum : SmartEnum<TEnum>
{
    protected SmartEnumBase(string name, int value) : base(name, value) { }

    public static bool IsInEnum(string name) => TryFromName(name, out _);
}

/// <summary>
/// Base class for SmartEnums with custom value type.
/// Provides IsInEnum for FluentValidation: .Must(MyEnum.IsInEnum)
/// </summary>
public abstract class SmartEnumBase<TEnum, TValue> : SmartEnum<TEnum, TValue>
    where TEnum : SmartEnum<TEnum, TValue>
    where TValue : IEquatable<TValue>, IComparable<TValue>
{
    protected SmartEnumBase(string name, TValue value) : base(name, value) { }

    public static bool IsInEnum(string name) => TryFromName(name, out _);
}
