using Microsoft.AspNetCore.Identity;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Monetail.Api.Authentication;
using Monetail.Api.Persistence;
using Xunit;

namespace Monetail.Api.Tests;

/// <summary>支払方法マスタの物理制約、所有者不変性、再初期化時の保存値保持を確認する。</summary>
public sealed class PaymentMethodSchemaTests
{
    [Fact]
    public async Task PaymentMethodsEnforceOwnerNamePeriodAndLedgerForeignKeys()
    {
        using var db = new AuthDatabaseFixture();
        await db.InitializeAsync();
        var accountId = await AdminIdAsync(db);
        using var connection = db.OpenForeignKeyConnection();
        var ledger = NewId();
        var otherLedger = NewId();
        var first = NewId();
        var second = NewId();
        var outsider = NewId();
        SeedLedger(connection, ledger, accountId);
        SeedLedger(connection, otherLedger, accountId);
        SeedPerson(connection, ledger, first, "父");
        SeedPerson(connection, ledger, second, "母");
        SeedPerson(connection, otherLedger, outsider, "別家計簿の人");

        InsertPayment(connection, ledger, first, NewId(), "現金");
        InsertPayment(connection, ledger, second, NewId(), "現金"); // Different owners may use the same name.
        InsertPayment(connection, otherLedger, outsider, NewId(), "現金");

        AssertConstraint(() => InsertPayment(connection, ledger, first, NewId(), "現金"));
        AssertConstraint(() => InsertPayment(connection, ledger, outsider, NewId(), "別家計簿の支払方法"));
        AssertConstraint(() => InsertPayment(connection, ledger, "00000000000000000000000000000000", NewId(), "所有者不在"));
        AssertConstraint(() => InsertPayment(connection, ledger, first, NewId(), "負数順位", order: -1));
        AssertConstraint(() => InsertPayment(connection, ledger, first, NewId(), "逆転期間",
            from: "2026-12-31", to: "2026-01-01"));
        AssertConstraint(() => Execute(connection, "DELETE FROM ledger_people WHERE ledger_id=@ledger AND person_id=@person",
            ("@ledger", ledger), ("@person", first)));

        Assert.Equal(3L, Convert.ToInt64(Scalar(connection, "SELECT COUNT(*) FROM ledger_payment_methods")));
        Assert.Equal(1L, Convert.ToInt64(Scalar(connection,
            "SELECT COUNT(*) FROM sqlite_schema WHERE type='trigger' AND name='trg_ledger_payment_methods_owner_immutable'")));
    }

    [Fact]
    public async Task OwnerChangeIsRejectedButNormalEditsAndDeleteRecreateWork()
    {
        using var db = new AuthDatabaseFixture();
        await db.InitializeAsync();
        var accountId = await AdminIdAsync(db);
        using var connection = db.OpenForeignKeyConnection();
        var ledger = NewId();
        var first = NewId();
        var second = NewId();
        var original = NewId();
        SeedLedger(connection, ledger, accountId);
        SeedPerson(connection, ledger, first, "父");
        SeedPerson(connection, ledger, second, "母");
        InsertPayment(connection, ledger, first, original, "旧名");

        AssertConstraint(() => Execute(connection,
            "UPDATE ledger_payment_methods SET owner_person_id=@owner WHERE ledger_id=@ledger AND payment_method_id=@id",
            ("@owner", second), ("@ledger", ledger), ("@id", original)));

        Execute(connection, "UPDATE ledger_payment_methods SET owner_person_id=@owner WHERE ledger_id=@ledger AND payment_method_id=@id",
            ("@owner", first), ("@ledger", ledger), ("@id", original));
        Execute(connection, "UPDATE ledger_payment_methods SET name='更新名', valid_to='2026-12-31', display_order=3 WHERE ledger_id=@ledger AND payment_method_id=@id",
            ("@ledger", ledger), ("@id", original));
        Assert.Equal("更新名", Scalar(connection,
            "SELECT name FROM ledger_payment_methods WHERE ledger_id=@ledger AND payment_method_id=@id",
            ("@ledger", ledger), ("@id", original)));

        using (var tx = connection.BeginTransaction())
        {
            using var valid = connection.CreateCommand();
            valid.Transaction = tx;
            valid.CommandText = "UPDATE ledger_payment_methods SET name='ロールバック対象' WHERE ledger_id=@ledger AND payment_method_id=@id";
            valid.Parameters.AddWithValue("@ledger", ledger);
            valid.Parameters.AddWithValue("@id", original);
            valid.ExecuteNonQuery();

            using var invalid = connection.CreateCommand();
            invalid.Transaction = tx;
            invalid.CommandText = "UPDATE ledger_payment_methods SET owner_person_id=@owner WHERE ledger_id=@ledger AND payment_method_id=@id";
            invalid.Parameters.AddWithValue("@owner", second);
            invalid.Parameters.AddWithValue("@ledger", ledger);
            invalid.Parameters.AddWithValue("@id", original);
            AssertConstraint(() => invalid.ExecuteNonQuery());
            tx.Rollback(); // RAISE(ABORT) cancels only the failed SQL statement.
        }
        Assert.Equal("更新名", Scalar(connection,
            "SELECT name FROM ledger_payment_methods WHERE ledger_id=@ledger AND payment_method_id=@id",
            ("@ledger", ledger), ("@id", original)));

        Execute(connection, "DELETE FROM ledger_payment_methods WHERE ledger_id=@ledger AND payment_method_id=@id",
            ("@ledger", ledger), ("@id", original));
        var recreated = NewId();
        InsertPayment(connection, ledger, second, recreated, "更新名");
        Assert.NotEqual(original, recreated);
        Assert.Equal(second, Scalar(connection,
            "SELECT owner_person_id FROM ledger_payment_methods WHERE ledger_id=@ledger AND payment_method_id=@id",
            ("@ledger", ledger), ("@id", recreated)));
    }

    [Theory]
    [InlineData(false)] [InlineData(true)]
    public async Task ReinitializationPreservesPeoplePaymentVersionsAndEveryStoredValue(bool hasPayments)
    {
        await using var app = await LoginTestApp.CreateAsync();
        using var connection = app.Database.OpenForeignKeyConnection();
        var ledger = NewId(); SeedLedger(connection, ledger, app.UserId);
        var a = NewId(); var b = NewId(); SeedPerson(connection, ledger, a, "父"); SeedPerson(connection, ledger, b, "母");
        Execute(connection, "UPDATE ledger_people SET valid_from='2020-04-01',valid_to='2026-09-30',display_order=7 WHERE person_id=@id", ("@id",a));
        Assert.NotEqual(Scalar(connection,"SELECT payment_order_revision FROM ledger_people WHERE person_id=@id",("@id",a)),
            Scalar(connection,"SELECT payment_order_revision FROM ledger_people WHERE person_id=@id",("@id",b)));
        if (hasPayments) InsertPayment(connection,ledger,a,NewId(),"現金");
        var before = app.Database.Snapshot(app.Database.Tables());
        await app.Database.InitializeAsync(); await app.Database.InitializeAsync();
        Assert.Equal(before,app.Database.Snapshot(app.Database.Tables()));
        if (hasPayments) AssertConstraint(() => Execute(connection,"UPDATE ledger_people SET person_id='missing' WHERE person_id=@id",("@id",a)));
    }

    private static async Task<string> AdminIdAsync(AuthDatabaseFixture db)
    {
        await using var services = db.CreateServices();
        return (await services.GetRequiredService<AdminProvisioningService>().InitializeAdminAsync())!.AuthUserId;
    }

    private static string NewId() => Guid.NewGuid().ToString("N");

    private static void SeedLedger(SqliteConnection connection, string id, string accountId) =>
        Execute(connection, """
            INSERT INTO ledgers(id,name,created_by_auth_user_id,created_at_utc,revision,people_order_revision,category_order_revision)
            VALUES(@ledger,'家計簿',@account,'2026-11-05',@revision,@order_revision,@category_revision)
            """, ("@ledger", id), ("@account", accountId), ("@revision", NewId()), ("@order_revision", NewId()), ("@category_revision", NewId()));

    private static void SeedPerson(SqliteConnection connection, string ledger, string id, string name) =>
        Execute(connection, """
            INSERT INTO ledger_people(ledger_id,person_id,name,display_order,revision,payment_order_revision)
            VALUES(@ledger,@id,@name,0,@revision,@payment_revision)
            """, ("@ledger", ledger), ("@id", id), ("@name", name),
            ("@revision", NewId()), ("@payment_revision", NewId()));

    private static void InsertPayment(SqliteConnection connection, string ledger, string owner, string id,
        string name, int order = 0, string? from = null, string? to = null) =>
        Execute(connection, """
            INSERT INTO ledger_payment_methods
                (ledger_id,payment_method_id,owner_person_id,name,display_order,valid_from,valid_to,revision)
            VALUES(@ledger,@id,@owner,@name,@order,@from,@to,@revision)
            """,
            ("@ledger", ledger), ("@id", id), ("@owner", owner), ("@name", name),
            ("@order", order), ("@from", from), ("@to", to), ("@revision", NewId()));

    private static void Execute(SqliteConnection connection, string sql, params (string, object?)[] parameters)
    {
        using var command = Command(connection, sql, parameters);
        command.ExecuteNonQuery();
    }

    private static object? Scalar(SqliteConnection connection, string sql, params (string, object?)[] parameters)
    {
        using var command = Command(connection, sql, parameters);
        return command.ExecuteScalar();
    }

    private static SqliteCommand Command(SqliteConnection connection, string sql, params (string, object?)[] parameters)
    {
        var command = connection.CreateCommand();
        command.CommandText = sql;
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    private static void AssertConstraint(Action action) =>
        Assert.Equal(19, Assert.Throws<SqliteException>(action).SqliteErrorCode);
}
