namespace BoardingHouse.UnitTests.Common;

internal static class TestEntities
{
    public static T New<T>(object values) where T : class =>
        Set((T)Activator.CreateInstance(typeof(T), nonPublic: true)!, values);

    // values: an anonymous object, e.g. new { IsActive = false, Room = room }
    public static T Set<T>(T entity, object values) where T : class
    {
        foreach (var value in values.GetType().GetProperties())
        {
            var property = typeof(T).GetProperty(value.Name)
                ?? throw new ArgumentException($"{typeof(T).Name} has no property '{value.Name}'", nameof(values));
            property.SetValue(entity, ConvertTo(value.GetValue(values), property.PropertyType));
        }

        return entity;
    }

    // Anonymous objects type numeric literals as int: DefaultUnitPrice = 100_000 must still fill a decimal
    private static object? ConvertTo(object? value, Type type)
    {
        var target = Nullable.GetUnderlyingType(type) ?? type;
        return value is IConvertible && !target.IsInstanceOfType(value) && !target.IsEnum
            ? Convert.ChangeType(value, target)
            : value;
    }
}
