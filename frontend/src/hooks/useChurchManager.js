import { useEffect, useRef, useState } from 'react';
import { loadDemoData, saveDemoData } from '../services/demoStorage.js';
import { api, API_URL, apiLogin, apiResource, DEMO_MODE } from '../services/index.js';
import { initSignalR, subscribeToUpdates, stopSignalR } from '../services/signalrService.js';
import { moduleMeta } from '../config/navigation.js';
import { humanRole } from '../utils/appHelpers.js';
import useTheme from './useTheme.js';
import useRecordActions from './useRecordActions.js';
import { clearSession, readSession, saveSession } from '../utils/sessionStore.js';
import { authenticateDemo } from '../services/demoAuthService.js';

const emptyData = () => ({
  members: [], ministries: [], events: [], transactions: [], donations: [], roles: [], scales: [],
  settings: { churchName: '', email: '', phone: '', address: '', timezone: 'America/Sao_Paulo' }
});

async function loadRemoteData(role) {
  if (humanRole(role) === 'Membro') {
    const [events, scales, settings] = await Promise.all([
      api('/api/member/events'),
      api('/api/member/scales'),
      api('/api/member/settings')
    ]);
    return { events, scales, settings };
  }

  const names = ['members', 'ministries', 'events', 'transactions', 'roles', 'scales'];
  const loaded = await Promise.all([
    ...names.map(resource => apiResource(resource)),
    api('/api/donations'),
    api('/api/church/settings')
  ]);
  return {
    ...Object.fromEntries(names.map((resource, index) => [resource, loaded[index] || []])),
    donations: loaded[6] || [],
    settings: loaded[7]
  };
}

export default function useChurchManager() {
  const [user, setUser] = useState(() => readSession(DEMO_MODE));
  const { theme, toggleTheme } = useTheme();
  const [data, setData] = useState(() => DEMO_MODE ? loadDemoData() : emptyData());
  const [module, setModule] = useState('dashboard');
  const [query, setQuery] = useState('');
  const [dialog, setDialog] = useState(null);
  const [toast, setToast] = useState('');
  const [mobileNav, setMobileNav] = useState(false);
  const [saving, setSaving] = useState(false);
  const [form, setForm] = useState({});
  const loadedRemoteData = useRef(false);
  const demo = DEMO_MODE;

  useEffect(() => {
    if (demo) saveDemoData(data);
  }, [data, demo]);

  useEffect(() => {
    const expireSession = () => {
      loadedRemoteData.current = false;
      clearSession();
      stopSignalR();
      setUser(null);
      setModule('dashboard');
      setDialog(null);
      setData(emptyData());
    };
    window.addEventListener('churchmanager:unauthorized', expireSession);
    return () => window.removeEventListener('churchmanager:unauthorized', expireSession);
  }, []);

  // Restaura apenas os dados que o perfil autenticado pode consultar.
  useEffect(() => {
    if (demo || !user || loadedRemoteData.current) return;
    let active = true;
    loadedRemoteData.current = true;

    (async () => {
      try {
        const loaded = await loadRemoteData(user.role);
        if (active) setData(old => ({ ...old, ...loaded, settings: loaded.settings || old.settings }));
      } catch (error) {
        if (!active) return;
        loadedRemoteData.current = false;
        clearSession();
        setUser(null);
        setToast(`A sessão não pôde ser restaurada: ${error.message}`);
      }
    })();

    return () => {
      active = false;
    };
  }, [demo, user]);

  useEffect(() => {
    if (demo || !user) return undefined;
    let active = true;

    const refreshForChurch = async payload => {
      if (!active || Number(payload?.churchId) !== Number(user.churchId)) return;
      const resource = String(payload?.resource || '').toLowerCase();
      if (!['members', 'donations', 'transactions', 'roles', 'settings'].includes(resource)) return;
      try {
        const loaded = await loadRemoteData(user.role);
        if (active) setData(old => ({ ...old, ...loaded, settings: loaded.settings || old.settings }));
      } catch (error) {
        console.warn('Não foi possível atualizar os dados em tempo real:', error.message);
      }
    };

    const unsubscribe = subscribeToUpdates(refreshForChurch);
    initSignalR(API_URL);
    return () => {
      active = false;
      unsubscribe();
    };
  }, [demo, user?.id, user?.role, user?.churchId]);

  useEffect(() => {
    if (!toast) return undefined;
    const id = setTimeout(() => setToast(''), 3000);
    return () => clearTimeout(id);
  }, [toast]);

  const tell = text => setToast(text);

  const logout = () => {
    loadedRemoteData.current = false;
    stopSignalR();
    setToast('');
    clearSession();
    setUser(null);
    setModule('dashboard');
    setDialog(null);
    setData(demo ? loadDemoData() : emptyData());
  };

  async function login(identifier, password, accessMode = 'admin') {
    const loginIdentifier = String(identifier || '').trim();
    if (!loginIdentifier || !password) throw new Error('Informe seu usuário e senha.');

    const result = demo
      ? { user: authenticateDemo(loginIdentifier, password) }
      : await apiLogin(loginIdentifier, password, accessMode);
    const next = result.user;

    if (!demo) {
      try {
        const loaded = await loadRemoteData(next.role);
        setData(old => ({ ...old, ...loaded, settings: loaded.settings || old.settings }));
        loadedRemoteData.current = true;
      } catch (error) {
        loadedRemoteData.current = false;
        clearSession();
        throw new Error(`Não foi possível carregar os dados protegidos. ${error.message}`);
      }
    }

    saveSession(next, demo);
    setUser(next);
    setModule('dashboard');
  }

  async function register(values) {
    if (demo) throw new Error('O cadastro está desativado nesta demonstração. Use o acesso autorizado.');

    const result = await api('/api/auth/register', {
      method: 'POST',
      body: JSON.stringify({
        name: values.name,
        email: values.email,
        password: values.password,
        churchName: values.church
      })
    });

    if (result.token) sessionStorage.setItem('churchmanager-token', result.token);
    const next = {
      id: result.id,
      name: result.name,
      email: result.email,
      role: result.role || 'Igreja',
      churchId: result.churchId,
      churchName: result.churchName,
      accessMode: result.accessMode || 'church'
    };
    saveSession(next, false);
    setUser(next);
  }

  async function submitDonation(values) {
    if (demo) throw new Error('O envio de contribuições não está disponível na demonstração.');
    const result = await api('/api/donations', {
      method: 'POST',
      body: JSON.stringify({
        amount: Number(values.amount),
        category: values.category,
        receiptUrl: values.receiptUrl || null,
        donorName: values.donorName || null
      })
    });
    tell('Contribuição enviada. Ela ficará pendente até a conferência da igreja.');
    return result;
  }

  async function updateDonationStatus(id, status) {
    const result = await api(`/api/donations/${id}/status`, {
      method: 'PATCH',
      body: JSON.stringify({ status })
    });
    const loaded = await loadRemoteData(user.role);
    setData(old => ({ ...old, ...loaded, settings: loaded.settings || old.settings }));
    tell(result.message || 'Status da contribuição atualizado.');
  }

  const {
    filtered, add, edit, remove, saveRecord, changeForm,
    handleAction, exportCsv
  } = useRecordActions({
    module, data, query, dialog, form, setForm, setDialog, setData, setSaving, tell
  });

  const meta = moduleMeta[module] || moduleMeta.dashboard;

  return {
    user, setUser, data, setData, module, setModule, query, setQuery,
    dialog, setDialog, toast, setToast, mobileNav, setMobileNav, saving,
    form, setForm, theme, toggleTheme, demo, login, register, logout,
    filtered, add, edit, remove, saveRecord, changeForm,
    handleAction, exportCsv, tell, meta, humanRole,
    submitDonation, updateDonationStatus
  };
}
