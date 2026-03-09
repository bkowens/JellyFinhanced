using System;
using Jellyfin.Database.Providers.MySql.ValueConverters;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Jellyfin.Database.Providers.MySql;

/// <summary>
/// Model builder extensions for MySQL.
/// </summary>
public static class ModelBuilderExtensions
{
    /// <summary>
    /// Applies a <see cref="ValueConverter"/> to all properties of a given type.
    /// </summary>
    /// <typeparam name="T">The CLR type to apply the converter to.</typeparam>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="converter">The value converter to apply.</param>
    /// <returns>The model builder for chaining.</returns>
    public static ModelBuilder UseValueConverterForType<T>(this ModelBuilder modelBuilder, ValueConverter converter)
    {
        var type = typeof(T);
        foreach (var entityType in modelBuilder.Model.GetEntityTypes())
        {
            foreach (var property in entityType.GetProperties())
            {
                if (property.ClrType == type)
                {
                    property.SetValueConverter(converter);
                }
            }
        }

        return modelBuilder;
    }

    /// <summary>
    /// Sets the default <see cref="DateTimeKind"/> for all <see cref="DateTime"/> properties in the model.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    /// <param name="kind">The kind to apply.</param>
    public static void SetDefaultDateTimeKind(this ModelBuilder modelBuilder, DateTimeKind kind)
    {
        modelBuilder.UseValueConverterForType<DateTime>(new DateTimeKindValueConverter(kind));
        modelBuilder.UseValueConverterForType<DateTime?>(new DateTimeKindValueConverter(kind));
    }
}
