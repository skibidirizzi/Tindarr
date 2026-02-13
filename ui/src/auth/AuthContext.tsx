import { createContext, useContext, useEffect, useMemo, useState, type ReactNode } from "react";
import type { AuthResponse, MeResponse } from "../api/contracts";
import { guestLogin as apiGuestLogin, login as apiLogin, me as apiMe, register as apiRegister } from "../api/client";
import { clearSession, getAccessToken, setSession } from "./session";

type AuthContextValue = {
  user: MeResponse | null;
  loading: boolean;
  login: (userId: string, password: string) => Promise<void>;
  guestLogin: (displayName?: string) => Promise<void>;
  register: (userId: string, displayName: string, password: string) => Promise<void>;
  logout: () => void;
};

const AuthContext = createContext<AuthContextValue | null>(null);

function mapAuthToMe(auth: AuthResponse): MeResponse {
  return { userId: auth.userId, displayName: auth.displayName, roles: auth.roles };
}

export function AuthProvider({ children }: { children: ReactNode }) {
  const [user, setUser] = useState<MeResponse | null>(null);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const token = getAccessToken();
    if (!token) {
      setLoading(false);
      return;
    }

    (async () => {
      try {
        const me = await apiMe();
        setUser(me);
      } catch {
        clearSession();
        setUser(null);
      } finally {
        setLoading(false);
      }
    })();
  }, []);

  const value = useMemo<AuthContextValue>(
    () => ({
      user,
      loading,
      login: async (userId: string, password: string) => {
        const session = await apiLogin({ userId, password });
        setSession(session);
        setUser(mapAuthToMe(session));
      },
      guestLogin: async (displayName?: string) => {
        const session = await apiGuestLogin({ displayName: displayName ?? null });
        setSession(session);
        setUser(mapAuthToMe(session));
      },
      register: async (userId: string, displayName: string, password: string) => {
        const session = await apiRegister({ userId, displayName, password });
        setSession(session);
        setUser(mapAuthToMe(session));
      },
      logout: () => {
        clearSession();
        setUser(null);
      }
    }),
    [user, loading]
  );

  return <AuthContext.Provider value={value}>{children}</AuthContext.Provider>;
}

export function useAuth() {
  const ctx = useContext(AuthContext);
  if (!ctx) {
    throw new Error("useAuth must be used within an AuthProvider");
  }
  return ctx;
}

