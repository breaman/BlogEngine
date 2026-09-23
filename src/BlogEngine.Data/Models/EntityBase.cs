using BlogEngine.Data.Interfaces;

namespace BlogEngine.Data.Models;

public abstract class EntityBase : IEntityBase
{
    public int Id { get; set; }
}