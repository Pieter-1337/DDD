using BuildingBlocks.Application.Interfaces;
using BuildingBlocks.Application.Messaging;
using BuildingBlocks.Domain;
using BuildingBlocks.Infrastructure.EfCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;

namespace BuildingBlocks.Tests;

/// <summary>
/// Tests for EfCoreUnitOfWork, specifically nested transaction handling.
/// </summary>
[TestClass]
public class EfCoreUnitOfWorkTests
{
    private SqliteConnection? _connection;
    private TestDbContext? _context;
    private EfCoreUnitOfWork<TestDbContext>? _unitOfWork;

    [TestInitialize]
    public void TestInitialize()
    {
        // SQLite in-memory requires the connection to stay open
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        var options = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection)
            .Options;

        _context = new TestDbContext(options);
        _context.Database.EnsureCreated();

        _unitOfWork = new EfCoreUnitOfWork<TestDbContext>(_context);
    }

    [TestCleanup]
    public void TestCleanup()
    {
        _context?.Dispose();
        _connection?.Dispose();
    }

    [TestMethod]
    public async Task BeginTransactionAsync_Should_StartNewTransaction_WhenNoTransactionActive()
    {
        // Act
        await _unitOfWork!.BeginTransactionAsync();

        // Assert - no exception thrown, transaction is active
        // We can verify by checking we can commit successfully
        await _unitOfWork.CloseTransactionAsync();
    }

    [TestMethod]
    public async Task BeginTransactionAsync_Should_ReuseTransaction_WhenTransactionAlreadyActive()
    {
        // Arrange - start outer transaction
        await _unitOfWork!.BeginTransactionAsync();

        // Act - try to start nested transaction
        await _unitOfWork.BeginTransactionAsync();

        // Assert - should not throw, and single close should work
        await _unitOfWork.CloseTransactionAsync();
    }

    [TestMethod]
    public async Task CloseTransactionAsync_Should_NotCommit_WhenNestedTransaction()
    {
        // Arrange
        var testEntity = new TestEntity { Name = "Test" };

        // Start outer transaction
        await _unitOfWork!.BeginTransactionAsync();

        // Simulate inner command starting "nested" transaction
        await _unitOfWork.BeginTransactionAsync();

        // Add entity in nested scope
        _unitOfWork.RepositoryFor<TestEntity>().Add(testEntity);
        await _unitOfWork.SaveChangesAsync();

        // Close nested (should NOT commit)
        await _unitOfWork.CloseTransactionAsync();

        // Close outer (should commit)
        await _unitOfWork.CloseTransactionAsync();

        // Assert - entity should be persisted
        var savedEntity = await _context!.TestEntities.FirstOrDefaultAsync(e => e.Name == "Test");
        savedEntity.ShouldNotBeNull();
    }

    [TestMethod]
    public async Task NestedClose_Should_NotThrow_WhenCalledMultipleTimes()
    {
        // Arrange - start outer transaction
        await _unitOfWork!.BeginTransactionAsync();

        // Simulate nested transaction (inner command)
        await _unitOfWork.BeginTransactionAsync();

        // Close nested - should not throw or affect outer transaction
        await _unitOfWork.CloseTransactionAsync();

        // Close outer - should not throw (transaction still exists)
        await _unitOfWork.CloseTransactionAsync();

        // Additional close should not throw (transaction is null now)
        await _unitOfWork.CloseTransactionAsync();
    }

    [TestMethod]
    public async Task NestedTransaction_Should_NotDoubleCommit()
    {
        // This test verifies that nested BeginTransaction doesn't create a second transaction
        // and nested CloseTransaction doesn't prematurely commit

        // Arrange
        var testEntity1 = new TestEntity { Name = "Entity1" };
        var testEntity2 = new TestEntity { Name = "Entity2" };

        // Start outer transaction
        await _unitOfWork!.BeginTransactionAsync();
        _unitOfWork.RepositoryFor<TestEntity>().Add(testEntity1);
        await _unitOfWork.SaveChangesAsync();

        // Nested transaction (inner command)
        await _unitOfWork.BeginTransactionAsync();
        _unitOfWork.RepositoryFor<TestEntity>().Add(testEntity2);
        await _unitOfWork.SaveChangesAsync();
        await _unitOfWork.CloseTransactionAsync(); // Should not commit yet

        // Before outer close - entities should be in the change tracker but not "committed"
        // Close outer - this actually commits
        await _unitOfWork.CloseTransactionAsync();

        // Verify both entities were saved in single transaction
        var entities = await _context!.TestEntities.ToListAsync();
        entities.Count.ShouldBe(2);
        entities.ShouldContain(e => e.Name == "Entity1");
        entities.ShouldContain(e => e.Name == "Entity2");
    }

    [TestMethod]
    public async Task OuterTransaction_Should_StillCommit_AfterNestedTransactionCloses()
    {
        // This test catches the bug where _ownsTransaction boolean was overwritten by nested calls.
        // The bug: outer sets _ownsTransaction=true, inner sets it to false,
        // then outer's CloseTransaction sees false and doesn't commit.
        // The fix uses a depth counter instead.

        // Arrange
        var testEntity = new TestEntity { Name = "OuterEntity" };

        // Outer command starts transaction
        await _unitOfWork!.BeginTransactionAsync();

        // Inner command starts "nested" transaction (reuses existing)
        await _unitOfWork.BeginTransactionAsync();

        // Inner command does work and closes
        await _unitOfWork.CloseTransactionAsync();

        // Outer command adds entity AFTER inner command closes
        // This is the key scenario - outer must still be able to commit
        _unitOfWork.RepositoryFor<TestEntity>().Add(testEntity);
        await _unitOfWork.SaveChangesAsync();

        // Outer command closes - THIS MUST COMMIT
        await _unitOfWork.CloseTransactionAsync();

        // Create a fresh context to verify the data was actually persisted
        var verifyOptions = new DbContextOptionsBuilder<TestDbContext>()
            .UseSqlite(_connection!)
            .Options;
        using var verifyContext = new TestDbContext(verifyOptions);

        var savedEntity = await verifyContext.TestEntities.FirstOrDefaultAsync(e => e.Name == "OuterEntity");
        savedEntity.ShouldNotBeNull("Entity should have been committed by outer transaction");
    }

    [TestMethod]
    public async Task DeeplyNestedTransactions_Should_OnlyCommitAtOutermostLevel()
    {
        // Test 3-level deep nesting to ensure depth counter works correctly

        // Arrange
        var entity1 = new TestEntity { Name = "Level1" };
        var entity2 = new TestEntity { Name = "Level2" };
        var entity3 = new TestEntity { Name = "Level3" };

        // Level 1 (outermost)
        await _unitOfWork!.BeginTransactionAsync();
        _unitOfWork.RepositoryFor<TestEntity>().Add(entity1);
        await _unitOfWork.SaveChangesAsync();

        // Level 2
        await _unitOfWork.BeginTransactionAsync();
        _unitOfWork.RepositoryFor<TestEntity>().Add(entity2);
        await _unitOfWork.SaveChangesAsync();

        // Level 3 (innermost)
        await _unitOfWork.BeginTransactionAsync();
        _unitOfWork.RepositoryFor<TestEntity>().Add(entity3);
        await _unitOfWork.SaveChangesAsync();
        await _unitOfWork.CloseTransactionAsync(); // Level 3 closes

        await _unitOfWork.CloseTransactionAsync(); // Level 2 closes

        await _unitOfWork.CloseTransactionAsync(); // Level 1 closes - commits

        // Verify all entities were saved
        var entities = await _context!.TestEntities.ToListAsync();
        entities.Count.ShouldBe(3);
        entities.ShouldContain(e => e.Name == "Level1");
        entities.ShouldContain(e => e.Name == "Level2");
        entities.ShouldContain(e => e.Name == "Level3");
    }

    [TestMethod]
    public async Task CloseTransaction_Should_CallCommitStrategy_WhenPresent()
    {
        // Arrange
        var commitStrategyMock = new Mock<ICommitStrategy>();
        var unitOfWork = new EfCoreUnitOfWork<TestDbContext>(_context!, commitStrategy: commitStrategyMock.Object);

        await unitOfWork.BeginTransactionAsync();
        unitOfWork.RepositoryFor<TestEntity>().Add(new TestEntity { Name = "Test" });
        await unitOfWork.SaveChangesAsync();

        // Act
        await unitOfWork.CloseTransactionAsync();

        // Assert
        commitStrategyMock.Verify(s => s.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CloseTransaction_Should_NotCallCommitStrategy_WhenNestedDepth()
    {
        // Arrange
        var commitStrategyMock = new Mock<ICommitStrategy>();
        var unitOfWork = new EfCoreUnitOfWork<TestDbContext>(_context!, commitStrategy: commitStrategyMock.Object);

        // Begin outer (depth 1)
        await unitOfWork.BeginTransactionAsync();
        // Begin inner (depth 2)
        await unitOfWork.BeginTransactionAsync();

        // Act - Close inner (depth goes to 1, should NOT call strategy)
        await unitOfWork.CloseTransactionAsync();

        // Assert - not called yet
        commitStrategyMock.Verify(s => s.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);

        // Act - Close outer (depth goes to 0, should call strategy)
        await unitOfWork.CloseTransactionAsync();

        // Assert - called exactly once at depth 0
        commitStrategyMock.Verify(s => s.CommitAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task CloseTransaction_Should_NotCallCommitStrategy_OnRollback()
    {
        // Arrange
        var commitStrategyMock = new Mock<ICommitStrategy>();
        var unitOfWork = new EfCoreUnitOfWork<TestDbContext>(_context!, commitStrategy: commitStrategyMock.Object);

        await unitOfWork.BeginTransactionAsync();

        // Act - close with exception (rollback)
        await unitOfWork.CloseTransactionAsync(new InvalidOperationException("Test error"));

        // Assert - commit strategy should NOT be called on rollback
        commitStrategyMock.Verify(s => s.CommitAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task SaveChangesAsync_Should_PublishIntegrationEvents_ViaEventBus()
    {
        // Arrange
        var eventBusMock = new Mock<IEventBus>();
        eventBusMock
            .Setup(b => b.PublishAsync(It.IsAny<TestIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var unitOfWork = new EfCoreUnitOfWork<TestDbContext>(_context!, eventBus: eventBusMock.Object);

        var event1 = new TestIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow);
        var event2 = new TestIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow);
        unitOfWork.QueueIntegrationEvent(event1);
        unitOfWork.QueueIntegrationEvent(event2);

        // Act
        await unitOfWork.SaveChangesAsync();

        // Assert - both events published
        eventBusMock.Verify(
            b => b.PublishAsync(It.IsAny<TestIntegrationEvent>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));

        // Act - second SaveChanges should publish nothing (queue was cleared)
        await unitOfWork.SaveChangesAsync();

        // Assert - still exactly 2 (no new calls)
        eventBusMock.Verify(
            b => b.PublishAsync(It.IsAny<TestIntegrationEvent>(), It.IsAny<CancellationToken>()),
            Times.Exactly(2));
    }

    [TestMethod]
    public async Task CloseTransaction_WithException_Should_ClearQueuedIntegrationEvents()
    {
        // Arrange
        var eventBusMock = new Mock<IEventBus>();
        eventBusMock
            .Setup(b => b.PublishAsync(It.IsAny<TestIntegrationEvent>(), It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);
        var unitOfWork = new EfCoreUnitOfWork<TestDbContext>(_context!, eventBus: eventBusMock.Object);

        unitOfWork.QueueIntegrationEvent(new TestIntegrationEvent(Guid.NewGuid(), DateTime.UtcNow));

        await unitOfWork.BeginTransactionAsync();

        // Act - rollback should clear queued events
        await unitOfWork.CloseTransactionAsync(new InvalidOperationException("Test error"));

        // SaveChanges after rollback should NOT publish anything
        await unitOfWork.SaveChangesAsync();

        // Assert
        eventBusMock.Verify(
            b => b.PublishAsync(It.IsAny<TestIntegrationEvent>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }
}

/// <summary>
/// Simple test entity for unit of work tests.
/// </summary>
public class TestEntity : Entity
{
    public string Name { get; set; } = string.Empty;
}

/// <summary>
/// Simple test DbContext for unit of work tests.
/// </summary>
public class TestDbContext : DbContext
{
    public TestDbContext(DbContextOptions<TestDbContext> options) : base(options)
    {
    }

    public DbSet<TestEntity> TestEntities => Set<TestEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TestEntity>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(100);
        });
    }
}

public record TestIntegrationEvent(Guid EventId, DateTime OccurredOn) : IIntegrationEvent;
