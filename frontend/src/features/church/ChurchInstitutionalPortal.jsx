import { useEffect, useState } from 'react';
import { api } from '../../services/httpClient.js';
import Button from '../../components/ui/Button.jsx';
import InputField from '../../components/ui/InputField.jsx';
import Modal from '../../components/ui/Modal.jsx';
import Brand from '../../components/ui/Brand.jsx';
import ThemeToggle from '../../components/ui/ThemeToggle.jsx';
import './ChurchInstitutionalPortal.css';

export default function ChurchInstitutionalPortal({ user, data, onLogout, theme, onToggleTheme, tell }) {
  const [admins, setAdmins] = useState(data.admins || []);
  const [members, setMembers] = useState(data.eligibleMembers || []);
  const [loading, setLoading] = useState(false);
  const [busyAdminId, setBusyAdminId] = useState(null);
  const [busyMemberId, setBusyMemberId] = useState(null);
  const [editingAdmin, setEditingAdmin] = useState(null);
  const [adminForm, setAdminForm] = useState({ name: '', email: '', phone: '' });
  const [savingAdmin, setSavingAdmin] = useState(false);
  const [error, setError] = useState('');

  async function refresh() {
    setLoading(true);
    setError('');
    try {
      const [nextAdmins, nextMembers] = await Promise.all([
        api('/api/church/settings/admins'),
        api('/api/church/settings/eligible-members')
      ]);
      setAdmins(Array.isArray(nextAdmins) ? nextAdmins : []);
      setMembers(Array.isArray(nextMembers) ? nextMembers : []);
    } catch (loadError) {
      setError(loadError.message || 'Não foi possível carregar os dados institucionais.');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { refresh(); }, []);

  // Mantém a tela sincronizada quando um membro se cadastra ou um acesso muda via SignalR.
  useEffect(() => {
    setAdmins(data.admins || []);
    setMembers(data.eligibleMembers || []);
  }, [data.admins, data.eligibleMembers]);

  async function designateAdmin(member) {
    if (!member.hasUserAccount) {
      tell('Este membro precisa criar uma conta pessoal pelo Portal do Membro antes de receber acesso administrativo.');
      return;
    }

    setBusyMemberId(member.id);
    setError('');
    try {
      const result = await api('/api/church/settings/promote-admin', {
        method: 'POST',
        body: JSON.stringify({ memberId: member.id })
      });
      tell(result.message || 'Administrador designado com sucesso.');
      await refresh();
    } catch (promotionError) {
      setError(promotionError.message || 'Não foi possível designar o administrador.');
    } finally {
      setBusyMemberId(null);
    }
  }

  function openEditAdmin(admin) {
    setEditingAdmin(admin);
    setAdminForm({
      name: admin.name || '',
      email: admin.email || '',
      phone: admin.phone || ''
    });
    setError('');
  }

  const changeAdminForm = (name, value) => {
    setAdminForm(current => ({ ...current, [name]: value }));
  };

  async function saveAdmin(event) {
    event.preventDefault();
    if (!editingAdmin) return;

    setSavingAdmin(true);
    setError('');
    try {
      await api(`/api/church/settings/admins/${editingAdmin.userId}`, {
        method: 'PUT',
        body: JSON.stringify({
          name: adminForm.name.trim(),
          email: adminForm.email.trim(),
          phone: adminForm.phone.trim()
        })
      });
      setEditingAdmin(null);
      tell('Dados do administrador atualizados.');
      await refresh();
    } catch (saveError) {
      setError(saveError.message || 'Não foi possível atualizar o administrador.');
    } finally {
      setSavingAdmin(false);
    }
  }

  async function removeAdmin(admin) {
    if (admins.length <= 1) {
      tell('A igreja precisa manter pelo menos um administrador ativo.');
      return;
    }

    const confirmed = window.confirm(
      `Remover ${admin.name} da administração? A conta continuará ativa como membro e poderá acessar o Portal do Membro.`
    );
    if (!confirmed) return;

    setBusyAdminId(admin.userId);
    setError('');
    try {
      await api(`/api/church/settings/admins/${admin.userId}`, { method: 'DELETE' });
      tell('Administrador removido. O cadastro e o acesso como membro foram preservados.');
      await refresh();
    } catch (removeError) {
      setError(removeError.message || 'Não foi possível remover o administrador.');
    } finally {
      setBusyAdminId(null);
    }
  }

  return (
    <main className="institution-page">
      <header className="institution-header">
        <Brand />
        <div className="institution-header-actions">
          <ThemeToggle theme={theme} onToggle={onToggleTheme} />
          <Button type="button" onClick={onLogout}>Sair</Button>
        </div>
      </header>

      <section className="institution-content">
        <div className="institution-heading">
          <span className="institution-eyebrow">ACESSO INSTITUCIONAL</span>
          <h1>{user.churchName || user.name}</h1>
          <p>Gerencie os administradores e os acessos da sua igreja. As alterações ficam restritas a esta instituição.</p>
        </div>

        {error && <div className="alert alert-error institution-error" role="alert">{error}</div>}

        <section className="institution-card">
          <div className="institution-card-heading">
            <div>
              <h2>Administradores da igreja</h2>
              <p>Edite os dados ou remova o perfil administrativo de alguém. O cadastro como membro será preservado.</p>
            </div>
            <span className={admins.length ? 'institution-status complete' : 'institution-status pending'}>
              {admins.length} ativo(s)
            </span>
          </div>

          <div className="institution-admin-list">
            {loading && admins.length === 0 && <p className="institution-empty">Carregando administradores...</p>}
            {!loading && admins.length === 0 && <p className="institution-empty">Nenhum administrador foi designado ainda.</p>}
            {admins.map(admin => (
              <div className="institution-admin-row" key={admin.userId}>
                <div>
                  <strong>{admin.name}</strong>
                  <span>{admin.email}</span>
                  {admin.phone && <small>{admin.phone}</small>}
                </div>
                <div className="institution-admin-actions">
                  <Button type="button" variant="secondary" size="sm" onClick={() => openEditAdmin(admin)}>Editar</Button>
                  <Button
                    type="button"
                    variant="secondary"
                    size="sm"
                    disabled={admins.length <= 1 || busyAdminId === admin.userId}
                    onClick={() => removeAdmin(admin)}
                  >
                    {busyAdminId === admin.userId ? 'Removendo...' : 'Remover administrador'}
                  </Button>
                </div>
              </div>
            ))}
          </div>
        </section>

        <section className="institution-card institution-promote-card">
          <div className="institution-card-heading">
            <div>
              <h2>Designar administrador</h2>
              <p>Selecione um membro que já tenha uma conta pessoal ativa. Ele usará a própria senha para entrar.</p>
            </div>
            <Button type="button" variant="secondary" size="sm" onClick={refresh} disabled={loading}>Atualizar</Button>
          </div>

          <div className="institution-member-list">
            {loading && members.length === 0 && <p className="institution-empty">Carregando membros...</p>}
            {!loading && members.length === 0 && <p className="institution-empty">Ainda não há membros elegíveis nesta igreja.</p>}
            {members.map(member => (
              <div className="institution-member-row" key={member.id}>
                <div>
                  <strong>{member.name}</strong>
                  <span>{member.email || 'Sem e-mail cadastrado'} · {member.ministry || 'Sem ministério'}</span>
                  {!member.hasUserAccount && <small>Precisa criar uma conta pessoal antes da designação.</small>}
                </div>
                <Button
                  type="button"
                  disabled={!member.hasUserAccount || loading || busyMemberId === member.id}
                  onClick={() => designateAdmin(member)}
                >
                  {busyMemberId === member.id ? 'Designando...' : 'Designar administrador'}
                </Button>
              </div>
            ))}
          </div>
        </section>

        <p className="institution-footnote">
          Remover alguém da administração não apaga a pessoa nem seu histórico. A conta permanece como membro da igreja.
        </p>
      </section>

      {editingAdmin && (
        <Modal
          title="Editar administrador"
          onClose={() => { if (!savingAdmin) setEditingAdmin(null); }}
          onSubmit={saveAdmin}
          saving={savingAdmin}
          submitLabel="Salvar alterações"
          savingLabel="Salvando..."
        >
          <InputField label="Nome completo" name="name" value={adminForm.name} onChange={changeAdminForm} minLength={2} maxLength={160} required />
          <InputField label="E-mail" name="email" type="email" value={adminForm.email} onChange={changeAdminForm} maxLength={254} required />
          <InputField label="Telefone" name="phone" value={adminForm.phone} onChange={changeAdminForm} maxLength={40} />
          {error && <div className="alert alert-error institution-error" role="alert">{error}</div>}
        </Modal>
      )}
    </main>
  );
}
