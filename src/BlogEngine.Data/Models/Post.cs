using BlogEngine.Shared.DTOs;

namespace BlogEngine.Data.Models;

public class Post : FingerPrintEntityBase
{
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string? Slug { get; set; }
    public string? Description { get; set; }
    public string? Content { get; set; }
    public DateOnly? PublishDate { get; set; }
    public bool IsPublished { get; set; }
    public int UserId { get; set; }
    public User User { get; set; }
    
    public PostDto ToDto()
    {
        return new PostDto
        {
            PostId = Id,
            Title = Title,
            Subtitle = Subtitle,
            Slug = Slug,
            Description = Description,
            Content = Content,
            PublishDate = PublishDate ?? DateOnly.FromDateTime(DateTime.Now),
            IsPublished = IsPublished
        };
    }

    public void FromDto(PostDto dto, int userId)
    {
        Title = dto.Title;
        Subtitle = dto.Subtitle;
        Slug = dto.Slug;
        Description = dto.Description;
        Content = dto.Content;
        PublishDate = dto.PublishDate;
        IsPublished = dto.IsPublished;
        UserId = userId;
    }
}