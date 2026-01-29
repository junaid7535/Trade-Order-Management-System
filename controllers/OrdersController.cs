
// Controllers/OrdersController.cs
namespace TradeOMS.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class OrdersController : ControllerBase
    {
        private readonly IOrderService _orderService;
        private readonly ILogger<OrdersController> _logger;
        
        public OrdersController(IOrderService orderService, ILogger<OrdersController> logger)
        {
            _orderService = orderService;
            _logger = logger;
        }
        
        [HttpPost]
        public async Task<IActionResult> CreateOrder([FromBody] CreateOrderRequest request)
        {
            // Extract idempotency key from header
            Guid? idempotencyKey = null;
            if (Request.Headers.TryGetValue("Idempotency-Key", out var idempotencyHeader))
            {
                if (Guid.TryParse(idempotencyHeader, out var parsedKey))
                {
                    idempotencyKey = parsedKey;
                }
            }
            
            try
            {
                var order = await _orderService.CreateOrderAsync(request, idempotencyKey);
                return Accepted(new { orderId = order.OrderId, status = order.OrderStatus });
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error creating order");
                return StatusCode(500, new { error = ex.Message });
            }
        }
        
        [HttpGet("{orderId}")]
        public async Task<IActionResult> GetOrder(Guid orderId)
        {
            var order = await _orderService.GetOrderAsync(orderId);
            if (order == null)
                return NotFound();
                
            return Ok(order);
        }
        
        [HttpGet("investor/{investorId}")]
        public async Task<IActionResult> GetInvestorOrders(int investorId, 
                                                         [FromQuery] DateTime? fromDate = null)
        {
            var orders = await _orderService.GetInvestorOrdersAsync(investorId, fromDate);
            return Ok(orders);
        }
        
        [HttpPost("{orderId}/cancel")]
        public async Task<IActionResult> CancelOrder(Guid orderId, 
                                                   [FromBody] CancelOrderRequest request)
        {
            try
            {
                await _orderService.CancelOrderAsync(orderId, request.Reason);
                return Ok(new { message = "Order cancellation requested" });
            }
            catch (InvalidOperationException ex)
            {
                return BadRequest(new { error = ex.Message });
            }
        }
    }
}