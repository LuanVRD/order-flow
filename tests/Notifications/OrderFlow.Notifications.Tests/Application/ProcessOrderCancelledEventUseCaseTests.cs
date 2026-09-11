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

public class ProcessOrderCancelledEventUseCaseTests
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IProcessedMessageRepository _processedMessageRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly ProcessOrderCancelledEventUseCase _useCase;

    public ProcessOrderCancelledEventUseCaseTests()
    {
        _notificationRepository = Substitute.For<INotificationRepository>();
        _processedMessageRepository = Substitute.For<IProcessedMessageRepository>();
        _unitOfWork = Substitute.For<IUnitOfWork>();
        _useCase = new ProcessOrderCancelledEventUseCase(_notificationRepository, _processedMessageRepository, _unitOfWork);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEventIsNewWithReason_ShouldCreateNotificationWithReasonAndCommit()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var payload = new OrderCancelledIntegrationEvent(
            OrderId: orderId,
            PreviousStatus: "Pending",
            CancelledAt: DateTimeOffset.UtcNow,
            Reason: "Item fora de estoque"
        );

        var envelope = EventEnvelope<OrderCancelledIntegrationEvent>.Create(
            eventType: "OrderCancelled",
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
        result.Type.Should().Be(NotificationType.OrderCancelled);
        result.Message.Should().Contain("Item fora de estoque");

        await _notificationRepository.Received(1)
            .AddAsync(Arg.Is<Notification>(n => n.OrderId == orderId && n.Type == NotificationType.OrderCancelled), Arg.Any<CancellationToken>());

        await _processedMessageRepository.Received(1)
            .AddAsync(Arg.Is<ProcessedMessage>(p => p.EventId == eventId), Arg.Any<CancellationToken>());

        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenEventIsNewWithoutReason_ShouldCreateNotificationWithoutReason()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var payload = new OrderCancelledIntegrationEvent(
            OrderId: orderId,
            PreviousStatus: "Pending",
            CancelledAt: DateTimeOffset.UtcNow,
            Reason: null
        );

        var envelope = EventEnvelope<OrderCancelledIntegrationEvent>.Create(
            eventType: "OrderCancelled",
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
        result.Type.Should().Be(NotificationType.OrderCancelled);
        result.Message.Should().Be($"Pedido {orderId} foi cancelado.");

        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenEventAlreadyProcessed_ShouldIgnoreDuplicate()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var payload = new OrderCancelledIntegrationEvent(
            OrderId: Guid.NewGuid(),
            PreviousStatus: "Pending",
            CancelledAt: DateTimeOffset.UtcNow,
            Reason: "Cancelamento solicitado"
        );

        var envelope = EventEnvelope<OrderCancelledIntegrationEvent>.Create(
            eventType: "OrderCancelled",
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
        var payload = new OrderCancelledIntegrationEvent(
            OrderId: orderId,
            PreviousStatus: "Pending",
            CancelledAt: DateTimeOffset.UtcNow,
            Reason: "Cancelamento solicitado"
        );

        var envelope = EventEnvelope<OrderCancelledIntegrationEvent>.Create(
            eventType: "OrderCancelled",
            data: payload,
            eventId: eventId
        );

        int checkCount = 0;
        _processedMessageRepository.ExistsAsync(eventId, Arg.Any<CancellationToken>())
            .Returns(_ =>
            {
                checkCount++;
                return Task.FromResult(checkCount > 1);
            });

        _unitOfWork.CommitAsync(Arg.Any<CancellationToken>())
            .ThrowsAsync(new DbUpdateException("Duplicate key unique constraint violation"));

        // Act
        var result = await _useCase.ExecuteAsync(envelope);

        // Assert
        result.Should().BeNull();
        await _unitOfWork.Received(1).CommitAsync(Arg.Any<CancellationToken>());
    }
}
