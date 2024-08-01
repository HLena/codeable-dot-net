namespace CachedInventory;

using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

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

    var app = builder.Build();

    if (app.Environment.IsDevelopment())
    {
      app.UseSwagger();
      app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();

    app.MapGet(
        "/stock/{productId:int}",
        async ([FromServices] IWarehouseStockSystemClient client, [FromServices] EventStoreContext context, int productId) =>
        {
          var stock = await GetStockFromEvents(context, productId);
          return Results.Ok(stock);
        })
      .WithName("GetStock")
      .WithOpenApi();

    app.MapGet(
        "/stock/validateOperation/{productId:int}",
        async ([FromServices] IWarehouseStockSystemClient client, [FromServices] EventStoreContext context, int productId, int amount) =>
        {
          var stock = await GetStockFromEvents(context, productId);
          var newStock = stock - amount;
          if(newStock < 0)
          {
            return Results.BadRequest("Not enough stock.");
          }
          return Results.Ok();
        })
      .WithName("CalculateStock")
      .WithOpenApi();


    app.MapPost(
      "/stock/retrieve",
      async ([FromServices] IWarehouseStockSystemClient client, [FromServices] EventStoreContext context, [FromBody] RetrieveStockRequest req) =>
      {
        var events = await GetEvents(context, req.ProductId);
        var stock = 0;
        if(events.Count == 0)
        {
          stock = await client.GetStock(req.ProductId);
          var stockRestockEvent = new Event
          {
            ProductId = req.ProductId,
            Type = "restock",
            Quantity = stock,
            Timestamp = DateTime.UtcNow
          };

          await context.Events.AddAsync(stockRestockEvent);
          await context.SaveChangesAsync();
        }
        stock = await GetStockFromEvents(context, req.ProductId);


        var newStock = stock - req.Amount;
        var stockRetrieveEvent = new Event
        {
          ProductId = req.ProductId,
          Type = "retrieve",
          Quantity = req.Amount,
          Timestamp = DateTime.UtcNow
        };
        await context.Events.AddAsync(stockRetrieveEvent);
        await context.SaveChangesAsync();
        // await client.UpdateStock(req.ProductId, stock - req.Amount);
        Results.Redirect($"/stock/validateOperation/{req.ProductId}?amount={req.Amount}");
      })
    .WithName("RetrieveStock")
    .WithOpenApi();


    app.MapPost(
          "/stock/restock",
          async ([FromServices] IWarehouseStockSystemClient client, [FromServices] EventStoreContext context, [FromBody] RestockRequest req) =>
          {
            // var stock = await client.GetStock(req.ProductId);
            var stock = await GetStockFromEvents(context, req.ProductId);
            var newStock = stock + req.Amount;
            var stockRestockEvent = new Event
            {
              ProductId = req.ProductId,
              Type = "restock",
              Quantity = req.Amount,
              Timestamp = DateTime.UtcNow
            };

            await context.Events.AddAsync(stockRestockEvent);
            await context.SaveChangesAsync();
            // await client.UpdateStock(req.ProductId, req.Amount + stock);
            return Results.Ok();
          })
        .WithName("Restock")
        .WithOpenApi();

      return app;
    }

    private static async Task<int> GetStockFromEvents(EventStoreContext context, int productId)
    {
        var events = await GetEvents(context, productId);
        var stock = 0;

        foreach (var e in events)
        {
            if (e.Type == "retrieve")
            {
                stock -= e.Quantity;
            }
            else if (e.Type == "restock")
            {
                stock += e.Quantity;
            }
        }
        return stock;
    }

  private static async Task<List<Event>> GetEvents(EventStoreContext context, int productId) => await context.Events
    .Where(e => e.ProductId == productId)
    .OrderBy(e => e.Timestamp)
    .ToListAsync();


}

public record RetrieveStockRequest(int ProductId, int Amount);

public record RestockRequest(int ProductId, int Amount);
