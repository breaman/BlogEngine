namespace BlogEngine.Server.DTOs;

public class PostDto
{
    public string Title { get; set; }
    public string Slug { get; set; }
    public string Description { get; set; }
    public DateTimeOffset PublishDate { get; set; }
}