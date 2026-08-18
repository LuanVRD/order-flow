using Microsoft.AspNetCore.Mvc;
using OrderFlow.Orders.Api.Models.Requests;
using OrderFlow.Orders.Application.DTOs;
using OrderFlow.Orders.Application.UseCases;

namespace OrderFlow.Orders.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class OrdersController : ControllerBase
{
    private readonly CreateOrderUseCase _createOrderUseCase;
    private readonly GetOrderByIdUseCase _getOrderByIdUseCase;
    private readonly GetOrdersUseCase _getOrdersUseCase;
    private readonly ChangeOrderStatusUseCase _changeOrderStatusUseCase;
    private readonly CancelOrderUseCase _cancelOrderUseCase;

    public OrdersController(
        CreateOrderUseCase createOrderUseCase,
        GetOrderByIdUseCase getOrderByIdUseCase,
        GetOrdersUseCase getOrdersUseCase,
        ChangeOrderStatusUseCase changeOrderStatusUseCase,
        CancelOrderUseCase cancelOrderUseCase)
    {
        _createOrderUseCase = createOrderUseCase;
        _getOrderByIdUseCase = getOrderByIdUseCase;
        _getOrdersUseCase = getOrdersUseCase;
        _changeOrderStatusUseCase = changeOrderStatusUseCase;
        _cancelOrderUseCase = cancelOrderUseCase;
    }

    [HttpPost]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<OrderResponse>> Create(
        [FromBody] CreateOrderCommand command,
        CancellationToken cancellationToken)
    {
        var response = await _createOrderUseCase.ExecuteAsync(command, cancellationToken);
        return CreatedAtAction(nameof(GetById), new { id = response.Id }, response);
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyCollection<OrderResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyCollection<OrderResponse>>> GetAll(
        CancellationToken cancellationToken)
    {
        var response = await _getOrdersUseCase.ExecuteAsync(cancellationToken);
        return Ok(response);
    }

    [HttpGet("{id:guid}")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderResponse>> GetById(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var response = await _getOrderByIdUseCase.ExecuteAsync(id, cancellationToken);
        return Ok(response);
    }

    [HttpPatch("{id:guid}/status")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderResponse>> UpdateStatus(
        [FromRoute] Guid id,
        [FromBody] UpdateOrderStatusRequest request,
        CancellationToken cancellationToken)
    {
        var command = new ChangeOrderStatusCommand(id, request.NewStatus);
        var response = await _changeOrderStatusUseCase.ExecuteAsync(command, cancellationToken);
        return Ok(response);
    }

    [HttpPost("{id:guid}/cancel")]
    [ProducesResponseType(typeof(OrderResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderResponse>> Cancel(
        [FromRoute] Guid id,
        CancellationToken cancellationToken)
    {
        var command = new CancelOrderCommand(id);
        var response = await _cancelOrderUseCase.ExecuteAsync(command, cancellationToken);
        return Ok(response);
    }
}
