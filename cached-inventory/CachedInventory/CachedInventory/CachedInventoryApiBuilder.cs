namespace CachedInventory;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

public static class CachedInventoryApiBuilder
{
  // private static readonly ConcurrentDictionary<int, int> StockUpdates = new();
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
    // builder.Services.AddScoped<IWarehouseStockSystemClient, WarehouseStockSystemClient>();
    // builder.Services.AddSingleton<InventoryService>();
    builder.Services.AddSingleton<BatchingProcessor>();
    builder.Services.AddHostedService(provider => provider.GetRequiredService<BatchingProcessor>());

    var app = builder.Build();
    var cache = app.Services.GetRequiredService<IMemoryCache>();
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
          var stock = await GetStockFromCacheOrSource(cache, client,logger, req.ProductId);

          if (stock < req.Amount)
          {
            logger.LogInformation("❌ Not enough stock for product ID {ProductId}. Requested amount: {Amount}, Available stock: {Stock}", req.ProductId, req.Amount, stock);
            return Results.BadRequest("Not enough stock.");
          }
          cache.Set(req.ProductId, stock - req.Amount);
          BatchingProcessor.EnqueueStockUpdate(req.ProductId, stock - req.Amount);
          logger.LogInformation("📦 Retrieve stock for product ID {ProductId}. Amount: {Amount}, New stock: {NewStock}", req.ProductId, req.Amount, stock - req.Amount);
          return Results.Ok();
        })
      .WithName("RetrieveStock")
      .WithOpenApi();


    app.MapPost(
        "/stock/restock",
        async ([FromServices] IWarehouseStockSystemClient client, [FromBody] RestockRequest req, [FromServices] ILogger<BatchingProcessor> logger) =>
        {
          var stock = await GetStockFromCacheOrSource(cache, client,logger, req.ProductId);

          cache.Set(req.ProductId, stock + req.Amount);
          BatchingProcessor.EnqueueStockUpdate(req.ProductId, stock + req.Amount);

          logger.LogInformation("📦 Restock for product ID {ProductId}. Amount: {Amount}, New stock: {NewStock}", req.ProductId, req.Amount, req.Amount + stock);

          return Results.Ok();
        })
      .WithName("Restock")
      .WithOpenApi();

    return app;
  }

  private static async Task<int> GetStockFromCacheOrSource(IMemoryCache cache, IWarehouseStockSystemClient client, ILogger logger, int productId)
  {
    if (!cache.TryGetValue(productId, out int cachedStock))
    {
        cachedStock = await client.GetStock(productId);
        cache.Set(productId, cachedStock);
        logger.LogInformation("📦 Cache miss for product ID {ProductId}. Fetched from source: {Stock}", productId, cachedStock);
    }
    else
    {
        logger.LogInformation("📦 Cache hit for product ID {ProductId}: {Stock}", productId, cachedStock);
    }

    return cachedStock;
  }

}

public record RetrieveStockRequest(int ProductId, int Amount);

public record RestockRequest(int ProductId, int Amount);
