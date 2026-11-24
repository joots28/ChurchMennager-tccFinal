export const API_URL = (import.meta.env.VITE_API_URL || 'http://localhost:5080').replace(/\/$/, '');
export const DEMO_MODE = import.meta.env.VITE_DEMO_MODE === 'true';

export async function api(path, options = {}) {
  const token = sessionStorage.getItem('churchmanager-token');
  const response = await fetch(API_URL + path, {
    ...options,
    headers: {
      'Content-Type': 'application/json',
      ...(token ? { Authorization: 'Bearer ' + token } : {}),
      ...(options.headers || {})
    }
  });

  if (response.status === 401) {
    // Não limpa imediatamente a sessão se a requisição for de login/registro (onde 401 significa apenas credencial incorreta)
    if (!path.startsWith('/api/auth/login') && !path.startsWith('/api/auth/register')) {
      sessionStorage.removeItem('churchmanager-token');
      sessionStorage.removeItem('churchmanager-session');
      window.dispatchEvent(new CustomEvent('churchmanager:unauthorized'));
    }
  }

  if (!response.ok) {
    let message = 'Falha na API (' + response.status + ')';
    try {
      const body = await response.json();
      message = body.message || body.title || message;
    } catch {
      // Resposta sem corpo JSON
    }
    if (response.status === 429) {
      message = 'Muitas tentativas em pouco tempo. Aguarde e tente novamente.';
    }
    throw new Error(message);
  }

  return response.status === 204 ? null : response.json();
}
