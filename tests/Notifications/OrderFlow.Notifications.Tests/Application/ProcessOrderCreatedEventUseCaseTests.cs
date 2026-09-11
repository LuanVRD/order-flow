using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using OrderFlow.Messaging.Contracts.Events;
using OrderFlow.Notifications.Application.Interfaces;
using OrderFlow.Notifications.Application.UseCases;
using OrderFlow.Notifications.Domain.Entities;
using OrderFlow.Notifications.Domain.Enums;

namespace OrderFlow.Notifications.Tests.Application;

public class ProcessOrderCreatedEventUseCaseTests
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IProcessedMessageRepository _processedMessageRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ProcessOrderCreatedEventUseCase _useCase;

    public ProcessOrderCreatedEventUseCaseTests()
    {
        _notificationRepository = Substitute.For<INotificationRepository>();
        _processedMessageRepository = Substitute.For<IProcessedMessageRepository>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _useCase = new ProcessOrderCreatedEventUseCase(_notificationRepository, _processedMessageRepository, _unitOfWork);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEventIsNew_ShouldCreateNotificationAndRecordProcessedMessageAndCommit()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var payload = new OrderCreatedIntegrationEvent(
            OrderId: orderId,
            CustomerName: "Luan Silva",
            CustomerEmail: "luan@example.com",
            TotalAmount: 150.75m,
            Status: "Pending",
            CreatedAt: DateTimeOffset.UtcNow
        );

        var envelope = EventEnvelope<OrderCreatedIntegrationEvent>.Create(
            eventType: "OrderCreated",
            data: payload,
            eventId: eventId
        );

        _processedMessageRepository.ExistsAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(false);

        // Act
        var result = await _useCase.ExecuteAsync(envelope);

        // Assert
        result.Should().NotBeNull();
        result!.OrderId.Should().Be(orderId);
        result.Type.Should().Be(NotificationType.OrderCreated);
        result.Message.Should().Contain("Luan Silva");
        result.Message.Should().Contain(orderId.ToString());
        result.Message.Should().Contain("150.75");

        await _notificationRepository.Received(1)
            .AddAsync(Arg.Is<Notification>(n => n.OrderId == orderId && n.Type == NotificationType.OrderCreated), Arg.Any<CancellationToken>());

        await _processedMessageRepository.Received(1)
            .AddAsync(Arg.Is<ProcessedMessage>(p => p.EventId == eventId && p.EventType == "OrderCreated"), Arg.Any<CancellationToken>());

        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenEventAlreadyProcessed_ShouldNotCreateNotificationOrAddProcessedMessage()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var payload = new OrderCreatedIntegrationEvent(
            OrderId: Guid.NewGuid(),
            CustomerName: "Luan Silva",
            CustomerEmail: "luan@example.com",
            TotalAmount: 150.75m,
            Status: "Pending",
            CreatedAt: DateTimeOffset.UtcNow
        );

        var envelope = EventEnvelope<OrderCreatedIntegrationEvent>.Create(
            eventType: "OrderCreated",
            data: payload,
            eventId: eventId
        );

        _processedMessageRepository.ExistsAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(true);

        // Act
        var result = await _useCase.ExecuteAsync(envelope);

        // Assert
        result.Should().BeNull();

        await _notificationRepository.DidNotReceive()
            .AddAsync(Arg.Any<Notification>(), Arg.Any<CancellationToken>());

        await _processedMessageRepository.DidNotReceive()
            .AddAsync(Arg.Any<ProcessedMessage>(), Arg.Any<CancellationToken>());

        await _unitOfWork.DidNotReceive().CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenConcurrentConflictOccursOnCommit_ShouldReturnNullWhenAlreadyPersisted()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var payload = new OrderCreatedIntegrationEvent(
            OrderId: orderId,
            CustomerName: "Luan Silva",
            CustomerEmail: "luan@example.com",
            TotalAmount: 150.75m,
            Status: "Pending",
            CreatedAt: DateTimeOffset.UtcNow
        );

        var envelope = EventEnvelope<OrderCreatedIntegrationEvent>.Create(
            eventType: "OrderCreated",
            data: payload,
            eventId: eventId
        );

        int checkCount = 0;
        _processedMessageRepository.ExistsAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                checkCount++;
                return Task.FromResult(checkCount > 1); // 1st check: false (race window), 2nd check after DbUpdateException: true
            });

        _unitOfWork.CommitAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new DbUpdateException("Duplicate key unique constraint violation"));

        // Act
        var result = await _useCase.ExecuteAsync(envelope);

        // Assert
        result.Should().BeNull();
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenEnvelopeIsNull_ShouldThrowArgumentNullException()
    {
        // Act
        var act = () => _useCase.ExecuteAsync(null!);

        // Assert
        await act.Should().ThrowAsync<ArgumentNullException>();
    }
}
