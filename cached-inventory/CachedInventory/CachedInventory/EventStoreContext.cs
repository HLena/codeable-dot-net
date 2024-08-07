
namespace CachedInventory;

using System.ComponentModel.DataAnnotations;
using Microsoft.EntityFrameworkCore;

// Add a reference to the Microsoft.EntityFrameworkCore assembly

public class EventStoreContext: DbContext
{
  public EventStoreContext(DbContextOptions<EventStoreContext> options) : base(options) { }

  public DbSet<Event> Events { get; set; }
}

public class Event
{
    public int Id { get; set; }
    public int ProductId { get; set; }
    public bool IsRestock { get; set; }
    public int Quantity { get; set; }
    public DateTime Timestamp { get; set; }
}

