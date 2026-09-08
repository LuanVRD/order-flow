using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using NSubstitute;
using OrderFlow.Messaging.Contracts.Correlation;
using OrderFlow.Orders.Api.Middlewares;

namespace OrderFlow.Orders.IntegrationTests.Middlewares;

public class CorrelationIdMiddlewareTests
{
    private readonly ILogger<CorrelationIdMiddleware> _loggerMock;
    private readonly ICorrelationContextAccessor _accessor;

    public CorrelationIdMiddlewareTests()
    {
        _loggerMock = Substitute.For<ILogger<CorrelationIdMiddleware>>();
        _accessor = new CorrelationContextAccessor();
    }

    [Fact]
    public async Task InvokeAsync_ShouldReuseCorrelationId_WhenHeaderIsPresent()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var customId = "trace-custom-correlation-1234";
        context.Request.Headers[CorrelationConstants.HeaderName] = customId;

        var nextCalled = false;
        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            _accessor.CorrelationId.Should().Be(customId);
            return Task.CompletedTask;
        };

        var middleware = new CorrelationIdMiddleware(next, _loggerMock);

        // Act
        await middleware.InvokeAsync(context, _accessor);

        // Assert
        nextCalled.Should().BeTrue();
        _accessor.CorrelationId.Should().Be(customId);
        context.Response.Headers.Should().ContainKey(CorrelationConstants.HeaderName);
        context.Response.Headers[CorrelationConstants.HeaderName].ToString().Should().Be(customId);
    }

    [Fact]
    public async Task InvokeAsync_ShouldGenerateCorrelationId_WhenHeaderIsMissing()
    {
        // Arrange
        var context = new DefaultHttpContext();
        var nextCalled = false;
        string? capturedCorrelationId = null;

        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            capturedCorrelationId = _accessor.CorrelationId;
            return Task.CompletedTask;
        };

        var middleware = new CorrelationIdMiddleware(next, _loggerMock);

        // Act
        await middleware.InvokeAsync(context, _accessor);

        // Assert
        nextCalled.Should().BeTrue();
        capturedCorrelationId.Should().NotBeNullOrWhiteSpace();
        Guid.TryParse(capturedCorrelationId, out _).Should().BeTrue();
        context.Response.Headers.Should().ContainKey(CorrelationConstants.HeaderName);
        context.Response.Headers[CorrelationConstants.HeaderName].ToString().Should().Be(capturedCorrelationId);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task InvokeAsync_ShouldGenerateCorrelationId_WhenHeaderIsWhitespace(string invalidHeader)
    {
        // Arrange
        var context = new DefaultHttpContext();
        context.Request.Headers[CorrelationConstants.HeaderName] = invalidHeader;

        var nextCalled = false;
        string? capturedCorrelationId = null;

        RequestDelegate next = (ctx) =>
        {
            nextCalled = true;
            capturedCorrelationId = _accessor.CorrelationId;
            return Task.CompletedTask;
        };

        var middleware = new CorrelationIdMiddleware(next, _loggerMock);

        // Act
        await middleware.InvokeAsync(context, _accessor);

        // Assert
        nextCalled.Should().BeTrue();
        capturedCorrelationId.Should().NotBeNullOrWhiteSpace();
        Guid.TryParse(capturedCorrelationId, out _).Should().BeTrue();
        context.Response.Headers[CorrelationConstants.HeaderName].ToString().Should().Be(capturedCorrelationId);
    }
}
