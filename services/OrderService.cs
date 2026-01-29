// Services/OrderService.cs
namespace TradeOMS.Services
{
    public interface IOrderService
    {
        Task<Order> CreateOrderAsync(CreateOrderRequest request, Guid? idempotencyKey = null);
        Task<Order> GetOrderAsync(Guid orderId);
        Task CancelOrderAsync(Guid orderId, string reason);
        Task<List<Order>> GetInvestorOrdersAsync(int investorId, DateTime? fromDate = null);
    }
    
    public class OrderService : IOrderService
    {
        private readonly TradeOMSContext _context;
        private readonly ILogger<OrderService> _logger;
        private readonly IValidatorService _validator;
        
        public OrderService(TradeOMSContext context, ILogger<OrderService> logger, 
                           IValidatorService validator)
        {
            _context = context;
            _logger = logger;
            _validator = validator;
        }
        
        public async Task<Order> CreateOrderAsync(CreateOrderRequest request, 
                                                Guid? idempotencyKey = null)
        {
            // 1. Idempotency check
            if (idempotencyKey.HasValue)
            {
                var existingOrder = await _context.Orders
                    .FirstOrDefaultAsync(o => o.IdempotencyKey == idempotencyKey.Value);
                if (existingOrder != null)
                {
                    _logger.LogWarning("Duplicate order detected with idempotency key: {Key}", 
                                     idempotencyKey.Value);
                    return existingOrder;
                }
            }
            
            // 2. Create order in NEW state
            var order = new Order
            {
                OrderId = Guid.NewGuid(),
                InvestorId = request.InvestorId,
                AssetId = request.AssetId,
                OrderType = request.OrderType,
                Quantity = request.Quantity,
                Price = request.Price,
                OrderStatus = OrderStatus.New,
                OrderDate = DateTime.UtcNow,
                IdempotencyKey = idempotencyKey
            };
            
            await _context.Orders.AddAsync(order);
            await LogStateChangeAsync(order.OrderId, null, OrderStatus.New.ToString(), 
                                    "Order created");
            
            // 3. Start validation workflow
            _ = Task.Run(() => ProcessOrderAsync(order.OrderId));
            
            await _context.SaveChangesAsync();
            return order;
        }
        
        private async Task ProcessOrderAsync(Guid orderId)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();
            
            try
            {
                var order = await _context.Orders
                    .Include(o => o.Investor)
                    .Include(o => o.Asset)
                    .FirstOrDefaultAsync(o => o.OrderId == orderId);
                    
                if (order == null) return;
                
                // Step 1: Move to VALIDATING
                await UpdateOrderStatusAsync(order, OrderStatus.Validating, 
                                           "Starting validation");
                
                // Step 2: Validate order
                var validationResult = await _validator.ValidateOrderAsync(order);
                
                if (!validationResult.IsValid)
                {
                    await UpdateOrderStatusAsync(order, OrderStatus.Rejected, 
                                               validationResult.ErrorMessage);
                    await transaction.CommitAsync();
                    return;
                }
                
                // Step 3: Move to VALIDATED
                await UpdateOrderStatusAsync(order, OrderStatus.Validated, 
                                           "Order validated successfully");
                
                // Step 4: Execute trade
                await ExecuteTradeAsync(order);
                
                // Step 5: Schedule settlement (T+2)
                ScheduleSettlement(order);
                
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                _logger.LogError(ex, "Error processing order {OrderId}", orderId);
                
                // Update order to error state
                await UpdateOrderStatusAsync(orderId, OrderStatus.Rejected, 
                                           $"System error: {ex.Message}");
            }
        }
        
        private async Task UpdateOrderStatusAsync(Order order, OrderStatus newStatus, 
                                                string reason)
        {
            var oldStatus = order.OrderStatus;
            order.OrderStatus = newStatus;
            
            if (newStatus == OrderStatus.Filled)
            {
                order.ExecutedDate = DateTime.UtcNow;
            }
            
            await LogStateChangeAsync(order.OrderId, oldStatus.ToString(), 
                                    newStatus.ToString(), reason);
        }
        
        private async Task ExecuteTradeAsync(Order order)
        {
            // Move to EXECUTING
            await UpdateOrderStatusAsync(order, OrderStatus.Executing, 
                                       "Starting trade execution");
            
            // Get current market price (simulated)
            decimal executionPrice = order.Price ?? order.Asset.CurrentPrice;
            
            using var executionTransaction = await _context.Database.BeginTransactionAsync();
            
            try
            {
                // 1. Create trade record
                var trade = new Trade
                {
                    TradeId = Guid.NewGuid(),
                    OrderId = order.OrderId,
                    InvestorId = order.InvestorId,
                    AssetId = order.AssetId,
                    Quantity = order.Quantity,
                    Price = executionPrice,
                    TradeDate = DateTime.UtcNow,
                    Side = order.OrderType.ToString()
                };
                
                await _context.Trades.AddAsync(trade);
                
                // 2. Update holdings
                await UpdateHoldingsAsync(order, executionPrice);
                
                // 3. Mark order as FILLED
                await UpdateOrderStatusAsync(order, OrderStatus.Filled, 
                                           $"Trade executed at {executionPrice}");
                
                await _context.SaveChangesAsync();
                await executionTransaction.CommitAsync();
                
                _logger.LogInformation("Trade executed for order {OrderId}, investor {InvestorId}", 
                                     order.OrderId, order.InvestorId);
            }
            catch (Exception ex)
            {
                await executionTransaction.RollbackAsync();
                throw new InvalidOperationException($"Trade execution failed: {ex.Message}", ex);
            }
        }
        
        private async Task UpdateHoldingsAsync(Order order, decimal executionPrice)
        {
            var holding = await _context.Holdings
                .FirstOrDefaultAsync(h => h.InvestorId == order.InvestorId 
                                       && h.AssetId == order.AssetId);
            
            if (order.OrderType == OrderType.Buy)
            {
                if (holding == null)
                {
                    // First time buying this asset
                    holding = new Holding
                    {
                        InvestorId = order.InvestorId,
                        AssetId = order.AssetId,
                        Quantity = order.Quantity,
                        AverageCost = executionPrice,
                        UpdatedAt = DateTime.UtcNow
                    };
                    await _context.Holdings.AddAsync(holding);
                }
                else
                {
                    // Update existing holding with new average cost
                    var totalCost = (holding.Quantity * holding.AverageCost) 
                                  + (order.Quantity * executionPrice);
                    var totalQuantity = holding.Quantity + order.Quantity;
                    
                    holding.AverageCost = totalCost / totalQuantity;
                    holding.Quantity = totalQuantity;
                    holding.UpdatedAt = DateTime.UtcNow;
                }
            }
            else // SELL order
            {
                if (holding == null || holding.Quantity < order.Quantity)
                {
                    throw new InvalidOperationException(
                        $"Insufficient holdings to sell. Available: {holding?.Quantity ?? 0}, " +
                        $"Requested: {order.Quantity}");
                }
                
                holding.Quantity -= order.Quantity;
                
                // If quantity becomes zero, we can optionally remove the holding
                if (holding.Quantity == 0)
                {
                    _context.Holdings.Remove(holding);
                }
                else
                {
                    holding.UpdatedAt = DateTime.UtcNow;
                }
            }
        }
        
        private void ScheduleSettlement(Order order)
        {
            // Simulate T+2 settlement (2 business days after trade)
            Task.Delay(TimeSpan.FromSeconds(10)).ContinueWith(async _ =>
            {
                using var scope = _context.GetServiceScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<TradeOMSContext>();
                
                var settledOrder = await dbContext.Orders
                    .FirstOrDefaultAsync(o => o.OrderId == order.OrderId);
                    
                if (settledOrder != null && settledOrder.OrderStatus == OrderStatus.Filled)
                {
                    settledOrder.OrderStatus = OrderStatus.Settled;
                    settledOrder.SettlementDate = DateTime.UtcNow;
                    
                    await LogStateChangeAsync(dbContext, order.OrderId, 
                                            OrderStatus.Filled.ToString(),
                                            OrderStatus.Settled.ToString(),
                                            "Settlement completed (T+2)");
                    
                    await dbContext.SaveChangesAsync();
                }
            });
        }
        
        private async Task LogStateChangeAsync(Guid orderId, string fromStatus, 
                                             string toStatus, string reason)
        {
            var log = new OrderStateLog
            {
                OrderId = orderId,
                FromStatus = fromStatus,
                ToStatus = toStatus,
                Reason = reason,
                LoggedBy = "System",
                LoggedAt = DateTime.UtcNow
            };
            
            await _context.OrderStateLogs.AddAsync(log);
        }
    }
}