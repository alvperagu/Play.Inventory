using MassTransit;
using Play.Inventory.Service.Dtos;
using Play.Inventory.Contracts;
using Play.Common;
using Play.Inventory.Service.Entities;
using Play.Inventory.Service.Exceptions;

namespace Play.Inventory.Service.Consumers;

public class GrantItemsConsumer : IConsumer<GrantItems>
{
    private readonly IRepository<InventoryItem> inventoryItemRepository;
    private readonly IRepository<CatalogItem> catalogItemRepository;

    public GrantItemsConsumer(
        IRepository<InventoryItem> inventoryItemRepository,
        IRepository<CatalogItem> catalogItemRepository)
    {
        this.inventoryItemRepository = inventoryItemRepository;
        this.catalogItemRepository = catalogItemRepository;
    }
    
    public async Task Consume(ConsumeContext<GrantItems> context)
    {
        var message = context.Message;
        
        var item = await catalogItemRepository.GetAsync(message.CatalogItemId);

        if(item == null)
        {
            throw new UnknownItemException(message.CatalogItemId);
        }
        var inventoryItem = await inventoryItemRepository.GetAsync(item 
                 => item.UserId == message.UserId && item.CatalogItemId == message.CatalogItemId);

        if(inventoryItem == null)
        {
            inventoryItem = new InventoryItem
            {
                CatalogItemId = message.CatalogItemId,
                UserId = message.UserId,
                Quantity = message.Quantity,
                AcquiredDate = DateTimeOffset.UtcNow
            };

            inventoryItem.MessageIds.Add(context.MessageId.Value);
            await inventoryItemRepository.CreateAsync(inventoryItem);
        }        
        else
        {
            if(inventoryItem.MessageIds.Contains(context.MessageId.Value)) 
            {
                await context.Publish(new InventoryItemsGranted(message.CorrelationId));
                return;
            }
            inventoryItem.Quantity += message.Quantity;
            await inventoryItemRepository.UpdateAsync(inventoryItem);
        }   
        var itemGrantedTask = context.Publish(new InventoryItemsGranted(message.CorrelationId));
        var itemUpdatedTask = context.Publish(new InventoryItemUpdated(inventoryItem.UserId,
            inventoryItem.CatalogItemId,
            inventoryItem.Quantity));
        await Task.WhenAll(itemGrantedTask, itemUpdatedTask);
        
    }
}
