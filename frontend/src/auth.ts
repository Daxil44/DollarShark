import { createContext, useContext } from 'react';
import axios from 'axios';

export type UserProfile = { id: string; email: string; hasPassword?: boolean };
export type DeleteAccountInput = { password?: string; confirmEmail?: string; confirmation: string };
export type RegisterOutcome = 'signedIn' | 'confirmationRequired';
export type AuthContextValue = {
  user: UserProfile | null;
  checking: boolean;
  login: (email: string, password: string) => Promise<void>;
  register: (email: string, password: string) => Promise<RegisterOutcome>;
  logout: () => Promise<void>;
  deleteAccount: (input: DeleteAccountInput) => Promise<void>;
};

export const AuthContext = createContext<AuthContextValue | null>(null);

export function useAuth(): AuthContextValue {
  const value = useContext(AuthContext);
  if (!value) throw new Error('useAuth must be used inside AuthProvider.');
  return value;
}

/** True when the API refused sign-in only because the account's email is not confirmed yet. */
export function isEmailNotConfirmed(error: unknown): boolean {
  return axios.isAxiosError(error) && error.response?.status === 403 &&
    (error.response.data as { code?: unknown } | undefined)?.code === 'email_not_confirmed';
}
