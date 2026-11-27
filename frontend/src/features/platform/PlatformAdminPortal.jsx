import { useEffect, useState } from 'react';
import { apiChangePlatformPassword, apiCreatePlatformChurch, apiGetPlatformChurches } from '../../services/authService.js';
import Brand from '../../components/ui/Brand.jsx';
import ThemeToggle from '../../components/ui/ThemeToggle.jsx';
import './PlatformAdminPortal.css';

const initialChurch = { name: '', email: '', password: '', denomination: '', phone: '', address: '' };

export default function PlatformAdminPortal({ user, onLogout, theme, onToggleTheme, tell }) {
  const [churches, setChurches] = useState([]);
  const [form, setForm] = useState(initialChurch);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState('');
  const [currentPassword, setCurrentPassword] = useState('');
  const [newPassword, setNewPassword] = useState('');

  const load = async () => {
    try { setChurches(await apiGetPlatformChurches()); }
    catch (e) { setError(e.message || 'Não foi possível carregar as igrejas.'); }
  };
  useEffect(() => { load(); }, []);

  const submitChurch = async event => {
    event.preventDefault(); setBusy(true); setError('');
    try {
      await apiCreatePlatformChurch(form);
      setForm(initialChurch);
      await load();
      tell('Igreja cadastrada com sucesso.');
    } catch (e) { setError(e.message || 'Falha ao cadastrar igreja.'); }
    finally { setBusy(false); }
  };

  const changePassword = async event => {
    event.preventDefault(); setBusy(true); setError('');
    try {
      await apiChangePlatformPassword(currentPassword, newPassword);
      setCurrentPassword(''); setNewPassword('');
      tell('Senha alterada com sucesso.'); 
    } catch (e) { setError(e.message || 'Não foi possível alterar a senha.'); }
    finally { setBusy(false); }
  };

  const update = (key, value) => setForm(old => ({ ...old, [key]: value }));

  return (
    <main className="platform-page">
      <header className="platform-header">
        <Brand />
        <div className="platform-header-actions">
          <span>{user.name}</span>
          <ThemeToggle theme={theme} onToggle={onToggleTheme} />
          <button type="button" onClick={onLogout}>Sair</button>
        </div>
      </header>
      <section className="platform-content">
        <div className="platform-intro">
          <span>ADMINISTRAÇÃO DA PLATAFORMA</span>
          <h1>Painel de superadministrador</h1>
          <p>Gerencie as instituições cadastradas no ChurchManager. Este ambiente não acessa os dados internos das igrejas.</p>
        </div>
        {error && <div className="platform-error" role="alert">{error}</div>}
        <div className="platform-grid">
          <section className="platform-panel">
            <h2>Cadastrar nova igreja</h2>
            <p>Somente superadministradores podem criar instituições.</p>
            <form onSubmit={submitChurch}>
              <label>Nome da igreja<input required minLength="2" maxLength="100" value={form.name} onChange={e => update('name', e.target.value)} /></label>
              <label>E-mail institucional<input required type="email" maxLength="254" value={form.email} onChange={e => update('email', e.target.value)} /></label>
              <label>Senha institucional<input required type="password" minLength="12" maxLength="128" value={form.password} onChange={e => update('password', e.target.value)} /></label>
              <label>Denominação (opcional)<input maxLength="100" value={form.denomination} onChange={e => update('denomination', e.target.value)} /></label>
              <label>Telefone (opcional)<input maxLength="40" value={form.phone} onChange={e => update('phone', e.target.value)} /></label>
              <label>Endereço (opcional)<input maxLength="250" value={form.address} onChange={e => update('address', e.target.value)} /></label>
              <button disabled={busy} type="submit">{busy ? 'Salvando...' : 'Cadastrar igreja'}</button>
            </form>
          </section>
          <section className="platform-panel">
            <h2>Igrejas cadastradas <small>{churches.length}</small></h2>
            <p>Instituições disponíveis para seleção no cadastro de membros.</p>
            <div className="platform-church-list">
              {churches.length === 0 ? <p>Nenhuma igreja cadastrada.</p> : churches.map(church => (
                <article key={church.id}>
                  <div><strong>{church.name}</strong><span>{church.email}</span></div>
                  <div className="platform-church-meta"><span>{church.members} membros</span><b className={church.isActive ? 'active' : 'inactive'}>{church.isActive ? 'Ativa' : 'Inativa'}</b></div>
                </article>
              ))}
            </div>
          </section>
          <section className="platform-panel platform-password-panel">
            <h2>Alterar minha senha</h2>
            <p>Cada superadministrador controla sua própria senha.</p>
            <form onSubmit={changePassword}>
              <label>Senha atual<input required type="password" value={currentPassword} onChange={e => setCurrentPassword(e.target.value)} /></label>
              <label>Nova senha (mínimo 12 caracteres)<input required minLength="12" maxLength="128" type="password" value={newPassword} onChange={e => setNewPassword(e.target.value)} /></label>
              <button disabled={busy} type="submit">{busy ? 'Salvando...' : 'Alterar senha'}</button>
            </form>
          </section>
        </div>
      </section>
    </main>
  );
}
