using Microsoft.EntityFrameworkCore;
using BoardingHouse.Domain.Common;
using BoardingHouse.Domain.Entities;
using BoardingHouse.Domain.Entities.Enums;
using BoardingHouse.Infrastructure.Repositories;
using BoardingHouse.IntegrationTests.Fixtures;

namespace BoardingHouse.IntegrationTests.Repositories;

public class PaymentRepositoryIntegrationTests(PostgresContainerFixture fixture)
    : IClassFixture<PostgresContainerFixture>, IAsyncLifetime
{
    public Task InitializeAsync() => fixture.ResetAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    [Fact]
    public async Task AddAsync_OnlyStagesThePayment_UntilSaveChanges()
    {
        var invoiceId = await SeedInvoiceAsync();
        await using var context = fixture.CreateContext();
        var repository = new PaymentRepository(context);
        var payment = NewPayment(invoiceId);

        await repository.AddAsync(payment);

        Assert.Equal(EntityState.Added, context.Entry(payment).State);
        await using (var readContext = fixture.CreateContext())
        {
            Assert.False(await readContext.Payments.AnyAsync());
        }

        await context.SaveChangesAsync();

        await using var afterSave = fixture.CreateContext();
        var saved = await afterSave.Payments.SingleAsync();
        Assert.Equal(payment.Id, saved.Id);
        Assert.Equal(invoiceId, saved.InvoiceId);
        Assert.Equal(PaymentStatus.Pending, saved.Status);
    }

    // Step 2 has no factory yet (Invoice.RecordPayment comes in step 3): build the row the way EF does
    private static Payment NewPayment(Guid invoiceId) => TestEntities.New<Payment>(new
    {
        InvoiceId = (Guid?)invoiceId,
        Amount = 500_000m,
        Method = PaymentMethod.Cash,
        PaymentDate = DateTimeOffset.UtcNow,
        CreatedBy = SentinelActors.System
    });

    private async Task<Guid> SeedInvoiceAsync()
    {
        await using var context = fixture.CreateContext();
        return (await TestInvoices.SeedAsync(context)).InvoiceId;
    }
}
