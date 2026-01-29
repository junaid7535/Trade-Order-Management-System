// services/api.js
import axios from 'axios';

const API_BASE_URL = process.env.REACT_APP_API_URL || 'http://localhost:5000/api';

// Configure axios with interceptors for idempotency
const api = axios.create({
  baseURL: API_BASE_URL,
  timeout: 10000,
});

api.interceptors.request.use((config) => {
  // Add idempotency key for POST requests
  if (config.method === 'post' && !config.headers['Idempotency-Key']) {
    config.headers['Idempotency-Key'] = generateIdempotencyKey();
  }
  return config;
});

export const createOrder = async (orderData, idempotencyKey) => {
  const headers = {};
  if (idempotencyKey) {
    headers['Idempotency-Key'] = idempotencyKey;
  }
  
  const response = await api.post('/orders', orderData, { headers });
  return response.data;
};

export const getInvestorOrders = async (investorId, fromDate = null) => {
  const params = fromDate ? { fromDate: fromDate.toISOString() } : {};
  const response = await api.get(`/orders/investor/${investorId}`, { params });
  return response.data;
};

export const cancelOrder = async (orderId, reason) => {
  const response = await api.post(`/orders/${orderId}/cancel`, { reason });
  return response.data;
};

export const getAssets = async () => {
  const response = await api.get('/assets');
  return response.data;
};

export const getInvestorHoldings = async (investorId) => {
  const response = await api.get(`/investors/${investorId}/holdings`);
  return response.data;
};

// Real-time updates using SignalR/WebSocket
let connection = null;

export const subscribeToOrderUpdates = (investorId, callback) => {
  if (!connection) {
    // Initialize SignalR connection
    connection = new signalR.HubConnectionBuilder()
      .withUrl(`${API_BASE_URL.replace('/api', '')}/orderhub`)
      .withAutomaticReconnect()
      .build();
    
    connection.start().catch(err => 
      console.error('SignalR Connection Error:', err));
  }
  
  // Subscribe to order updates for this investor
  connection.on('OrderUpdated', (updatedOrder) => {
    if (updatedOrder.investorId === investorId) {
      callback(updatedOrder);
    }
  });
  
  return () => {
    connection.off('OrderUpdated');
  };
};

// Helper function to generate idempotency keys
export const generateIdempotencyKey = () => {
  return `${Date.now()}-${Math.random().toString(36).substr(2, 9)}`;
};