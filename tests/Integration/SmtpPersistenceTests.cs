using System.Data.Common;
using CarbonFootprint.Infrastructure.Identity;
using CarbonFootprint.Infrastructure.Persistence;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Options;

namespace CarbonFootprint.Integration.Tests;

public sealed class SmtpPersistenceTests
{
    [Fact]
    public async Task PreviouslySavedOrganizationDestinationIsRevalidatedAtSendTime()
    {
        var scope = new Scope(Guid.NewGuid());
        await using var db = CreateDb(scope);
        db.Organizations.Add(new OrganizationRecord { Id = scope.Id, Name = "SMTP egress fixture" });
        db.OrganizationMailSettings.Add(new OrganizationMailSettingsRecord
        {
            Id = Guid.NewGuid(),
            OrganizationId = scope.Id,
            Host = "127.0.0.1",
            Port = 587,
            EnableSsl = true,
            FromAddress = "sender@example.test",
            FromName = "SMTP fixture"
        });
        await db.SaveChangesAsync();
        try
        {
            var options = new MailOptions { Host = "8.8.8.8", Port = 587, EnableSsl = true };
            var sender = CreateSender(options, db, scope);
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => sender.SendTestMessageAsync("recipient@example.test"));
            Assert.Contains("政策拒絕", error.Message);
        }
        finally
        {
            db.OrganizationMailSettings.RemoveRange(db.OrganizationMailSettings);
            db.Organizations.RemoveRange(db.Organizations.Where(item => item.Id == scope.Id));
            await db.SaveChangesAsync();
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task TotalDeadlineAndCancellationIncludeDatabaseSettingsRead(bool cancel)
    {
        var scope = new Scope(Guid.NewGuid());
        var interceptor = new StalledSettingsRead();
        await using var db = CreateDb(scope, interceptor);
        var options = new MailOptions { Host = "8.8.8.8", Port = 587, EnableSsl = true, TimeoutSeconds = 1 };
        using var cancellation = new CancellationTokenSource();
        var send = CreateSender(options, db, scope).SendTestMessageAsync("recipient@example.test", cancellation.Token);
        await interceptor.Reached.Task.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.True(interceptor.Token.CanBeCanceled);
        if (cancel)
        {
            cancellation.Cancel();
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => send.WaitAsync(TimeSpan.FromSeconds(5)));
        }
        else
        {
            var error = await Assert.ThrowsAsync<InvalidOperationException>(() => send.WaitAsync(TimeSpan.FromSeconds(5)));
            Assert.Contains("逾時", error.Message);
        }
    }

    private static CarbonFootprintDbContext CreateDb(IOrganizationScope scope, IInterceptor? interceptor = null)
    {
        var connection = Environment.GetEnvironmentVariable("CARBON_TEST_DB_CONNECTION")
            ?? throw new InvalidOperationException("Integration test 需要 CARBON_TEST_DB_CONNECTION。");
        var builder = new DbContextOptionsBuilder<CarbonFootprintDbContext>().UseNpgsql(connection).UseSnakeCaseNamingConvention();
        if (interceptor is not null) builder.AddInterceptors(interceptor);
        return new CarbonFootprintDbContext(builder.Options, scope);
    }

    private static SmtpEmailSender CreateSender(MailOptions options, CarbonFootprintDbContext db, IOrganizationScope scope) =>
        new(Options.Create(options), db, scope, new EphemeralDataProtectionProvider(), new SmtpEgressPolicy(options, false), new HttpContextAccessor());

    private sealed record Scope(Guid Id) : IOrganizationScope
    {
        public Guid? OrganizationId => Id;
    }

    private sealed class StalledSettingsRead : DbCommandInterceptor
    {
        public TaskCompletionSource Reached { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public CancellationToken Token { get; private set; }

        public override async ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(
            DbCommand command, CommandEventData eventData, InterceptionResult<DbDataReader> result, CancellationToken cancellationToken = default)
        {
            Token = cancellationToken;
            Reached.TrySetResult();
            await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
            return result;
        }
    }
}
