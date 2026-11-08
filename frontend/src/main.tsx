import { StrictMode } from 'react';
import { createRoot } from 'react-dom/client';
import { BrowserRouter } from 'react-router-dom';
import AuthProvider from './AuthProvider';
import 'bootstrap/dist/css/bootstrap.min.css';
import './index.css';
import App from './App';
import ErrorBoundary from './components/ErrorBoundary';

createRoot(document.getElementById('root')!).render(<StrictMode><BrowserRouter><AuthProvider><ErrorBoundary variant="page"><App /></ErrorBoundary></AuthProvider></BrowserRouter></StrictMode>);
