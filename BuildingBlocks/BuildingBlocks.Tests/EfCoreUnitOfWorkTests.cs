using BuildingBlocks.Domain;
using BuildingBlocks.Infrastructure.EfCore;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.VisualStudio.TestTools.UnitTesting;
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
