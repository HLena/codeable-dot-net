namespace CachedInventory;

using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;

public static class CachedInventoryApiBuilder
{
  public static WebApplication Build(string[] args)
  {
    var builder = WebApplication.CreateBuilder(args);

    builder.Logging.ClearProviders();
    builder.Logging.AddConsole();
    builder.Logging.AddDebug();
    // Add services to the container.
    // Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();
    builder.Services.AddMemoryCache();
    // builder.Services.AddScoped<IWarehouseStockSystemClient, WarehouseStockSystemClient>();
    builder.Services.AddSingleton<WarehouseStockSystemClient>();
    builder.Services.AddSingleton<InventoryService>();

    var app = builder.Build();

    // Configure the HTTP request pipeline.
    if (app.Environment.IsDevelopment())
    {
      app.UseSwagger();
      app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();

    app.MapGet(
        "/stock/{productId:int}",
        async ([FromServices] InventoryService  service, int productId) =>
        {
          var stock = await service.GetProductStock(productId);
          service.logger.LogInformation("📦 Retrieved stock for product ID {ProductId}: {Stock}", productId, stock);
          return Results.Json(stock);
        })
      .WithName("GetStock")
      .WithOpenApi();

    app.MapPost(
        "/stock/retrieve",
        async ([FromServices] InventoryService  service, [FromBody] RetrieveStockRequest req) =>
        {
          var stock = await service.GetProductStock(req.ProductId);
          if (stock < req.Amount)
          {
            service.logger.LogInformation("❌ Not enough stock for product ID {ProductId}. Requested amount: {Amount}, Available stock: {Stock}", req.ProductId, req.Amount, stock);
            return Results.BadRequest("Not enough stock.");
          }

          service.SaveStockUpdate(req.ProductId, stock - req.Amount);
          service.logger.LogInformation("📦 Retrieve stock for product ID {ProductId}. Amount: {Amount}, New stock: {NewStock}", req.ProductId, req.Amount, stock - req.Amount);
          return Results.Ok();
        })
      .WithName("RetrieveStock")
      .WithOpenApi();


    app.MapPost(
        "/stock/restock",
        async ([FromServices] InventoryService  service, [FromBody] RestockRequest req) =>
        {
          var stock = await service.GetProductStock(req.ProductId);
          service.SaveStockUpdate(req.ProductId, req.Amount + stock);
          service.logger.LogInformation("📦 Restock for product ID {ProductId}. Amount: {Amount}, New stock: {NewStock}", req.ProductId, req.Amount, req.Amount + stock);
          return Results.Ok();
        })
      .WithName("Restock")
      .WithOpenApi();

    return app;
  }
}

public record RetrieveStockRequest(int ProductId, int Amount);

public record RestockRequest(int ProductId, int Amount);
