namespace CachedInventory;


using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Http;
using System.Net.Http.Json;

public static class CachedInventoryApiBuilder
{
  public static WebApplication Build(string[] args)
  {
    var builder = WebApplication.CreateBuilder(args);

    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.AddDebug();

    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddDbContext<EventStoreContext>(
      options =>
        options.UseSqlServer("name=DefaultConnection")
    );
    builder.Services.AddSingleton<IWarehouseStockSystemClient, WarehouseStockSystemClient>();

    builder.Services.AddHttpClient();

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
      app.UseSwagger();
      app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();

    _ = app.MapGet(
        "/stock/{productId:int}",
        static async ([FromServices] IWarehouseStockSystemClient client, [FromServices] EventStoreContext context, int productId) =>
        {
          var stock = await GetStockFromEvents(context, productId);
          // return Results.Ok(stock);
          return Results.Json(new { stock.Stock, stock.FailedRequests });

        })
      .WithName("GetStock")
      .WithOpenApi();

    app.MapPost(
        "/stock/validate-retrieve",
        async ([FromServices] IWarehouseStockSystemClient client, [FromServices] EventStoreContext context, int productId, [FromBody] RetrieveStockRequest req) =>
        {

          var hasPreviousEvents = await context.Events.AnyAsync(e => e.ProductId == productId);
          if(!hasPreviousEvents)
          {
              var initialStock = await client.GetStock(productId);
              if(initialStock < req.Amount)
              {
                return Results.BadRequest("Not enough stock.");
              }
          }
          else
          {
            var currentStock = await GetStockFromEvents(context, productId);
            if(currentStock.Stock < req.Amount)
            {
              return Results.BadRequest("Not enough stock.");
            }
          }

          return Results.Ok("Stock is available.");
        })
      .WithName("ValidateRetrieve")
      .WithOpenApi();


    app.MapPost(
      "/stock/retrieve",
      async ([FromServices] EventStoreContext context,[FromServices] IHttpClientFactory httpClientFactory, [FromBody] RetrieveStockRequest req) =>
      {

        var client = httpClientFactory.CreateClient();

        var validationResponse = await client.PostAsJsonAsync("/stock/validate-retrieve", req);

        if(!validationResponse.IsSuccessStatusCode)
        {
          var error = await validationResponse.Content.ReadAsStringAsync();
          return Results.BadRequest(error);
        }

        var currentStock = await GetStockFromEvents(context, req.ProductId);

        var stockRemovalEvent = new Event
        {
          ProductId = req.ProductId,
          IsRestock = false,
          Quantity = req.Amount,
          Timestamp = DateTime.UtcNow
        };

        await context.Events.AddAsync(stockRemovalEvent);
        await context.SaveChangesAsync();

        return Results.Ok();
        // Results.Redirect($"/stock/validateOperation/{req.ProductId}?amount={req.Amount}");
      })
    .WithName("RetrieveStock")
    .WithOpenApi();


    app.MapPost(
          "/stock/restock",
          async ([FromServices] IWarehouseStockSystemClient client, [FromServices] EventStoreContext context, [FromBody] RestockRequest req) =>
          {
            var hasPreviousEvents = await context.Events.AnyAsync(e => e.ProductId == req.ProductId);
            if(!hasPreviousEvents)
            {
                var initialStock = await client.GetStock(req.ProductId);
                var initialStockEvent = new Event
                {
                    ProductId = req.ProductId,
                    IsRestock = true,
                    Quantity = initialStock,
                    Timestamp = DateTime.UtcNow
                };
                await context.Events.AddAsync(initialStockEvent);
            }

            var stockRestoredEvent = new Event
            {
              ProductId = req.ProductId,
              IsRestock = true,
              Quantity = req.Amount,
              Timestamp = DateTime.UtcNow
            };

            await context.Events.AddAsync(stockRestoredEvent);
            await context.SaveChangesAsync();
            return Results.Ok();
          })
        .WithName("Restock")
        .WithOpenApi();

      return app;
    }

    private static async Task<ProductStock> GetStockFromEvents(EventStoreContext context, int productId)
    {
        var events = await GetEvents(context, productId);

        var productStock = ProductStock.Default(productId);

        foreach (var e in events)
        {
            IEvent @event;
            if (e.IsRestock)
            {
                @event = new StockRestored(e.ProductId, e.Quantity);
            }
            else
            {
                @event = new StockRemovalRequested(e.ProductId, e.Quantity, Guid.NewGuid());
            }
            productStock = productStock.Apply(@event);
        }
        return productStock;
    }

  private static async Task<List<Event>> GetEvents(EventStoreContext context, int productId) => await context.Events
    .Where(e => e.ProductId == productId)
    .OrderBy(e => e.Timestamp)
    .ToListAsync();

}

public interface IEvent;

public record ProductStock(int ProductId, int Stock, Guid[] FailedRequests)
{
  public static ProductStock Default(int productId) => new(productId, 0, Array.Empty<Guid>());

  public ProductStock Apply(IEvent @event) => @event switch
  {
    StockRestored e => this with { Stock = Stock + e.Amount },
    StockRemovalRequested e =>
      Stock >= e.Amount ?
      this with { Stock = Stock - e.Amount }:
      this with { FailedRequests = FailedRequests.Append(e.RequestId).ToArray() },
    _ => this
  };
}

public record StockRestored(int ProductId, int Amount) : IEvent;

public record StockRemovalRequested(int ProductId, int Amount, Guid RequestId) : IEvent;

public record RetrieveStockRequest(int ProductId, int Amount);

public record RestockRequest(int ProductId, int Amount);


