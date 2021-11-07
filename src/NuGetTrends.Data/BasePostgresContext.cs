using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;
using Npgsql;
using Npgsql.NameTranslation;
using System.Text.RegularExpressions;
using Microsoft.EntityFrameworkCore.Metadata.Internal;

namespace NuGetTrends.Data
{
    // https://github.com/npgsql/Npgsql.EntityFrameworkCore.PostgreSQL/issues/21
    public abstract class BasePostgresContext : DbContext
    {
        private static readonly Regex KeysRegex = new Regex("^(PK|FK|IX)_", RegexOptions.Compiled);

        protected BasePostgresContext(DbContextOptions options)
            : base(options)
        { }

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
#pragma warning disable 618
                    // https://github.com/dotnet/efcore/issues/23301#issuecomment-727003676
                    // If you have a model for 3.1 and you haven't changed it to use TPT, then you should be okay calling the implementation above in 5.0.
                    // TODO: Need to figure out how to get it given the info in scope
                    var storedObjectId = StoreObjectIdentifier.Create(/* ???? */);
                    property.SetColumnName(ConvertGeneralToSnake(mapper, property.GetColumnName(storedObjectId)!));
#pragma warning restore 618
                    break;
                case IMutableKey primaryKey:
                    primaryKey.SetName(ConvertKeyToSnake(mapper, primaryKey.GetName()!));
                    break;
                case IMutableForeignKey foreignKey:
                    foreignKey.SetConstraintName(ConvertKeyToSnake(mapper, foreignKey.GetConstraintName()!));
                    break;
                case IMutableIndex indexKey:
                    indexKey.SetDatabaseName(ConvertKeyToSnake(mapper, indexKey.GetDatabaseName()));
                    break;
                default:
                    throw new NotImplementedException("Unexpected type was provided to snake case converter");
            }
        }

        private string ConvertKeyToSnake(INpgsqlNameTranslator mapper, string keyName) =>
            ConvertGeneralToSnake(mapper, KeysRegex.Replace(keyName, match => match.Value.ToLower()));

        private string ConvertGeneralToSnake(INpgsqlNameTranslator mapper, string entityName) =>
            mapper.TranslateMemberName(ModifyNameBeforeConvertion(mapper, entityName));

        protected virtual string ModifyNameBeforeConvertion(INpgsqlNameTranslator mapper, string entityName) => entityName;
    }
}
