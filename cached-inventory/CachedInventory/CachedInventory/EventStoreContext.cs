
namespace CachedInventory;
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
    public string Type { get; set; }
    public string Data { get; set; }
    public DateTime Timestamp { get; set; }
}

public record StockRetrieved(int ProductId, int Amount);
public record StockRestocked(int ProductId, int Amount);
