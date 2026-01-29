// Services/ValidatorService.cs
namespace TradeOMS.Services
{
    public class ValidationResult
    {
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; }
    }
    
    public interface IValidatorService
    {
        Task<ValidationResult> ValidateOrderAsync(Order order);
    }
    
    public class ValidatorService : IValidatorService
    {
        private readonly TradeOMSContext _context;
        
        public ValidatorService(TradeOMSContext context)
        {
            _context = context;
        }
        
        public async Task<ValidationResult> ValidateOrderAsync(Order order)
        {
            // 1. Check investor account status
            var investor = await _context.Investors
                .FirstOrDefaultAsync(i => i.InvestorId == order.InvestorId);
                
            if (investor == null)
                return ValidationResult.Fail("Investor not found");
                
            if (investor.AccountStatus != "ACTIVE")
                return ValidationResult.Fail($"Account is {investor.AccountStatus}");
            
            // 2. Check asset is active
            var asset = await _context.Assets
                .FirstOrDefaultAsync(a => a.AssetId == order.AssetId);
                
            if (asset == null || !asset.IsActive)
                return ValidationResult.Fail("Asset is not available for trading");
            
            // 3. Validate order parameters
            if (order.Quantity <= 0)
                return ValidationResult.Fail("Quantity must be positive");
                
            if (order.Price.HasValue && order.Price <= 0)
                return ValidationResult.Fail("Price must be positive");
            
            // 4. For SELL orders: check holdings
            if (order.OrderType == OrderType.Sell)
            {
                var holding = await _context.Holdings
                    .FirstOrDefaultAsync(h => h.InvestorId == order.InvestorId 
                                           && h.AssetId == order.AssetId);
                    
                if (holding == null || holding.Quantity < order.Quantity)
                {
                    var available = holding?.Quantity ?? 0;
                    return ValidationResult.Fail(
                        $"Insufficient holdings. Available: {available}, " +
                        $"Requested: {order.Quantity}");
                }
            }
            
            // 5. Market data validation (simplified)
            if (!order.Price.HasValue) // Market order
            {
                if (asset.CurrentPrice <= 0)
                    return ValidationResult.Fail("Invalid market price for asset");
            }
            
            return ValidationResult.Success();
        }
    }
}