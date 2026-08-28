using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Prdb.Fab.Infrastructure.Persistence;

public enum ExportClass
{
    NotExported,
    Exported,
}

public static class ExportClassDeclarations
{
    public const string Annotation = "PrdbFab:ExportClass";

    public static EntityTypeBuilder<TEntity> Declares<TEntity>(
        this EntityTypeBuilder<TEntity> entity,
        ExportClass exportClass)
        where TEntity : class
    {
        entity.HasAnnotation(Annotation, exportClass);
        return entity;
    }
}
