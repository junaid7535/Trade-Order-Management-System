// components/OrderForm.jsx
import React, { useState, useEffect } from 'react';
import { 
  createOrder, 
  getAssets, 
  getInvestorHoldings 
} from '../services/api';
import { useIdempotencyKey } from '../hooks/useIdempotencyKey';

const OrderForm = ({ investorId }) => {
  const [assets, setAssets] = useState([]);
  const [holdings, setHoldings] = useState([]);
  const [loading, setLoading] = useState(false);
  const [formData, setFormData] = useState({
    assetId: '',
    orderType: 'BUY',
    quantity: '',
    price: '',
    isMarketOrder: false
  });
  
  const { generateIdempotencyKey } = useIdempotencyKey();
  
  useEffect(() => {
    loadAssets();
    loadHoldings();
  }, [investorId]);
  
  const loadAssets = async () => {
    try {
      const data = await getAssets();
      setAssets(data);
    } catch (error) {
      console.error('Failed to load assets:', error);
    }
  };
  
  const loadHoldings = async () => {
    try {
      const data = await getInvestorHoldings(investorId);
      setHoldings(data);
    } catch (error) {
      console.error('Failed to load holdings:', error);
    }
  };
  
  const handleSubmit = async (e) => {
    e.preventDefault();
    setLoading(true);
    
    try {
      const idempotencyKey = generateIdempotencyKey();
      const payload = {
        investorId,
        assetId: parseInt(formData.assetId),
        orderType: formData.orderType,
        quantity: parseFloat(formData.quantity),
        price: formData.isMarketOrder ? null : parseFloat(formData.price)
      };
      
      const result = await createOrder(payload, idempotencyKey);
      
      alert(`Order submitted! Order ID: ${result.orderId}`);
      
      // Reset form
      setFormData({
        assetId: '',
        orderType: 'BUY',
        quantity: '',
        price: '',
        isMarketOrder: false
      });
    } catch (error) {
      alert(`Error: ${error.message}`);
    } finally {
      setLoading(false);
    }
  };
  
  const selectedAsset = assets.find(a => a.assetId === parseInt(formData.assetId));
  const availableToSell = holdings.find(h => 
    h.assetId === parseInt(formData.assetId))?.quantity || 0;
  
  return (
    <div className="order-form">
      <h2>Place New Order</h2>
      <form onSubmit={handleSubmit}>
        <div className="form-group">
          <label>Asset:</label>
          <select 
            value={formData.assetId}
            onChange={(e) => setFormData({...formData, assetId: e.target.value})}
            required
          >
            <option value="">Select Asset</option>
            {assets.map(asset => (
              <option key={asset.assetId} value={asset.assetId}>
                {asset.symbol} - {asset.name} (${asset.currentPrice})
              </option>
            ))}
          </select>
        </div>
        
        <div className="form-group">
          <label>Order Type:</label>
          <div className="order-type-buttons">
            <button 
              type="button"
              className={`btn ${formData.orderType === 'BUY' ? 'btn-buy' : ''}`}
              onClick={() => setFormData({...formData, orderType: 'BUY'})}
            >
              BUY
            </button>
            <button 
              type="button"
              className={`btn ${formData.orderType === 'SELL' ? 'btn-sell' : ''}`}
              onClick={() => setFormData({...formData, orderType: 'SELL'})}
            >
              SELL
            </button>
          </div>
        </div>
        
        {formData.orderType === 'SELL' && selectedAsset && (
          <div className="info-box">
            <p>Available to sell: {availableToSell} shares</p>
          </div>
        )}
        
        <div className="form-group">
          <label>
            <input 
              type="checkbox"
              checked={formData.isMarketOrder}
              onChange={(e) => setFormData({
                ...formData, 
                isMarketOrder: e.target.checked,
                price: e.target.checked ? '' : formData.price
              })}
            />
            Market Order (execute at current price)
          </label>
        </div>
        
        {!formData.isMarketOrder && (
          <div className="form-group">
            <label>Price ($):</label>
            <input 
              type="number"
              step="0.01"
              min="0.01"
              value={formData.price}
              onChange={(e) => setFormData({...formData, price: e.target.value})}
              required={!formData.isMarketOrder}
              disabled={formData.isMarketOrder}
            />
          </div>
        )}
        
        <div className="form-group">
          <label>Quantity:</label>
          <input 
            type="number"
            step="0.0001"
            min="0.0001"
            value={formData.quantity}
            onChange={(e) => setFormData({...formData, quantity: e.target.value})}
            required
          />
          {selectedAsset && (
            <span className="hint">
              Estimated cost: ${(formData.quantity * (formData.price || selectedAsset.currentPrice)).toFixed(2)}
            </span>
          )}
        </div>
        
        <button 
          type="submit" 
          disabled={loading}
          className={`btn btn-submit ${formData.orderType === 'BUY' ? 'btn-buy' : 'btn-sell'}`}
        >
          {loading ? 'Submitting...' : `Place ${formData.orderType} Order`}
        </button>
      </form>
    </div>
  );
};

export default OrderForm;