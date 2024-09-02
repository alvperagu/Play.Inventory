using MassTransit;
using Play.Inventory.Contracts;
using Play.Common;
using Play.Inventory.Service.Entities;
using Play.Inventory.Service.Exceptions;

namespace Play.Inventory.Service.Consumers;

public class SubtractItemsConsumer : IConsumer<SubtractItems>
{
    private readonly IRepository<InventoryItem> inventoryItemRepository;
    private readonly IRepository<CatalogItem> catalogItemRepository;

    public SubtractItemsConsumer(
        IRepository<InventoryItem> inventoryItemRepository,
        IRepository<CatalogItem> catalogItemRepository)
    {
        this.inventoryItemRepository = inventoryItemRepository;
        this.catalogItemRepository = catalogItemRepository;
    }
    
    public async Task Consume(ConsumeContext<SubtractItems> context)
    {
        var message = context.Message;
        
        var item = await catalogItemRepository.GetAsync(message.CatalogItemId);

        if(item == null)
        {
            throw new UnknownItemException(message.CatalogItemId);
        }
        var inventoryItem = await inventoryItemRepository.GetAsync(item 
                 => item.UserId == message.UserId && item.CatalogItemId == message.CatalogItemId);

        if(inventoryItem != null)
        {
            if(inventoryItem.MessageIds.Contains(context.MessageId.Value)) 
            {
                await context.Publish(new InventoryItemsSubtracted(message.CorrelationId));
                return;
            }
            
            inventoryItem.Quantity -= message.Quantity;
            inventoryItem.MessageIds.Add(context.MessageId.Value);
            await inventoryItemRepository.UpdateAsync(inventoryItem);
           
            await context.Publish(new InventoryItemUpdated(
                inventoryItem.UserId,
                inventoryItem.CatalogItemId,
                inventoryItem.Quantity));
        }        
        
        await context.Publish(new InventoryItemsSubtracted(message.CorrelationId));

    }
}
