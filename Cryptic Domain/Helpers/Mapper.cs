using System.Reflection;
using Cryptic_Domain.Database.Attributes;
using Cryptic_Domain.Interfaces.Database;
using Npgsql;

namespace Cryptic_Domain.Helpers;

public static class Mapper
{
    private static readonly Dictionary<Type, Dictionary<PropertyInfo, ColumnAttribute>> TypeMappingCache =
        new Dictionary<Type, Dictionary<PropertyInfo, ColumnAttribute>>();

    public static T Map<T>(this NpgsqlDataReader? reader) where T : IDatabaseTable
    {
        return MapAsync<T>(reader).GetAwaiter().GetResult();
    }

    public static async Task<T> MapAsync<T>(this NpgsqlDataReader? reader) where T : IDatabaseTable
    {
        var entityType = typeof(T);

        if (!TypeMappingCache.TryGetValue(entityType, out var entityMapping))
        {
            entityMapping = CacheTypeMapping(entityType);
        }

        var newEntity = Activator.CreateInstance(entityType);

        await MapDataReaderToEntityAsync(reader, newEntity, entityMapping);

        return (T)newEntity;
    }

    private static Dictionary<PropertyInfo, ColumnAttribute> CacheTypeMapping(Type entityType)
    {
        var columnAttributeType = typeof(ColumnAttribute);
        var entityProps = entityType.GetProperties();
        var entityMapping = new Dictionary<PropertyInfo, ColumnAttribute>();

        foreach (var prop in entityProps)
        {
            var columnInfo = (ColumnAttribute?)prop.GetCustomAttribute(columnAttributeType);
            if (columnInfo != null)
            {
                entityMapping[prop] = columnInfo;
            }
        }

        TypeMappingCache[entityType] = entityMapping;

        return entityMapping;
    }

    private static async Task MapDataReaderToEntityAsync(NpgsqlDataReader? reader, object newEntity,
        Dictionary<PropertyInfo, ColumnAttribute> entityMapping)
    {
        if (reader == null)
            return;

        var columnNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < reader.FieldCount; i++)
        {
            columnNames.Add(reader.GetName(i));
        }

        foreach (var kvp in entityMapping)
        {
            var prop = kvp.Key;
            var columnInfo = kvp.Value;
            var columnName = columnInfo.Name;
            var propType = prop.PropertyType;

            if (columnNames.Contains(columnName))
            {
                object? safeValue = null;
                var nullableType = Nullable.GetUnderlyingType(propType);
                var ordinal = reader.GetOrdinal(columnName);

                if (!await reader.IsDBNullAsync(ordinal))
                {
                    if (propType.IsArray && propType.GetElementType() == typeof(int?))
                    {
                        safeValue = await reader.GetFieldValueAsync<int?[]>(ordinal);
                    }
                    else
                    {
                        var value = await reader.GetFieldValueAsync<object>(ordinal);

                        if (propType.IsEnum)
                        {
                            propType = typeof(int);
                        }

                        var targetType = nullableType ?? propType;
                        safeValue = Convert.ChangeType(value, targetType);
                    }
                }

                if (safeValue == null)
                {
                    if (nullableType != null || !propType.IsValueType)
                    {
                        prop.SetValue(newEntity, null);
                    }
                    else
                    {
                        prop.SetValue(newEntity, Activator.CreateInstance(propType));
                    }
                }
                else
                {
                    prop.SetValue(newEntity, safeValue);
                }
            }
        }
    }
}