// components/OrderDashboard.jsx
import React, { useState, useEffect } from 'react';
import { getInvestorOrders, subscribeToOrderUpdates } from '../services/api';
import OrderStatusBadge from './OrderStatusBadge';

const OrderDashboard = ({ investorId }) => {
  const [orders, setOrders] = useState([]);
  const [loading, setLoading] = useState(true);
  const [filter, setFilter] = useState('ALL');
  
  useEffect(() => {
    loadOrders();
    
    // Subscribe to real-time updates
    const unsubscribe = subscribeToOrderUpdates(investorId, (updatedOrder) => {
      setOrders(prev => prev.map(order => 
        order.orderId === updatedOrder.orderId ? updatedOrder : order
      ));
    });
    
    return () => unsubscribe();
  }, [investorId]);
  
  const loadOrders = async () => {
    try {
      const data = await getInvestorOrders(investorId);
      setOrders(data);
    } catch (error) {
      console.error('Failed to load orders:', error);
    } finally {
      setLoading(false);
    }
  };
  
  const filteredOrders = orders.filter(order => {
    if (filter === 'ALL') return true;
    return order.orderStatus === filter;
  });
  
  const getStatusCounts = () => {
    const counts = {};
    orders.forEach(order => {
      counts[order.orderStatus] = (counts[order.orderStatus] || 0) + 1;
    });
    return counts;
  };
  
  const statusCounts = getStatusCounts();
  
  return (
    <div className="order-dashboard">
      <div className="dashboard-header">
        <h2>Order Dashboard</h2>
        <div className="status-summary">
          {Object.entries(statusCounts).map(([status, count]) => (
            <div key={status} className="status-count">
              <OrderStatusBadge status={status} />
              <span className="count">{count}</span>
            </div>
          ))}
        </div>
      </div>
      
      <div className="filters">
        <button 
          className={`filter-btn ${filter === 'ALL' ? 'active' : ''}`}
          onClick={() => setFilter('ALL')}
        >
          All Orders
        </button>
        {['NEW', 'VALIDATED', 'FILLED', 'SETTLED', 'CANCELLED'].map(status => (
          <button 
            key={status}
            className={`filter-btn ${filter === status ? 'active' : ''}`}
            onClick={() => setFilter(status)}
          >
            {status}
          </button>
        ))}
      </div>
      
      {loading ? (
        <div className="loading">Loading orders...</div>
      ) : (
        <div className="orders-table">
          <table>
            <thead>
              <tr>
                <th>Order ID</th>
                <th>Asset</th>
                <th>Type</th>
                <th>Quantity</th>
                <th>Price</th>
                <th>Status</th>
                <th>Date</th>
                <th>Actions</th>
              </tr>
            </thead>
            <tbody>
              {filteredOrders.map(order => (
                <tr key={order.orderId}>
                  <td className="order-id">{order.orderId.substring(0, 8)}...</td>
                  <td>{order.asset?.symbol}</td>
                  <td>
                    <span className={`order-type ${order.orderType.toLowerCase()}`}>
                      {order.orderType}
                    </span>
                  </td>
                  <td>{order.quantity}</td>
                  <td>
                    {order.price ? `$${order.price.toFixed(2)}` : 'MARKET'}
                  </td>
                  <td>
                    <OrderStatusBadge status={order.orderStatus} />
                    {order.orderStatus === 'FILLED' && order.executedDate && (
                      <div className="executed-time">
                        Executed: {new Date(order.executedDate).toLocaleTimeString()}
                      </div>
                    )}
                  </td>
                  <td>{new Date(order.orderDate).toLocaleDateString()}</td>
                  <td>
                    {['NEW', 'VALIDATED'].includes(order.orderStatus) && (
                      <button 
                        className="btn-cancel"
                        onClick={() => handleCancel(order.orderId)}
                      >
                        Cancel
                      </button>
                    )}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      )}
    </div>
  );
};

export default OrderDashboard;