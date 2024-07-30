using System.Collections.Concurrent;
using CachedInventory;

public class StockUpdateScheduler  : BackgroundService
{
  private readonly IServiceScopeFactory scopeFactory;
  private readonly ConcurrentDictionary<int, int> stockUpdates;
  private readonly ConcurrentDictionary<int, Timer> timers;
  private readonly ILogger<StockUpdateScheduler> logger;
  private readonly TimeSpan updateInterval = TimeSpan.FromSeconds(1);

  public StockUpdateScheduler(IServiceScopeFactory scopeFactory, ILogger<StockUpdateScheduler> logger)
  {
      this.scopeFactory = scopeFactory;
      stockUpdates = new ConcurrentDictionary<int, int>();
      timers = new ConcurrentDictionary<int, Timer>();
      this.logger = logger;
  }

  public void EnqueueStockUpdate(int productId, int newAmount)
  {
      logger.LogInformation($"🌐 Thread ID: {Thread.CurrentThread.ManagedThreadId} - Enqueued update for Product ID {productId} with new amount {newAmount}.");
      stockUpdates[productId] = newAmount;
      ScheduleStockUpdate(productId);

  }

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = scopeFactory.CreateScope();
        var warehouseStockSystem = scope.ServiceProvider.GetRequiredService<IWarehouseStockSystemClient>();

        while (!stoppingToken.IsCancellationRequested)
        {
            logger.LogInformation($"🌐 Thread ID: {Thread.CurrentThread.ManagedThreadId} - Waiting for stock updates...");
            await Task.Delay(updateInterval, stoppingToken);
        }
    }

    private void ScheduleStockUpdate(int productId)
    {
        if (timers.TryGetValue(productId, out var existingTimer))
        {
            existingTimer.Change(2500, Timeout.Infinite);
        }
        else
        {
            var newTimer = new Timer(async state =>
            {
                var pid = (int)state!;
                try
                {
                    if (stockUpdates.TryGetValue(pid, out var stock))
                    {
                        using var scope = scopeFactory.CreateScope();
                        var warehouseStockSystem = scope.ServiceProvider.GetRequiredService<IWarehouseStockSystemClient>();
                        await warehouseStockSystem.UpdateStock(pid, stock);
                        logger.LogInformation($"🌐 Thread ID: {Thread.CurrentThread.ManagedThreadId} - Successfully updated product ID {pid} to new amount {stock}.");
                        stockUpdates.TryRemove(pid, out _);
                    }
                }
                finally
                {
                }
            }, productId, 2500, Timeout.Infinite);
            timers[productId] = newTimer;
        }
    }

}
