import { api } from './httpClient.js';

export async function apiLogin(identifier, password, accessMode = 'admin') {
  const result = await api('/api/auth/login', {
    method: 'POST',
    body: JSON.stringify({ identifier, password, accessMode })
  });

  if (result.token) {
    sessionStorage.setItem('churchmanager-token', result.token);
  }

  return {
    user: {
      id: result.id,
      name: result.name,
      email: result.email,
      role: result.role,
      churchId: result.churchId,
      churchName: result.churchName,
      accessMode: result.accessMode
    }
  };
}

export async function apiRegisterChurch(data) {
  const result = await api('/api/auth/register-church', {
    method: 'POST',
    body: JSON.stringify({
      name: data.name,
      denomination: data.denomination || null,
      email: data.email,
      password: data.password,
      phone: data.phone || '',
      address: data.address || '',
      logoUrl: data.logoUrl || null
    })
  });

  if (result.token) {
    sessionStorage.setItem('churchmanager-token', result.token);
  }

  return {
    user: {
      id: result.id,
      name: result.name,
      email: result.email,
      role: result.role,
      churchId: result.churchId,
      churchName: result.churchName,
      accessMode: result.accessMode
    }
  };
}

export async function apiRegisterMember(data) {
  const result = await api('/api/auth/register-member', {
    method: 'POST',
    body: JSON.stringify({
      name: data.name,
      email: data.email,
      password: data.password,
      churchId: Number(data.churchId),
      phone: data.phone || '',
      ministry: data.ministry || 'Todos'
    })
  });

  if (result.token) {
    sessionStorage.setItem('churchmanager-token', result.token);
  }

  return {
    user: {
      id: result.id,
      name: result.name,
      email: result.email,
      role: result.role,
      churchId: result.churchId,
      churchName: result.churchName,
      accessMode: result.accessMode
    }
  };
}

export async function apiGetPlatformChurches() {
  return api('/api/platform/churches');
}

export async function apiCreatePlatformChurch(data) {
  return api('/api/platform/churches', { method: 'POST', body: JSON.stringify(data) });
}

export async function apiChangePlatformPassword(currentPassword, newPassword) {
  return api('/api/platform/change-password', {
    method: 'POST',
    body: JSON.stringify({ currentPassword, newPassword })
  });
}

export async function apiGetPublicChurches(search = '') {
  const query = search ? `?search=${encodeURIComponent(search)}` : '';
  return api(`/api/churches${query}`);
}

export async function apiCheckSession() {
  return api('/api/auth/me');
}
