import { useEffect, useState } from 'react';
import Button from '../../components/ui/Button.jsx';
import InputField from '../../components/ui/InputField.jsx';
import Modal from '../../components/ui/Modal.jsx';
import { api } from '../../services/httpClient.js';
import './AdminsManagement.css';

export default function AdminsManagement({ tell }) {
  const [admins, setAdmins] = useState([]);
  const [eligibleMembers, setEligibleMembers] = useState([]);
  const [selectedMemberId, setSelectedMemberId] = useState('');
  const [editing, setEditing] = useState(null);
  const [form, setForm] = useState({ name: '', email: '', phone: '' });
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  async function loadManagementData() {
    setLoading(true);
    setError('');
    try {
      const [adminList, memberList] = await Promise.all([
        api('/api/church/settings/admins'),
        api('/api/church/settings/eligible-members')
      ]);
      setAdmins(Array.isArray(adminList) ? adminList : []);
      setEligibleMembers((Array.isArray(memberList) ? memberList : []).filter(member =>
        member.hasUserAccount && member.role !== 'Administrador'
      ));
    } catch (loadError) {
      setError(loadError.message || 'Não foi possível carregar os administradores.');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => { loadManagementData(); }, []);

  async function promoteSelectedMember(event) {
    event.preventDefault();
    if (!selectedMemberId) return;
    setSaving(true);
    setError('');
    try {
      const result = await api('/api/church/settings/promote-admin', {
        method: 'POST',
        body: JSON.stringify({ memberId: Number(selectedMemberId) })
      });
      setSelectedMemberId('');
      await loadManagementData();
      tell(result.message || 'Administrador adicionado.');
    } catch (promotionError) {
      setError(promotionError.message || 'Não foi possível promover o membro.');
    } finally {
      setSaving(false);
    }
  }

  function openEdit(admin) {
    setEditing(admin);
    setForm({ name: admin.name || '', email: admin.email || '', phone: admin.phone || '' });
    setError('');
  }

  async function saveAdmin(event) {
    event.preventDefault();
    if (!editing) return;
    setSaving(true);
    setError('');
    try {
      await api(`/api/church/settings/admins/${editing.userId}`, {
        method: 'PUT',
        body: JSON.stringify({ name: form.name.trim(), email: form.email.trim(), phone: form.phone.trim() })
      });
      setEditing(null);
      await loadManagementData();
      tell('Dados do administrador atualizados.');
    } catch (saveError) {
      setError(saveError.message || 'Não foi possível atualizar o administrador.');
    } finally {
      setSaving(false);
    }
  }

  async function revokeAdmin(admin) {
    const confirmed = window.confirm(
      `Remover ${admin.name} da administração? A conta continuará ativa como membro e poderá acessar o Portal do Membro.`
    );
    if (!confirmed) return;
    setError('');
    try {
      await api(`/api/church/settings/admins/${admin.userId}`, { method: 'DELETE' });
      await loadManagementData();
      tell('Administrador removido. A conta continua ativa como membro.');
    } catch (deleteError) {
      setError(deleteError.message || 'Não foi possível revogar o acesso.');
    }
  }

  const changeForm = (name, value) => setForm(current => ({ ...current, [name]: value }));

  return (
    <>
      <section className="panel admins-management-panel">
        <div className="panel-heading">
          <div>
            <h2>Administradores da igreja</h2>
            <p>Gerencie quem possui acesso administrativo à instituição.</p>
          </div>
          <Button size="sm" variant="secondary" icon="refresh" onClick={loadManagementData} disabled={loading}>Atualizar</Button>
        </div>

        {error && <p className="admins-management-error" role="alert">{error}</p>}

        <div className="admins-promote-box">
          <form onSubmit={promoteSelectedMember}>
            <label className="field">
              <span className="field-label">Promover membro a administrador</span>
              <select value={selectedMemberId} onChange={event => setSelectedMemberId(event.target.value)} disabled={loading || saving || eligibleMembers.length === 0}>
                <option value="">Selecione um membro com acesso ativo</option>
                {eligibleMembers.map(member => <option key={member.id} value={member.id}>{member.name} · {member.email}</option>)}
              </select>
            </label>
            <Button type="submit" disabled={saving || loading || !selectedMemberId}>{saving ? 'Salvando...' : 'Adicionar administrador'}</Button>
          </form>
          <p className="admins-management-hint">Apenas membros com conta ativa podem ser promovidos. A senha pessoal do membro será mantida.</p>
        </div>

        <div className="admins-list">
          {loading ? <p className="admins-management-empty">Carregando administradores...</p> :
            admins.length === 0 ? <p className="admins-management-empty">Nenhum administrador pessoal ativo foi encontrado.</p> :
            admins.map(admin => (
              <article className="admin-management-row" key={admin.userId}>
                <div className="admin-management-identity">
                  <span className="admin-management-avatar">{(admin.name || '?').trim().slice(0, 1).toUpperCase()}</span>
                  <div>
                    <strong>{admin.name}</strong>
                    <span>{admin.email}</span>
                    {admin.phone && <small>{admin.phone}</small>}
                  </div>
                </div>
                <div className="admin-management-actions">
                  <Button size="sm" variant="secondary" onClick={() => openEdit(admin)}>Editar</Button>
                  <Button size="sm" variant="secondary" onClick={() => revokeAdmin(admin)}>Remover administrador</Button>
                </div>
              </article>
            ))
          }
        </div>
      </section>

      {editing && (
        <Modal title="Editar administrador" onClose={() => setEditing(null)} onSubmit={saveAdmin} saving={saving}>
          <div className="admin-edit-form">
            <InputField label="Nome completo" name="name" value={form.name} onChange={changeForm} minLength={2} maxLength={160} required />
            <InputField label="E-mail" name="email" type="email" value={form.email} onChange={changeForm} maxLength={254} required />
            <InputField label="Telefone" name="phone" value={form.phone} onChange={changeForm} maxLength={40} />
            {error && <p className="admins-management-error" role="alert">{error}</p>}
          </div>
        </Modal>
      )}
    </>
  );
}
