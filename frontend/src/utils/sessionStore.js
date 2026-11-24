const SESSION_KEY = 'churchmanager-session';
const TOKEN_KEY = 'churchmanager-token';

export function tokenIsValid(token) {
  if (!token || typeof token !== 'string') return false;
  try {
    const parts = token.split('.');
    if (parts.length !== 3) return false;
    const normalized = parts[1].replace(/-/g, '+').replace(/_/g, '/');
    const padded = normalized.padEnd(Math.ceil(normalized.length / 4) * 4, '=');
    const payload = JSON.parse(atob(padded));
    return Number.isFinite(payload.exp) && payload.exp * 1000 > Date.now();
  } catch {
    return false;
  }
}

export function clearSession() {
  try {
    sessionStorage.removeItem(SESSION_KEY);
    sessionStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(SESSION_KEY);
    localStorage.removeItem(TOKEN_KEY);
  } catch {
    // Tratamento para ambientes com storage desativado
  }
}

export function readSession(demoMode) {
  try {
    const storage = demoMode ? localStorage : sessionStorage;
    const stored = storage.getItem(SESSION_KEY);
    if (!stored) return null;

    const user = JSON.parse(stored);
    if (!user || typeof user.name !== 'string' || typeof user.role !== 'string') {
      clearSession();
      return null;
    }

    if (demoMode) {
      if (user.role !== 'Administrador' && user.role !== 'Membro') {
        clearSession();
        return null;
      }
    } else {
      const token = sessionStorage.getItem(TOKEN_KEY);
      const validRoles = ['Administrador', 'Membro', 'Igreja'];
      if (!validRoles.includes(user.role) || !tokenIsValid(token || '')) {
        clearSession();
        return null;
      }
    }

    return user;
  } catch {
    clearSession();
    return null;
  }
}

export function saveSession(user, demoMode) {
  const storage = demoMode ? localStorage : sessionStorage;
  storage.setItem(SESSION_KEY, JSON.stringify(user));
}
