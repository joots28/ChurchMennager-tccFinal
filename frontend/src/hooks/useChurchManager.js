import { useEffect, useRef, useState } from 'react';
import { loadDemoData, saveDemoData } from '../services/demoStorage.js';
import {
  api, API_URL, apiLogin, apiRegisterChurch, apiRegisterMember,
  apiCheckSession, apiResource, DEMO_MODE,
  initSignalR, subscribeToUpdates, stopSignalR
} from '../services/index.js';
import { moduleMeta } from '../config/navigation.js';
import { humanRole } from '../utils/appHelpers.js';
import useTheme from './useTheme.js';
import useRecordActions from './useRecordActions.js';
import { clearSession, readSession, saveSession } from '../utils/sessionStore.js';
import { authenticateDemo } from '../services/demoAuthService.js';

const emptyData = () => ({
  members: [],
  ministries: [],
  events: [],
  transactions: [],
  roles: [],
  scales: [],
  admins: [],
  eligibleMembers: [],
  auditLogs: [],
  settings: { churchName: '', email: '', phone: '', address: '', timezone: 'America/Sao_Paulo' }
});

async function loadRemoteData(user) {
  if (user.role === 'Igreja') {
    const [settings, admins, eligibleMembers] = await Promise.all([
      api('/api/church/settings'),
      api('/api/church/settings/admins'),
      api('/api/church/settings/eligible-members')
    ]);
    return { settings, admins, eligibleMembers };
  }

  if (humanRole(user.role) === 'Membro') {
    const [events, scales, settings, ministries] = await Promise.all([
      api('/api/member/events'),
      api('/api/member/scales'),
      api('/api/member/settings'),
      api('/api/member/ministries')
    ]);
    return { events, scales, settings, ministries };
  }

  const names = ['members', 'ministries', 'events', 'transactions', 'roles', 'scales'];
  const loaded = await Promise.all([
    ...names.map(resource => apiResource(resource)),
    api('/api/church/settings'),
    api('/api/church/settings/admins'),
    api('/api/church/settings/eligible-members'),
    api('/api/church/settings/audit-logs')
  ]);

  return {
    ...Object.fromEntries(names.map((resource, index) => [resource, loaded[index] || []])),
    settings: loaded[6],
    admins: loaded[7] || [],
    eligibleMembers: loaded[8] || [],
    auditLogs: loaded[9] || []
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
      stopSignalR();
      clearSession();
      setUser(null);
      setModule('dashboard');
      setDialog(null);
      setData(emptyData());
    };

    window.addEventListener('churchmanager:unauthorized', expireSession);
    return () => window.removeEventListener('churchmanager:unauthorized', expireSession);
  }, []);

  // No F5, valida o JWT e recupera o perfil atual antes de carregar dados protegidos.
  useEffect(() => {
    if (demo || !user || loadedRemoteData.current) return undefined;
    let active = true;
    loadedRemoteData.current = true;

    (async () => {
      try {
        const verifiedUser = await apiCheckSession();
        if (!active) return;
        saveSession(verifiedUser, false);
        setUser(verifiedUser);

        const loaded = await loadRemoteData(verifiedUser);
        if (active) setData(old => ({ ...old, ...loaded, settings: loaded.settings || old.settings }));
      } catch (error) {
        if (!active) return;
        loadedRemoteData.current = false;
        stopSignalR();
        clearSession();
        setUser(null);
        setData(emptyData());
        setToast(`A sessão não pôde ser restaurada: ${error.message}`);
      }
    })();

    return () => { active = false; };
  }, [demo, user]);

  // Atualizações em tempo real: o SignalR envia apenas metadados; os dados são
  // recarregados pelos endpoints que já aplicam as permissões do usuário.
  useEffect(() => {
    if (demo || !user) return undefined;

    initSignalR(API_URL);
    const unsubscribe = subscribeToUpdates(payload => {
      if (payload?.churchId && Number(payload.churchId) !== Number(user.churchId)) return;

      loadRemoteData(user)
        .then(loaded => setData(old => ({ ...old, ...loaded, settings: loaded.settings || old.settings })))
        .catch(error => setToast(`Não foi possível atualizar os dados: ${error.message}`));
    });

    return () => {
      unsubscribe();
      stopSignalR();
    };
  }, [demo, user?.id, user?.role, user?.churchId]);

  useEffect(() => {
    if (!toast) return undefined;
    const id = setTimeout(() => setToast(''), 4000);
    return () => clearTimeout(id);
  }, [toast]);

  const tell = text => setToast(text);

  const logout = () => {
    loadedRemoteData.current = false;
    setToast('');
    stopSignalR();
    clearSession();
    setUser(null);
    setModule('dashboard');
    setDialog(null);
    setData(demo ? loadDemoData() : emptyData());
  };

  async function login(identifier, password, accessMode = 'admin') {
    const loginIdentifier = String(identifier || '').trim();
    if (!loginIdentifier || !password) throw new Error('Informe seu e-mail e senha.');

    let result;
    if (demo) {
      if (accessMode !== 'admin') {
        throw new Error('O ambiente demonstrativo disponibiliza somente o acesso administrativo.');
      }
      result = { user: authenticateDemo(loginIdentifier, password) };
    } else {
      result = await apiLogin(loginIdentifier, password, accessMode);
    }

    const next = result.user;

    if (!demo) {
      try {
        const loaded = await loadRemoteData(next);
        setData(old => ({ ...old, ...loaded, settings: loaded.settings || old.settings }));
        loadedRemoteData.current = true;
      } catch (error) {
        loadedRemoteData.current = false;
        stopSignalR();
        clearSession();
        throw new Error(`Login realizado, mas não foi possível carregar os dados autorizados. ${error.message}`);
      }
    }

    saveSession(next, demo);
    setUser(next);
    setModule('dashboard');
  }

  async function register(values, accessMode) {
    if (demo) throw new Error('O cadastro real está desativado no ambiente demonstrativo.');

    let result;
    if (accessMode === 'church') {
      result = await apiRegisterChurch(values);
    } else if (accessMode === 'member') {
      result = await apiRegisterMember(values);
    } else {
      throw new Error('O cadastro pessoal deve ser realizado como membro. Administradores são designados nas configurações.');
    }

    const next = result.user;
    try {
      const loaded = await loadRemoteData(next);
      setData(old => ({ ...old, ...loaded, settings: loaded.settings || old.settings }));
      loadedRemoteData.current = true;
      saveSession(next, false);
      setUser(next);
      setModule('dashboard');
    } catch (error) {
      loadedRemoteData.current = false;
      stopSignalR();
      clearSession();
      throw new Error(`Conta criada, mas não foi possível carregar o ambiente. ${error.message}`);
    }
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
    handleAction, exportCsv, tell, meta, humanRole
  };
}
