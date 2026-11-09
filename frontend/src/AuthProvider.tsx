import { useEffect, useMemo, useState, type ReactNode } from 'react';
import { api, refreshCsrfToken } from './api';
import { AuthContext, type AuthContextValue, type UserProfile } from './auth';

export default function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<UserProfile | null>(null);
  const [checking, setChecking] = useState(true);

  useEffect(() => {
    let active = true;
    api.get<UserProfile>('/api/auth/me')
      .then(response => { if (active) setUser(response.data); })
      .catch(() => { if (active) setUser(null); })
      .finally(() => { if (active) setChecking(false); });
    return () => { active = false; };
  }, []);

  const value = useMemo<AuthContextValue>(() => ({
    user,
    checking,
    async login(email, password) {
      const response = await api.post<UserProfile>('/api/auth/login', { email, password });
      setUser(response.data);
      await refreshCsrfToken();
    },
    async register(email, password) {
      const response = await api.post<UserProfile | { requiresEmailConfirmation: boolean }>('/api/auth/register', { email, password });
      if (response.status === 202 || 'requiresEmailConfirmation' in response.data) return 'confirmationRequired';
      setUser(response.data);
      await refreshCsrfToken();
      return 'signedIn';
    },
    async logout() {
      await api.post('/api/auth/logout');
      setUser(null);
      await refreshCsrfToken();
    },
    async deleteAccount(input) {
      await api.post('/api/auth/delete-account', input);
      // Full reload: clears every bit of in-memory state and lands on sign-in with a confirmation message.
      window.location.replace('/login?deleted=1');
    },
  }), [user, checking]);

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}
