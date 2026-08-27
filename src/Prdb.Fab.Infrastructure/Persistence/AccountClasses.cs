using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

using Prdb.Fab.Core.Catalogue;

namespace Prdb.Fab.Infrastructure.Persistence;

/// <summary>
/// How a table says what becomes of it when the prdb key belongs to a different
/// account.
/// </summary>
/// <remarks>
/// ADR 0033 writes the account cut as a property of each table rather than as
/// prose, so that changing the key is a list of deletes readable off the schema.
/// It is kept on the model, which is what makes the rule enforceable: a test
/// walks every entity type and fails over one that says nothing, so a table
/// added later has to answer rather than inherit an answer by omission.
/// </remarks>
public static class AccountClasses
{
    public const string Annotation = "Fab:AccountClass";

    public static EntityTypeBuilder<TEntity> Declares<TEntity>(
        this EntityTypeBuilder<TEntity> entity,
        AccountClass accountClass)
        where TEntity : class
    {
        entity.Metadata.SetAnnotation(Annotation, accountClass.ToString());

        return entity;
    }

    /// <summary>
    /// What <paramref name="entity"/> declared, or null if it declared nothing.
    /// </summary>
    public static AccountClass? DeclaredBy(IReadOnlyEntityType entity) =>
        entity.FindAnnotation(Annotation)?.Value is string declared
        && Enum.TryParse<AccountClass>(declared, out var accountClass)
            ? accountClass
            : null;
}
