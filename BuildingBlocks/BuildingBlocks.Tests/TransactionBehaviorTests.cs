using BuildingBlocks.Application.Behaviors;
using BuildingBlocks.Application.Cqrs;
using BuildingBlocks.Application.Interfaces;
using FluentValidation;
using MediatR;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using Shouldly;

namespace BuildingBlocks.Tests;

/// <summary>
/// Tests for TransactionBehavior pipeline behavior.
/// Verifies correct transaction handling for commands vs queries.
/// </summary>
[TestClass]
public class TransactionBehaviorTests
{
    private Mock<IUnitOfWork> _unitOfWorkMock = null!;

    [TestInitialize]
    public void Setup()
    {
        _unitOfWorkMock = new Mock<IUnitOfWork>();
    }

    [TestMethod]
    public async Task Handle_ShouldBeginTransaction_ForCommand()
    {
        // Arrange
        var behavior = new TransactionBehavior<TestCommand, TestResponse>(_unitOfWorkMock.Object);
        var command = new TestCommand();
        RequestHandlerDelegate<TestResponse> next = () => Task.FromResult(new TestResponse());

        // Act
        await behavior.Handle(command, next, CancellationToken.None);

        // Assert
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Once);
    }

    [TestMethod]
    public async Task Handle_ShouldNotBeginTransaction_ForQuery()
    {
        // Arrange
        var behavior = new TransactionBehavior<TestQuery, TestResponse>(_unitOfWorkMock.Object);
        var query = new TestQuery();
        RequestHandlerDelegate<TestResponse> next = () => Task.FromResult(new TestResponse());

        // Act
        await behavior.Handle(query, next, CancellationToken.None);

        // Assert
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Handle_ShouldNotBeginTransaction_WhenSkipTransactionIsTrue()
    {
        // Arrange
        var behavior = new TransactionBehavior<TestCommand, TestResponse>(_unitOfWorkMock.Object);
        var command = new TestCommand { SkipTransaction = true };
        RequestHandlerDelegate<TestResponse> next = () => Task.FromResult(new TestResponse());

        // Act
        await behavior.Handle(command, next, CancellationToken.None);

        // Assert
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Never);
    }

    [TestMethod]
    public async Task Handle_ShouldCommitTransaction_OnSuccess()
    {
        // Arrange
        var behavior = new TransactionBehavior<TestCommand, TestResponse>(_unitOfWorkMock.Object);
        var command = new TestCommand();
        RequestHandlerDelegate<TestResponse> next = () => Task.FromResult(new TestResponse());

        // Act
        await behavior.Handle(command, next, CancellationToken.None);

        // Assert - CloseTransactionAsync should be called without an exception (null)
        _unitOfWorkMock.Verify(
            u => u.CloseTransactionAsync(null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task Handle_ShouldRollbackTransaction_OnException()
    {
        // Arrange
        var behavior = new TransactionBehavior<TestCommand, TestResponse>(_unitOfWorkMock.Object);
        var command = new TestCommand();
        var expectedException = new InvalidOperationException("Test exception");
        RequestHandlerDelegate<TestResponse> next = () => throw expectedException;

        // Act & Assert
        var thrownException = await Should.ThrowAsync<InvalidOperationException>(
            async () => await behavior.Handle(command, next, CancellationToken.None));

        thrownException.ShouldBe(expectedException);

        // Verify CloseTransactionAsync was called with the exception (for rollback)
        _unitOfWorkMock.Verify(
            u => u.CloseTransactionAsync(expectedException, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task Handle_ShouldCommitTransaction_OnValidationException()
    {
        // Arrange
        var behavior = new TransactionBehavior<TestCommand, TestResponse>(_unitOfWorkMock.Object);
        var command = new TestCommand();
        var validationException = new ValidationException("Validation failed");
        RequestHandlerDelegate<TestResponse> next = () => throw validationException;

        // Act & Assert
        await Should.ThrowAsync<ValidationException>(
            async () => await behavior.Handle(command, next, CancellationToken.None));

        // Verify CloseTransactionAsync was called WITHOUT an exception (commit, not rollback)
        // ValidationExceptions should commit any validator side effects
        _unitOfWorkMock.Verify(
            u => u.CloseTransactionAsync(null, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [TestMethod]
    public async Task Handle_ShouldReturnResponse_FromNextDelegate()
    {
        // Arrange
        var behavior = new TransactionBehavior<TestCommand, TestResponse>(_unitOfWorkMock.Object);
        var command = new TestCommand();
        var expectedResponse = new TestResponse { Value = "Expected" };
        RequestHandlerDelegate<TestResponse> next = () => Task.FromResult(expectedResponse);

        // Act
        var result = await behavior.Handle(command, next, CancellationToken.None);

        // Assert
        result.ShouldBe(expectedResponse);
    }

    [TestMethod]
    public async Task Handle_ShouldNotCallCloseTransaction_ForQuery()
    {
        // Arrange
        var behavior = new TransactionBehavior<TestQuery, TestResponse>(_unitOfWorkMock.Object);
        var query = new TestQuery();
        RequestHandlerDelegate<TestResponse> next = () => Task.FromResult(new TestResponse());

        // Act
        await behavior.Handle(query, next, CancellationToken.None);

        // Assert - no transaction methods should be called for queries
        _unitOfWorkMock.Verify(
            u => u.CloseTransactionAsync(It.IsAny<Exception?>(), It.IsAny<CancellationToken>()),
            Times.Never);
    }

    [TestMethod]
    public async Task NestedCommands_ShouldShareTransaction_AndOnlyOuterCommits()
    {
        // This test simulates:
        // OuterCommand -> TransactionBehavior -> Handler calls InnerCommand
        //                                         -> TransactionBehavior (reuses txn)
        //                                         -> Handler completes
        //                                      <- returns
        //              <- commits transaction

        // Track call order
        var callOrder = new List<string>();

        _unitOfWorkMock.Setup(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("BeginTransaction"))
            .Returns(Task.CompletedTask);

        _unitOfWorkMock.Setup(u => u.CloseTransactionAsync(null, It.IsAny<CancellationToken>()))
            .Callback(() => callOrder.Add("CloseTransaction"))
            .Returns(Task.CompletedTask);

        var outerBehavior = new TransactionBehavior<TestCommand, TestResponse>(_unitOfWorkMock.Object);
        var innerBehavior = new TransactionBehavior<TestCommand, TestResponse>(_unitOfWorkMock.Object);

        // Simulate outer command calling inner command
        RequestHandlerDelegate<TestResponse> innerHandler = () => Task.FromResult(new TestResponse());

        RequestHandlerDelegate<TestResponse> outerHandler = async () =>
        {
            callOrder.Add("OuterHandler_Start");

            // This simulates calling a nested command through MediatR
            // The inner behavior should detect existing transaction
            var innerResult = await innerBehavior.Handle(new TestCommand(), innerHandler, CancellationToken.None);

            callOrder.Add("OuterHandler_End");
            return new TestResponse();
        };

        // Act
        await outerBehavior.Handle(new TestCommand(), outerHandler, CancellationToken.None);

        // Assert
        // BeginTransaction should be called twice (outer and inner both try)
        _unitOfWorkMock.Verify(u => u.BeginTransactionAsync(It.IsAny<CancellationToken>()), Times.Exactly(2));

        // CloseTransaction should be called twice (but inner one does nothing due to _ownsTransaction)
        _unitOfWorkMock.Verify(u => u.CloseTransactionAsync(null, It.IsAny<CancellationToken>()), Times.Exactly(2));

        // Verify order: Begin -> OuterStart -> Begin (inner, reused) -> Close (inner, no-op) -> OuterEnd -> Close (outer, commits)
        callOrder[0].ShouldBe("BeginTransaction");
        callOrder[1].ShouldBe("OuterHandler_Start");
        callOrder[2].ShouldBe("BeginTransaction"); // Inner tries but reuses
        callOrder[3].ShouldBe("CloseTransaction"); // Inner closes (no-op)
        callOrder[4].ShouldBe("OuterHandler_End");
        callOrder[5].ShouldBe("CloseTransaction"); // Outer commits
    }

    #region Test Classes

    private record TestCommand : Command<TestResponse>;

    private record TestQuery : Query<TestResponse>;

    private record TestResponse
    {
        public string Value { get; init; } = string.Empty;
    }

    #endregion
}
