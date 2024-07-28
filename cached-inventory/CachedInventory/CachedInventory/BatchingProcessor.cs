using System.Collections.Concurrent;
using CachedInventory;

public class BatchingProcessor  : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ConcurrentDictionary<int, int> stockUpdates;
    private readonly ILogger<BatchingProcessor> logger;
    private readonly TimeSpan updateInterval = TimeSpan.FromSeconds(1);

    public BatchingProcessor(IServiceScopeFactory scopeFactory, ILogger<BatchingProcessor> logger)
    {
        this.scopeFactory = scopeFactory;
        stockUpdates = new ConcurrentDictionary<int, int>();
        this.logger = logger;
    }

    // Método para añadir o actualizar una entrada de stock en la cola
    public void EnqueueStockUpdate(int productId, int newAmount)
    {
        logger.LogWarning("Enqueued update for Product ID {ProductId} with new amount {Amount}.", productId, newAmount);
        stockUpdates[productId] = newAmount; // Añade o reemplaza la cantidad existente para el producto
    }


  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var warehouseStockSystem = scope.ServiceProvider.GetRequiredService<IWarehouseStockSystemClient>();

        while (!stoppingToken.IsCancellationRequested)
        {
          logger.LogInformation("🔄 Waiting for the next batch update cycle...");
          await Task.Delay(updateInterval, stoppingToken);
          await ExecutePendingUpdatesAsync(warehouseStockSystem);
        }
    }

    // Método para procesar las actualizaciones pendientes de stock
    private async Task ExecutePendingUpdatesAsync(IWarehouseStockSystemClient warehouseStockSystem)
    {
      logger.LogInformation("🔄 Processing stock updates...");
      try
      {
          foreach (var update in stockUpdates.ToList())
          {
              if (stockUpdates.TryRemove(update.Key, out var amount))
              {
                  logger.LogInformation("📤 Updating product ID {ProductId} to new amount {Amount}.", update.Key, amount);
                  await warehouseStockSystem.UpdateStock(update.Key, amount);
                  logger.LogInformation("✅ Successfully updated product ID {ProductId} to new amount {Amount}.", update.Key, amount);
              }
          }
      }
      catch (Exception ex)
      {
        logger.LogError(ex, "⚠️ Error processing stock updates");
      }
      logger.LogInformation("🔄 Finished processing stock updates.");
    }
}
