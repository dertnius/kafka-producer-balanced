using System;
using System.Collections.Generic;
using System.Dynamic;
using System.Linq;
using System.Reflection;
using MyDotNetApp.Data.Attributes;

namespace MyDotNetApp.Data.Helpers
{
    /// <summary>
    /// Helper class for extracting key and metadata information from entities
    /// </summary>
    public static class KeyHelper
    {
        /// <summary>
        /// Gets the database table name for an entity
        /// </summary>
        public static string GetTableName<T>() where T : class
        {
            var tableAttr = typeof(T).GetCustomAttribute<TableAttribute>();
            return tableAttr?.Name ?? typeof(T).Name;
        }

        /// <summary>
        /// Gets all properties marked with [Key] attribute
        /// </summary>
        public static PropertyInfo[] GetKeyProperties<T>() where T : class
        {
            return typeof(T).GetProperties()
                .Where(p => p.GetCustomAttribute<KeyAttribute>() != null)
                .ToArray();
        }

        /// <summary>
        /// Gets all properties NOT marked with [Key] attribute
        /// </summary>
        public static PropertyInfo[] GetNonKeyProperties<T>() where T : class
        {
            var keyNames = GetKeyProperties<T>().Select(p => p.Name).ToHashSet();
            return typeof(T).GetProperties()
                .Where(p => !keyNames.Contains(p.Name))
                .ToArray();
        }

        /// <summary>
        /// Checks if the entity has a composite key (more than one key property)
        /// </summary>
        public static bool IsCompositeKey<T>() where T : class
        {
            return GetKeyProperties<T>().Length > 1;
        }

        /// <summary>
        /// Gets the key value(s) from an entity instance
        /// </summary>
        public static object GetKeyValue<T>(T entity) where T : class
        {
            if (entity == null)
                throw new ArgumentNullException(nameof(entity));

            var keyProps = GetKeyProperties<T>();
            if (keyProps.Length == 0)
                throw new InvalidOperationException($"No key properties found on {typeof(T).Name}");

            if (keyProps.Length == 1)
            {
                return keyProps[0].GetValue(entity)!;
            }

            // Composite key - return as dynamic object
            var dict = new ExpandoObject() as IDictionary<string, object>;
            foreach (var prop in keyProps)
            {
                dict[prop.Name] = prop.GetValue(entity)!;
            }
            return dict;
        }

        /// <summary>
        /// Validates that key count matches entity key properties
        /// </summary>
        public static void ValidateKeyCount<T>(int providedKeyCount) where T : class
        {
            var expectedCount = GetKeyProperties<T>().Length;
            if (providedKeyCount != expectedCount)
            {
                throw new ArgumentException(
                    $"Expected {expectedCount} key(s) for {typeof(T).Name}, got {providedKeyCount}");
            }
        }
    }
}
