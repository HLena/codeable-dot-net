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
        async ([FromServices] IWarehouseStockSystemClient client, int productId) => await client.GetStock(productId))
      .WithName("GetStock")
      .WithOpenApi();


    app.MapPost(
      "/stock/retrieve",
      async ([FromServices] IWarehouseStockSystemClient client, [FromBody] RetrieveStockRequest req) =>
      {
        var stock = await client.GetStock(req.ProductId);
        if (stock < req.Amount)
        {
          return Results.BadRequest("Not enough stock.");
        }

        await client.UpdateStock(req.ProductId, stock - req.Amount);
        return Results.Ok();
      })
    .WithName("RetrieveStock")
      .WithOpenApi();


    app.MapPost(
          "/stock/restock",
          async ([FromServices] IWarehouseStockSystemClient client, [FromBody] RestockRequest req) =>
          {
            var stock = await client.GetStock(req.ProductId);
            await client.UpdateStock(req.ProductId, req.Amount + stock);
            return Results.Ok();
          })
        .WithName("Restock")
        .WithOpenApi();

      return app;
    }

    private static async Task<int> GetStockFromEvents(EventStoreContext context, int productId)
    {
        var events = await context.Events
            .Where(e => e.ProductId == productId)
            .OrderBy(e => e.Timestamp)
            .ToListAsync();

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
}

public record RetrieveStockRequest(int ProductId, int Amount);

public record RestockRequest(int ProductId, int Amount);
