using Microsoft.EntityFrameworkCore;

namespace BlogEngine.Data.Models;

public class ApplicationDbContext : AuthDbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : base(options)
    {
    }
}