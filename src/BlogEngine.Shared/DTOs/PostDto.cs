using FluentValidation;

namespace BlogEngine.Shared.DTOs;

public class PostDto
{
    public int PostId { get; set; }
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string? Slug { get; set; }
    public string? Description { get; set; }
    public string? Content { get; set; }
    public DateOnly PublishDate { get; set; }
    public bool IsPublished { get; set; }
}

public class PostDtoValidator : AbstractValidator<PostDto>
{
    public PostDtoValidator()
    {
        RuleFor(viewModel => viewModel.Title).NotEmpty();
        RuleFor(viewModel => viewModel.Slug).NotEmpty();
        RuleFor(viewModel => viewModel.Content).NotEmpty();
        RuleFor(viewModel => viewModel.PublishDate).NotEmpty();
    }
}