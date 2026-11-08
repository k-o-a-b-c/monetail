using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Storage;

namespace Monetail.Api.Persistence;

/// <summary>現在のEFモデルの表・制約・索引・初期値と、モデル外のトリガーを一括作成する。</summary>
internal static class DatabaseSchema
{
    public static async Task CreateAsync(ApiDbContext context, CancellationToken token)
    {
        // 呼び出し元のSQLiteトランザクションを使用し、HasDataの初期値も同時に保存する。
        await context.GetService<IRelationalDatabaseCreator>().CreateTablesAsync(token);
        await context.Database.ExecuteSqlRawAsync(Triggers, token);
    }

    internal const string Triggers = """
        CREATE TRIGGER trg_ledger_payment_methods_owner_immutable
        BEFORE UPDATE OF owner_person_id ON ledger_payment_methods
        FOR EACH ROW
        WHEN OLD.owner_person_id IS NOT NEW.owner_person_id
        BEGIN
            SELECT RAISE(ABORT, 'payment_method_owner_immutable');
        END;

        CREATE TRIGGER trg_tax_rate_history_overlap_insert
        BEFORE INSERT ON tax_rate_history
        FOR EACH ROW
        WHEN EXISTS (
            SELECT 1 FROM tax_rate_history AS existing
            WHERE existing.tax_category_code = NEW.tax_category_code
              AND (existing.valid_to IS NULL OR NEW.valid_from <= existing.valid_to)
              AND (NEW.valid_to IS NULL OR existing.valid_from <= NEW.valid_to)
        )
        BEGIN
            SELECT RAISE(ABORT, 'tax_rate_period_overlap');
        END;

        CREATE TRIGGER trg_tax_rate_history_overlap_update
        BEFORE UPDATE ON tax_rate_history
        FOR EACH ROW
        WHEN EXISTS (
            SELECT 1 FROM tax_rate_history AS existing
            WHERE existing.tax_category_code = NEW.tax_category_code AND existing.id <> OLD.id
              AND (existing.valid_to IS NULL OR NEW.valid_from <= existing.valid_to)
              AND (NEW.valid_to IS NULL OR existing.valid_from <= NEW.valid_to)
        )
        BEGIN
            SELECT RAISE(ABORT, 'tax_rate_period_overlap');
        END;
        """;
}
