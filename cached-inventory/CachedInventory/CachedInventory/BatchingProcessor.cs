using System.Collections.Concurrent;
using CachedInventory;

public class BatchingProcessor  : BackgroundService
{
    private readonly IServiceScopeFactory scopeFactory;
    private readonly ConcurrentDictionary<int, int> stockUpdates;
    private readonly ILogger<BatchingProcessor> logger;
    private readonly SemaphoreSlim semaphore = new(1, 1);
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
        logger.LogInformation($"🌐 Thread ID: {Thread.CurrentThread.ManagedThreadId} - Enqueued update for Product ID {productId} with new amount {newAmount}.");
        stockUpdates[productId] = newAmount; // Añade o reemplaza la cantidad existente para el producto

    }


  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var warehouseStockSystem = scope.ServiceProvider.GetRequiredService<IWarehouseStockSystemClient>();

        while (!stoppingToken.IsCancellationRequested)
        {
          logger.LogInformation($"🌐 Thread ID: {Thread.CurrentThread.ManagedThreadId} - Waiting for stock updates...");
          // logger.LogInformation("🔄 Waiting for the next batch update cycle...");
          await Task.Delay(updateInterval, stoppingToken);
          await ExecutePendingUpdatesAsync(warehouseStockSystem);
        }
    }

    // Método para procesar las actualizaciones pendientes de stock
    private async Task ExecutePendingUpdatesAsync(IWarehouseStockSystemClient warehouseStockSystem)
    {

      await semaphore.WaitAsync();
      try
      {
        if (stockUpdates.IsEmpty)
        {
            logger.LogInformation($"🌐 Thread ID: {Thread.CurrentThread.ManagedThreadId} -🟢 No stock updates to process.");
            return;
        }

        logger.LogInformation(" ⚙️ Processing stock updates...");
        foreach(var update in stockUpdates.ToList())
        {
          if (stockUpdates.TryRemove(update.Key, out var amount))
          {
              logger.LogInformation($"🌐 Thread ID: {Thread.CurrentThread.ManagedThreadId} - 📤 Updating product ID {update.Key} to new amount {amount}.");
              await warehouseStockSystem.UpdateStock(update.Key, amount);
              logger.LogInformation($"🌐 Thread ID: {Thread.CurrentThread.ManagedThreadId} - Successfully updated product ID {update.Key} to new amount {amount}.");

          }

        }
      }
      catch (Exception ex)
      {
        logger.LogError(ex, $"🌐 Thread ID: {Thread.CurrentThread.ManagedThreadId} -⚠️ Error processing stock updates");
      }
      finally
      {
        semaphore.Release();
      }
      logger.LogInformation($"🔚 Finished processing stock updates.");
    }
}
