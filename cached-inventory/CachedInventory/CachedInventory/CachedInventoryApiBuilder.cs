namespace CachedInventory;

using System.Collections.Concurrent;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

public static class CachedInventoryApiBuilder
{
  private static readonly SemaphoreSlim semaphore = new(1, 1);
  public static WebApplication Build(string[] args)
  {
    var builder = WebApplication.CreateBuilder(args);

    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.AddDebug();
    // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
    // Add services to the container.
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddMemoryCache();
    builder.Services.AddSingleton<IWarehouseStockSystemClient, WarehouseStockSystemClient>();
    builder.Services.AddSingleton<BatchingProcessor>();
    builder.Services.AddHostedService(provider => provider.GetRequiredService<BatchingProcessor>());

    var app = builder.Build();
    // var cache = app.Services.GetRequiredService<IMemoryCache>();
    var cache = new ConcurrentDictionary<int, int>();
    var logger = app.Services.GetRequiredService<ILogger<BatchingProcessor>>();
    var BatchingProcessor = app.Services.GetRequiredService<BatchingProcessor>();

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
      app.UseSwagger();
      app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();

    app.MapGet(
        "/stock/{productId:int}",
        async ([FromServices] IWarehouseStockSystemClient client, int productId) =>
        {
          var stock = await GetStockFromCacheOrSource(cache, client,logger, productId);
          return Results.Json(stock);
        })
      .WithName("GetStock")
      .WithOpenApi();

    app.MapPost(
        "/stock/retrieve",
        async ( [FromServices] IWarehouseStockSystemClient client, [FromBody] RetrieveStockRequest req,[FromServices] ILogger<BatchingProcessor> logger) =>
        {
          await semaphore.WaitAsync();
          try
          {
            var stock = await GetStockFromCacheOrSource(cache, client,logger, req.ProductId);

            if (stock < req.Amount)
            {
              logger.LogInformation("❌ Not enough stock for product ID {ProductId}. Requested amount: {Amount}, Available stock: {Stock}", req.ProductId, req.Amount, stock);
              return Results.BadRequest("Not enough stock.");
            }
            var newStock = stock - req.Amount;
            cache[req.ProductId] =  newStock;
            // cache.Set(req.ProductId, newStock);
            logger.LogInformation($"🌐 Thread ID: {Thread.CurrentThread.ManagedThreadId} - 📦 Retrieve stock for product ID {req.ProductId}. Amount: {req.Amount}, New stock: {newStock}, currentStock:{stock}");
            BatchingProcessor.EnqueueStockUpdate(req.ProductId, stock - req.Amount);
            return Results.Ok();
          }
          finally
          {
            semaphore.Release();
          }
        })
      .WithName("RetrieveStock")
      .WithOpenApi();


    app.MapPost(
        "/stock/restock",
        async ([FromServices] IWarehouseStockSystemClient client, [FromBody] RestockRequest req, [FromServices] ILogger<BatchingProcessor> logger) =>
        {
          var stock = await GetStockFromCacheOrSource(cache, client,logger, req.ProductId);
          var newStock = stock + req.Amount;
          cache[req.ProductId] = newStock;
          // cache.Set(req.ProductId, newStock);

          logger.LogInformation($"🌐 Thread ID: {Thread.CurrentThread.ManagedThreadId} - 📦 Restock for product ID {req.ProductId}. Amount: {req.Amount}, New stock: {newStock}, currentStock:{stock}");
          BatchingProcessor.EnqueueStockUpdate(req.ProductId, stock + req.Amount);


          return Results.Ok();
        })
      .WithName("Restock")
      .WithOpenApi();

    return app;
  }

  // private static async Task<int> GetStockFromCacheOrSource(IMemoryCache cache, IWarehouseStockSystemClient client, ILogger logger, int productId)
  private static async Task<int> GetStockFromCacheOrSource(ConcurrentDictionary<int, int> cache, IWarehouseStockSystemClient client, ILogger logger, int productId)
  {

      if (!cache.TryGetValue(productId, out var cachedStock))
      {
          cachedStock = await client.GetStock(productId);
          // cache.Set(productId, cachedStock);
          cache[productId] = cachedStock;
          logger.LogInformation($"🌐 Thread ID: {Thread.CurrentThread.ManagedThreadId} - 📦 Cache miss for product ID {productId}. Fetched from source: {cachedStock}", productId, cachedStock);
      }
      else
      {
          logger.LogInformation($"🌐 Thread ID: {Thread.CurrentThread.ManagedThreadId} - 📦 Cache hit for product ID {productId}: {cachedStock}");
      }
      return cachedStock;

  }

}

public record RetrieveStockRequest(int ProductId, int Amount);

public record RestockRequest(int ProductId, int Amount);
