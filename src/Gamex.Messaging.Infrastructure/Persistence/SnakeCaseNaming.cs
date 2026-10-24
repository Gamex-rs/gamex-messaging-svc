using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Gamex.Messaging.Infrastructure.Persistence;

internal static class SnakeCaseNaming
{
    /// <summary>Renames columns, keys, foreign keys and indexes to snake_case. Table names are set explicitly in configurations.</summary>
    public static void ApplySnakeCaseNames(this ModelBuilder modelBuilder)
    {
        foreach (var entity in modelBuilder.Model.GetEntityTypes())
        {
            var table = entity.GetTableName();
            if (table is null)
            {
                continue;
            }

            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(ToSnakeCase(property.Name));
            }

            foreach (var key in entity.GetKeys())
            {
                key.SetName(key.IsPrimaryKey() ? $"pk_{table}" : $"ak_{table}_{Columns(key.Properties)}");
            }

            foreach (var foreignKey in entity.GetForeignKeys())
            {
                var principal = foreignKey.PrincipalEntityType.GetTableName();
                foreignKey.SetConstraintName($"fk_{table}_{principal}_{Columns(foreignKey.Properties)}");
            }

            foreach (var index in entity.GetIndexes())
            {
                index.SetDatabaseName($"{(index.IsUnique ? "ux" : "ix")}_{table}_{Columns(index.Properties)}");
            }
        }
    }

    private static string Columns(IEnumerable<Microsoft.EntityFrameworkCore.Metadata.IReadOnlyProperty> properties) =>
        string.Join('_', properties.Select(p => ToSnakeCase(p.Name)));

    internal static string ToSnakeCase(string name)
    {
        var builder = new StringBuilder(name.Length + 8);
        for (var i = 0; i < name.Length; i++)
        {
            var c = name[i];
            if (char.IsUpper(c))
            {
                var startsWord = i > 0 && (char.IsLower(name[i - 1]) || (i + 1 < name.Length && char.IsLower(name[i + 1])));
                if (startsWord && name[i - 1] != '_')
                {
                    builder.Append('_');
                }

                builder.Append(char.ToLowerInvariant(c));
            }
            else
            {
                builder.Append(c);
            }
        }

        return builder.ToString();
    }
}
