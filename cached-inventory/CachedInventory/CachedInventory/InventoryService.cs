namespace CachedInventory;

using System.Collections.Concurrent;
using Microsoft.Extensions.Caching.Memory;

public class InventoryService
{
    private readonly WarehouseStockSystemClient warehouseClient;
    private readonly IMemoryCache cache;
    public readonly ILogger<InventoryService> logger;
    private readonly ConcurrentDictionary<int, int> updateDictionary;
    // private readonly ConcurrentQueue<(int productId, int amount)> updateDictionary;

    private readonly Timer timer;

    public InventoryService(WarehouseStockSystemClient warehouseClient, IMemoryCache cache, ILogger<InventoryService> logger)
    {
        this.warehouseClient = warehouseClient;
        this.cache = cache;
        this.logger = logger;

        updateDictionary = new ConcurrentDictionary<int, int>();
        timer = new Timer(ProcessQueue, null, TimeSpan.Zero, TimeSpan.FromSeconds(5));
    }

    public async Task<int> GetProductStock(int productId)
    {
        if (!cache.TryGetValue(productId, out int stock))
        {
          logger.LogInformation("🔍 Cache miss for product ID {ProductId}. Fetching stock from source.", productId);
          stock = await warehouseClient.GetStock(productId);
          cache.Set(productId, stock);
          logger.LogInformation("📥 Fetched stock for product ID {ProductId}: {Stock}", productId, stock);
        }
        else
        {
          logger.LogInformation("✅ Cache hit for product ID {ProductId}: {Stock}", productId, stock);
        }

        return stock;
    }

    public void SaveStockUpdate(int productId, int newAmount)
    {
        cache.Set(productId, newAmount);
        logger.LogInformation("🔄 Updated cache for product ID {ProductId} with new stock {NewStock}.", productId, newAmount);

        updateDictionary.AddOrUpdate(productId, newAmount, (key, existingAmount) => existingAmount + newAmount);
        logger.LogInformation("📥 Queued stock update for product ID {ProductId} with amount {Amount}.", productId, newAmount);
    }

    private async void ProcessQueue(object? state)
    {
        logger.LogInformation("🔄 Processing queue...");
        foreach (var update in updateDictionary)
        {
            var productId = update.Key;
            var newStock = update.Value;

            logger.LogInformation("🔄 Processing update for product ID {ProductId} with new stock {NewStock}.", productId, newStock);
            await UpdateProductStock(productId, newStock);

            updateDictionary.TryRemove(productId, out _);
        }
        logger.LogInformation("✅ Updates processing completed.");
    }

    private async Task UpdateProductStock(int productId, int newAmount)
    {
        await warehouseClient.UpdateStock(productId, newAmount);
        cache.Set(productId, newAmount, TimeSpan.FromMinutes(10));
        logger.LogInformation("💾 Stock updated for product ID {ProductId} to new amount {NewStock}.", productId, newAmount);
    }


}
