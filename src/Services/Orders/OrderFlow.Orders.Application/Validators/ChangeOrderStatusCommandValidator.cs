using FluentValidation;
using OrderFlow.Orders.Application.DTOs;

namespace OrderFlow.Orders.Application.Validators;

public class ChangeOrderStatusCommandValidator : AbstractValidator<ChangeOrderStatusCommand>
{
    public ChangeOrderStatusCommandValidator()
    {
        RuleFor(x => x.Id)
            .NotEmpty().WithMessage("Order ID is required.");

        RuleFor(x => x.NewStatus)
            .IsInEnum().WithMessage("Invalid order status value.");
    }
}
