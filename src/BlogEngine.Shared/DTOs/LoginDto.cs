using FluentValidation;

namespace BlogEngine.Shared.DTOs;

public class LoginDto
{
    public string Email { get; set; }
    public string Password { get; set; }
}
public class LoginDtoValidator : AbstractValidator<LoginDto>
{
    public LoginDtoValidator()
    {
        RuleFor(viewModel => viewModel.Email).NotEmpty().EmailAddress();
        RuleFor(viewModel => viewModel.Password).NotEmpty();
    }
}