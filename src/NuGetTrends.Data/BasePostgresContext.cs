using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using Npgsql.NameTranslation;
using System.Text.RegularExpressions;

namespace NuGetTrends.Data;

// https://github.com/npgsql/Npgsql.EntityFrameworkCore.PostgreSQL/issues/21
public abstract class BasePostgresContext(DbContextOptions options) : DbContext(options)
{
    private static readonly Regex KeysRegex = new("^(PK|FK|IX)_", RegexOptions.Compiled);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);
        FixSnakeCaseNames(modelBuilder);
    }

    private void FixSnakeCaseNames(ModelBuilder modelBuilder)
    {
        var mapper = new NpgsqlSnakeCaseNameTranslator();
        foreach (var table in modelBuilder.Model.GetEntityTypes())
        {
            ConvertToSnake(mapper, table);
            foreach (var property in table.GetProperties())
            {
                ConvertToSnake(mapper, property);
            }

            foreach (var primaryKey in table.GetKeys())
            {
                ConvertToSnake(mapper, primaryKey);
            }

            foreach (var foreignKey in table.GetForeignKeys())
            {
                ConvertToSnake(mapper, foreignKey);
            }

            foreach (var indexKey in table.GetIndexes())
            {
                ConvertToSnake(mapper, indexKey);
            }
        }
    }

    private void ConvertToSnake(INpgsqlNameTranslator mapper, object entity)
    {
        switch (entity)
        {
            case IMutableEntityType table:
                table.SetTableName(ConvertGeneralToSnake(mapper, table.GetTableName()!));
                break;
            case IMutableProperty property:
                var columnName = property.GetColumnName(
                    StoreObjectIdentifier.Table(property.DeclaringType.GetTableName()!));
                property.SetColumnName(ConvertGeneralToSnake(mapper, columnName!));
                break;
            case IMutableKey primaryKey:
                primaryKey.SetName(ConvertKeyToSnake(mapper, primaryKey.GetName()!));
                break;
            case IMutableForeignKey foreignKey:
                foreignKey.SetConstraintName(ConvertKeyToSnake(mapper, foreignKey.GetConstraintName()!));
                break;
            case IMutableIndex indexKey:
                var dbName = indexKey.GetDatabaseName();
                if (string.IsNullOrWhiteSpace(dbName))
                {
                    throw new InvalidOperationException("Can't adjust casing, missing DB name");
                }
                indexKey.SetDatabaseName(ConvertKeyToSnake(mapper, dbName));
                break;
            default:
                throw new NotImplementedException($"Unexpected type '{entity.GetType().FullName}' was provided to snake case converter.");
        }
    }

    private string ConvertKeyToSnake(INpgsqlNameTranslator mapper, string keyName) =>
        ConvertGeneralToSnake(mapper, KeysRegex.Replace(keyName, match => match.Value.ToLower()));

    private string ConvertGeneralToSnake(INpgsqlNameTranslator mapper, string entityName) =>
        mapper.TranslateMemberName(ModifyNameBeforeConversion(mapper, entityName));

    private string ModifyNameBeforeConversion(INpgsqlNameTranslator mapper, string entityName) => entityName;
}
