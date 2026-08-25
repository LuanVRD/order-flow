using FluentAssertions;
using NSubstitute;
using OrderFlow.Messaging.Contracts.Events;
using OrderFlow.Notifications.Application.Interfaces;
using OrderFlow.Notifications.Application.UseCases;
using OrderFlow.Notifications.Domain.Entities;
using OrderFlow.Notifications.Domain.Enums;

namespace OrderFlow.Notifications.Tests.Application;

public class ProcessOrderCompletedEventUseCaseTests
{
    private readonly INotificationRepository _notificationRepository;
    private readonly IProcessedMessageRepository _processedMessageRepository;
    private readonly ProcessOrderCompletedEventUseCase _useCase;

    public ProcessOrderCompletedEventUseCaseTests()
    {
        _notificationRepository = Substitute.For<INotificationRepository>();
        _processedMessageRepository = Substitute.For<IProcessedMessageRepository>();
        _useCase = new ProcessOrderCompletedEventUseCase(_notificationRepository, _processedMessageRepository);
    }

    [Fact]
    public async Task ExecuteAsync_WhenEventIsNew_ShouldCreateNotificationAndRecordProcessedMessage()
    {
        // Arrange
        var orderId = Guid.NewGuid();
        var eventId = Guid.NewGuid();
        var payload = new OrderCompletedIntegrationEvent(
            OrderId: orderId,
            CompletedAt: DateTimeOffset.UtcNow
        );

        var envelope = EventEnvelope<OrderCompletedIntegrationEvent>.Create(
            eventType: "OrderCompleted",
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
        result.Type.Should().Be(NotificationType.OrderCompleted);
        result.Message.Should().Contain("concluído com sucesso");

        await _notificationRepository.Received(1)
            .AddAsync(Arg.Is<Notification>(n => n.OrderId == orderId && n.Type == NotificationType.OrderCompleted), Arg.Any<CancellationToken>());

        await _processedMessageRepository.Received(1)
            .AddAsync(Arg.Is<ProcessedMessage>(p => p.EventId == eventId), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WhenEventAlreadyProcessed_ShouldIgnoreDuplicate()
    {
        // Arrange
        var eventId = Guid.NewGuid();
        var payload = new OrderCompletedIntegrationEvent(
            OrderId: Guid.NewGuid(),
            CompletedAt: DateTimeOffset.UtcNow
        );

        var envelope = EventEnvelope<OrderCompletedIntegrationEvent>.Create(
            eventType: "OrderCompleted",
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
    }
}
