import { useEffect, useState } from 'react';
import Icon from '../../components/Icon.jsx';
import Button from '../../components/ui/Button.jsx';
import Brand from '../../components/ui/Brand.jsx';
import ThemeToggle from '../../components/ui/ThemeToggle.jsx';
import InputField from '../../components/ui/InputField.jsx';
import { apiGetPublicChurches } from '../../services/index.js';
import { DEMO_MODE } from '../../services/httpClient.js';
import './Auth.css';

function initialMode() {
  const pathname = window.location.pathname.toLowerCase();
  if (pathname.startsWith('/igreja')) return 'church';
  if (pathname.startsWith('/membro')) return 'member';
  return 'admin';
}

export default function Auth({ onLogin, onRegister, theme, onToggleTheme, initialNotice = '' }) {
  const isPlatformEntry = window.location.pathname.toLowerCase().startsWith('/superadmin');
  const [accessMode, setAccessMode] = useState(() => isPlatformEntry ? 'superadmin' : initialMode());
  const [screen, setScreen] = useState('login');
  const [showPassword, setShowPassword] = useState(false);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [notice, setNotice] = useState(initialNotice);
  const [churches, setChurches] = useState([]);
  const [churchSearch, setChurchSearch] = useState('');
  const [form, setForm] = useState({
    email: '', password: '', confirm: '', name: '', churchId: '',
    churchName: '', denomination: '', phone: '', address: '', ministry: ''
  });

  const change = (key, value) => setForm(old => ({ ...old, [key]: value }));

  useEffect(() => {
    if (accessMode !== 'member' || screen !== 'register') return undefined;
    let active = true;

    apiGetPublicChurches(churchSearch)
      .then(items => {
        if (active) setChurches(Array.isArray(items) ? items : []);
      })
      .catch(loadError => {
        if (active) setError(loadError.message || 'Não foi possível carregar as igrejas cadastradas.');
      });

    return () => { active = false; };
  }, [accessMode, screen, churchSearch]);

  const selectMode = mode => {
    setAccessMode(mode);
    setScreen('login');
    setError('');
    setNotice('');
    setChurchSearch('');
  };

  const submit = async event => {
    event.preventDefault();
    setError('');
    setNotice('');
    setBusy(true);

    try {
      if (screen === 'login') {
        await onLogin(form.email.trim(), form.password, accessMode);
        return;
      }

      if (screen === 'recover') {
        setNotice('A recuperação de senha ainda não está disponível. Entre em contato com a administração da igreja.');
        return;
      }

      if (form.password.length < 8 || form.password.length > 128) {
        throw new Error('A senha deve ter entre 8 e 128 caracteres.');
      }
      if (form.password !== form.confirm) {
        throw new Error('As senhas não coincidem.');
      }

      if (accessMode === 'member') {
        if (form.name.trim().length < 2 || !form.email.trim() || !form.churchId) {
          throw new Error('Preencha seu nome, e-mail e selecione uma igreja existente.');
        }

        await onRegister({
          name: form.name.trim(),
          email: form.email.trim(),
          password: form.password,
          churchId: Number(form.churchId),
          phone: form.phone.trim(),
          ministry: form.ministry.trim()
        }, 'member');
        return;
      }

      throw new Error('Novas contas pessoais começam como membros. O acesso administrativo é concedido nas configurações da igreja.');
    } catch (submitError) {
      setError(submitError.message || 'Não foi possível concluir a operação.');
    } finally {
      setBusy(false);
    }
  };

  const isRegister = screen === 'register';
  const isChurch = accessMode === 'church';
  const isMember = accessMode === 'member';
  const isSuperAdmin = accessMode === 'superadmin';
  const title = screen === 'recover'
    ? 'Recuperar senha'
    : isRegister
      ? 'Criar conta de membro'
      : isPlatformEntry ? 'Acesso da plataforma' : isChurch ? 'Acesso institucional' : isMember ? 'Portal do Membro' : 'Acesso Administrativo';

  const subtitle = screen === 'recover'
    ? 'Entre em contato com a administração para recuperar seu acesso.'
    : isRegister
      ? 'Sua conta será vinculada a uma igreja já cadastrada.'
      : isChurch
        ? 'Acesse as configurações institucionais da sua igreja.'
        : isMember
          ? 'Acesse eventos, escalas e informações da sua comunidade.'
          : isSuperAdmin
            ? 'Área restrita aos responsáveis pela plataforma ChurchManager.'
            : 'Acesse a gestão administrativa da sua igreja.';

  return (
    <main className="auth-page">
      <section className="auth-card">
        <div className="auth-top">
          <ThemeToggle theme={theme} onToggle={onToggleTheme} className="auth-theme-toggle" />
          <div className="auth-brand"><Brand /></div>

          {!isPlatformEntry && !isRegister && (
            <nav className="auth-portals" aria-label="Tipo de acesso">
              <button type="button" className={isChurch ? 'active' : ''} onClick={() => selectMode('church')}>Igreja</button>
              <button type="button" className={accessMode === 'admin' ? 'active' : ''} onClick={() => selectMode('admin')}>Administrador</button>
              <button type="button" className={isMember ? 'active' : ''} onClick={() => selectMode('member')}>Membro</button>
            </nav>
          )}

          {screen === 'recover' && (
            <button className="back-link" type="button" onClick={() => { setScreen('login'); setError(''); }}>
              <span aria-hidden="true">←</span> Voltar ao acesso
            </button>
          )}

          <div className="auth-hero">
            {screen === 'recover' && <div className="recover-icon"><Icon name="lock" size={24} /></div>}
            <h1>{title}</h1>
            <p>{subtitle}</p>
          </div>

          <form id="auth-form" className="auth-form" onSubmit={submit}>
            {isRegister && (
              <>
                <InputField
                  label="Nome completo" name="name" value={form.name} onChange={change}
                  placeholder="Seu nome e sobrenome" icon="user"
                  minLength={2} maxLength={160} autoComplete="name" required
                />
              </>
            )}

            {(screen !== 'recover') && (
              <InputField
                label={isSuperAdmin ? 'Nome de usuário' : isChurch ? 'E-mail institucional' : 'E-mail'}
                name="email" value={form.email} onChange={change}
                placeholder={isSuperAdmin ? 'seu usuário' : isChurch ? 'contato@igreja.com.br' : 'voce@email.com'}
                type={isSuperAdmin ? 'text' : 'email'} icon={isSuperAdmin ? 'user' : 'mail'} maxLength={254}
                autoComplete={isRegister ? 'email' : 'username'} required
              />
            )}

            {isRegister && isMember && (
              <>
                <div className="auth-select-field">
                  <label htmlFor="church-search">Pesquisar igreja existente</label>
                  <input id="church-search" value={churchSearch} onChange={event => setChurchSearch(event.target.value)} placeholder="Digite o nome da igreja" maxLength={100} />
                  <label htmlFor="church-select">Selecione sua igreja</label>
                  <select id="church-select" value={form.churchId} onChange={event => change('churchId', event.target.value)} required>
                    <option value="">Escolha uma igreja cadastrada</option>
                    {churches.map(church => (
                      <option key={church.id} value={church.id}>{church.name}{church.denomination ? ' — ' + church.denomination : ''}</option>
                    ))}
                  </select>
                  {churches.length === 0 && <small>Nenhuma igreja encontrada com essa pesquisa.</small>}
                </div>
                <InputField label="Telefone (opcional)" name="phone" value={form.phone} onChange={change} placeholder="(00) 00000-0000" maxLength={40} />
                <InputField label="Ministério (opcional)" name="ministry" value={form.ministry} onChange={change} placeholder="Ex.: Louvor, Recepção..." maxLength={120} />
              </>
            )}

            {screen !== 'recover' && (
              <InputField
                label="Senha" name="password" value={form.password} onChange={change}
                placeholder={isRegister ? 'Mínimo de 8 caracteres' : 'Sua senha'}
                type={showPassword ? 'text' : 'password'} icon="lock"
                rightIcon={showPassword ? 'eye-off' : 'eye'}
                onRightClick={() => setShowPassword(value => !value)}
                minLength={isRegister ? 8 : 1} maxLength={128}
                autoComplete={isRegister ? 'new-password' : 'current-password'} required
              />
            )}

            {isRegister && (
              <InputField
                label="Confirmar senha" name="confirm" value={form.confirm} onChange={change}
                placeholder="Digite a senha novamente" type={showPassword ? 'text' : 'password'}
                icon="lock" minLength={8} maxLength={128} autoComplete="new-password" required
                rightIcon={showPassword ? 'eye-off' : 'eye'} onRightClick={() => setShowPassword(value => !value)}
              />
            )}

            {screen === 'login' && DEMO_MODE && accessMode === 'admin' && (
              <p className="auth-demo-hint">Acesso demonstrativo: use apenas dados fictícios do ambiente local.</p>
            )}

            {screen === 'login' && (
              <div className="forgot-row">
                <button type="button" onClick={() => { setScreen('recover'); setError(''); }}>Esqueci minha senha</button>
              </div>
            )}

            {isSuperAdmin && <p className="auth-access-note">Acesso privado. Utilize o usuário fornecido pela equipe da plataforma.</p>}

            {accessMode === 'admin' && isRegister === false && (
              <p className="auth-access-note">O acesso administrativo é concedido por um administrador nas configurações da igreja. Não existe cadastro público de administrador.</p>
            )}

            {error && <div className="alert alert-error" role="alert">{error}</div>}
            {notice && <div className="alert alert-info" role="status">{notice}</div>}
          </form>
        </div>

        <div className="auth-bottom">
          <div className="auth-submit">
            <Button form="auth-form" type="submit" icon="arrow" disabled={busy}>
              {busy ? 'Aguarde...' : screen === 'login' ? 'Entrar' : isRegister ? (isChurch ? 'Cadastrar igreja' : 'Criar conta de membro') : 'Continuar'}
            </Button>
          </div>

          {screen === 'login' ? (
            isMember
              ? <p>Não possui acesso? <button type="button" onClick={() => { setScreen('register'); setError(''); setNotice(''); }}>Criar cadastro</button></p>
              : isPlatformEntry
                ? <p>Conta restrita. Solicite acesso ao responsável pela plataforma.</p>
                : isChurch
                  ? <p>O acesso institucional é fornecido pela administração da plataforma.</p>
                  : <p>O cadastro de membros é realizado pelo portal do Membro.</p>
          ) : screen === 'register' ? (
            <p>Já possui uma conta? <button type="button" onClick={() => { setScreen('login'); setError(''); }}>Entrar</button></p>
          ) : (
            <p>Lembrou sua senha? <button type="button" onClick={() => setScreen('login')}>Entrar</button></p>
          )}

          <a className="auth-privacy-link" href="/privacidade">Privacidade e política de cookies</a>
        </div>
      </section>
    </main>
  );
}
