import axios from 'axios';

export const api = axios.create({
  baseURL: import.meta.env.VITE_API_BASE_URL || 'http://localhost:5017',
  withCredentials: true,
  headers: { Accept: 'application/json' },
});

let csrfRequestToken: string | null = null;

export async function refreshCsrfToken(): Promise<void> {
  const response = await api.get<{ requestToken: string }>('/api/auth/csrf');
  csrfRequestToken = response.data.requestToken;
}

api.interceptors.request.use(async config => {
  const method = (config.method ?? 'get').toLowerCase();
  if (!['get', 'head', 'options', 'trace'].includes(method)) {
    if (!csrfRequestToken) await refreshCsrfToken();
    config.headers.set('X-CSRF-TOKEN', csrfRequestToken!);
  }
  return config;
});

export function apiErrorMessage(error: unknown): string {
  if (axios.isAxiosError(error)) {
    const data: unknown = error.response?.data;
    if (data && typeof data === 'object') {
      const body = data as { title?: unknown; detail?: unknown; errors?: Record<string, unknown> };
      if (body.errors) {
        const messages = Object.values(body.errors)
          .flatMap(value => Array.isArray(value) ? value : [])
          .filter((value): value is string => typeof value === 'string');
        if (messages.length) return messages.join(' ');
      }
      if (typeof body.detail === 'string') return body.detail;
      if (typeof body.title === 'string') return body.title;
    }
    if (!error.response) return 'Could not reach the API. Check that the backend is running.';
  }
  return 'Something went wrong. Please try again.';
}
