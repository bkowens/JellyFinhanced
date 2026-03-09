using System;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace Jellyfin.Database.Providers.MySql.ValueConverters
{
    /// <summary>
    /// Converts <see cref="DateTime"/> values to ensure a consistent <see cref="DateTimeKind"/>.
    /// </summary>
    public class DateTimeKindValueConverter : ValueConverter<DateTime, DateTime>
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="DateTimeKindValueConverter"/> class.
        /// </summary>
        /// <param name="kind">The <see cref="DateTimeKind"/> to apply.</param>
        /// <param name="mappingHints">Optional mapping hints.</param>
        public DateTimeKindValueConverter(DateTimeKind kind, ConverterMappingHints? mappingHints = null)
            : base(v => v.ToUniversalTime(), v => DateTime.SpecifyKind(v, kind), mappingHints)
        {
        }
    }
}
