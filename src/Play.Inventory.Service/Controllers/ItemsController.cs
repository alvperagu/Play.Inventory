using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using MassTransit;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;
using Play.Common;
using Play.Inventory.Contracts;
using Play.Inventory.Service.Clients;
using Play.Inventory.Service.Dtos;
using Play.Inventory.Service.Entities;

namespace Play.Inventory.Service.Controllers;

[ApiController]
[Route("[controller]")]
public class ItemsController : ControllerBase
{
    private const string AdminRole = "Admin";
    private readonly IRepository<InventoryItem> inventoryItemRepository;
    private readonly IRepository<CatalogItem> catalogItemRepository;
    private readonly IPublishEndpoint publishEndpoint;
 
    public ItemsController(
        IRepository<InventoryItem> inventoryItemRepository,
        IRepository<CatalogItem> catalogItemRepository,
        IPublishEndpoint publishEndpoint)
    {
        this.inventoryItemRepository = inventoryItemRepository;
        this.catalogItemRepository = catalogItemRepository;
        this.publishEndpoint = publishEndpoint;
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<IEnumerable<InventoryItemDto>>> GetAsync(Guid userId)
    {
        if (userId == Guid.Empty)
        {
            return BadRequest();
        }
         //sub claims includes the userid
        var currentUserId = User.FindFirstValue(JwtRegisteredClaimNames.Sub);

        if(Guid.Parse(currentUserId) != userId)
        {
            if(!User.IsInRole(AdminRole))
            {
                return Forbid();
            }
        }
       
        var inventoryItemEntities = await inventoryItemRepository.GetAllAsync(item => item.UserId == userId);
        var itemsIds = inventoryItemEntities.Select(x=>x.CatalogItemId);
        var catalogItemEntities = await catalogItemRepository.GetAllAsync(item => itemsIds.Contains(item.Id));

        var inventoryItemDtos = inventoryItemEntities.Select(inventoryItem => 
        {
            var catalogItem = catalogItemEntities.Single(catalogItem => catalogItem.Id == inventoryItem.CatalogItemId);
            return inventoryItem.AsDto(catalogItem.Name, catalogItem.Description);
        });
        return Ok(inventoryItemDtos);
    }
           
    [HttpPost]
    [Authorize(Roles = AdminRole)]
    public async Task<ActionResult> PostAsync(GrantItemsDto grantItemsDto)
    {
        var inventoryItem = await inventoryItemRepository.GetAsync(item 
                    => item.UserId == grantItemsDto.UserId && item.CatalogItemId == grantItemsDto.CatalogItemId);

        if(inventoryItem == null)
        {
                inventoryItem = new InventoryItem{
                    CatalogItemId = grantItemsDto.CatalogItemId,
                    UserId = grantItemsDto.UserId,
                    Quantity = grantItemsDto.Quantity,
                    AcquiredDate = DateTimeOffset.UtcNow,
                };
                await inventoryItemRepository.CreateAsync(inventoryItem);
        }        
        else
        {
                inventoryItem.Quantity += grantItemsDto.Quantity;
                await inventoryItemRepository.UpdateAsync(inventoryItem);
        }

        await publishEndpoint.Publish(new InventoryItemUpdated(
            inventoryItem.UserId,
            inventoryItem.CatalogItemId,
            inventoryItem.Quantity
        ));
        return Ok();
    }
}
