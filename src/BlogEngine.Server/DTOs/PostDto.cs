namespace BlogEngine.Server.DTOs;

public class PostDto
{
    public string? Title { get; set; }
    public string? Subtitle { get; set; }
    public string? Slug { get; set; }
    public string? Description { get; set; }
    public string? Content { get; set; }
    public DateOnly PublishDate { get; set; }
}