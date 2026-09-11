using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using OrderFlow.Notifications.Domain.Entities;
using OrderFlow.Notifications.Infrastructure.Persistence;
using OrderFlow.Notifications.Infrastructure.Persistence.Repositories;
using Xunit;

namespace OrderFlow.Notifications.Tests.Infrastructure.Persistence;

public class ProcessedMessageRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<NotificationsDbContext> _dbContextOptions;

    public ProcessedMessageRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _dbContextOptions = new DbContextOptionsBuilder<NotificationsDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new NotificationsDbContext(_dbContextOptions);
        context.Database.EnsureCreated();
    }

    [Fact]
    public async Task ExistsAsync_ShouldReturnFalse_WhenEventNotProcessed()
    {
        // Arrange
        await using var context = new NotificationsDbContext(_dbContextOptions);
        var repository = new ProcessedMessageRepository(context);

        // Act
        var exists = await repository.ExistsAsync(Guid.NewGuid());

        // Assert
        Assert.False(exists);
    }

    [Fact]
    public async Task AddAsync_ShouldPersistProcessedMessageAndReflectInExistsAsync()
    {
        // Arrange
        await using var context = new NotificationsDbContext(_dbContextOptions);
        var repository = new ProcessedMessageRepository(context);
        var eventId = Guid.NewGuid();
        var processedMessage = new ProcessedMessage(eventId, "OrderCreatedIntegrationEvent");

        // Act
        await repository.AddAsync(processedMessage);
        await context.SaveChangesAsync();

        // Assert
        await using var verifyContext = new NotificationsDbContext(_dbContextOptions);
        var persisted = await verifyContext.ProcessedMessages.FirstOrDefaultAsync(p => p.EventId == eventId);

        Assert.NotNull(persisted);
        Assert.Equal(eventId, persisted.EventId);
        Assert.Equal("OrderCreatedIntegrationEvent", persisted.EventType);
        Assert.Equal(processedMessage.ProcessedAt, persisted.ProcessedAt);

        var exists = await repository.ExistsAsync(eventId);
        Assert.True(exists);
    }

    [Fact]
    public async Task AddAsync_ShouldThrowException_WhenDuplicateEventIdIsInsertedAndCommitted()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var first = new ProcessedMessage(eventId, "OrderCreatedIntegrationEvent");
        var duplicate = new ProcessedMessage(eventId, "OrderCreatedIntegrationEvent");

        await using (var seedContext = new NotificationsDbContext(_dbContextOptions))
        {
            var repo = new ProcessedMessageRepository(seedContext);
            await repo.AddAsync(first);
            await seedContext.SaveChangesAsync();
        }

        // Act & Assert (DB primary key / unique constraint prevents duplicate EventId on commit)
        await using (var duplicateContext = new NotificationsDbContext(_dbContextOptions))
        {
            var repo = new ProcessedMessageRepository(duplicateContext);
            await repo.AddAsync(duplicate);
            await Assert.ThrowsAnyAsync<DbUpdateException>(() => duplicateContext.SaveChangesAsync());
        }
    }

    public void Dispose()
    {
        _connection.Dispose();
    }
}
