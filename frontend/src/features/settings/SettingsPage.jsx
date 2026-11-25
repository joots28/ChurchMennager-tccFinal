import { useEffect, useState } from 'react';
import Icon from '../../components/Icon.jsx';
import Button from '../../components/ui/Button.jsx';
import InputField from '../../components/ui/InputField.jsx';
import { api } from '../../services/httpClient.js';
import './SettingsPage.css';
import '../../components/ui/Panel.css';

export default function SettingsPage({ data, setData, demo, tell }) {
  const [admins, setAdmins] = useState(data.admins || []);
  const [eligibleMembers, setEligibleMembers] = useState(data.eligibleMembers || []);
  const [busyMemberId, setBusyMemberId] = useState(null);

  const updateSetting = (key, value) => {
    setData(current => ({
      ...current,
      settings: { ...current.settings, [key]: value }
    }));
  };

  async function refreshAccess() {
    if (demo) return;
    const [nextAdmins, nextMembers] = await Promise.all([
      api('/api/church/settings/admins'),
      api('/api/church/settings/eligible-members')
    ]);
    setAdmins(nextAdmins || []);
    setEligibleMembers(nextMembers || []);
    setData(current => ({
      ...current,
      admins: nextAdmins || [],
      eligibleMembers: nextMembers || []
    }));
  }

  useEffect(() => {
    refreshAccess().catch(error => tell(error.message || 'Não foi possível carregar os acessos administrativos.'));
  }, [demo]);

  async function saveSettings(event) {
    event.preventDefault();

    try {
      const settings = {
        ...data.settings,
        churchName: String(data.settings.churchName ?? '').trim(),
        email: String(data.settings.email ?? '').trim().toLowerCase(),
        phone: String(data.settings.phone ?? '').trim(),
        address: String(data.settings.address ?? '').trim()
      };

      if (settings.churchName.length < 2 || settings.churchName.length > 100) {
        throw new Error('O nome da igreja deve ter de 2 a 100 caracteres.');
      }
      if (settings.email.length > 254 || !/^[^\\s@]+@[^\\s@]+\\.[^\\s@]+$/.test(settings.email)) {
        throw new Error('Informe um e-mail institucional válido.');
      }
      if (settings.phone.length > 40 || settings.address.length > 250) {
        throw new Error('Telefone deve ter até 40 caracteres e endereço até 250 caracteres.');
      }

      if (!demo) {
        const updated = await api('/api/church/settings', {
          method: 'PUT',
          body: JSON.stringify(settings)
        });
        setData(current => ({ ...current, settings: updated }));
      } else {
        setData(current => ({ ...current, settings }));
      }

      tell('Configurações atualizadas.');
    } catch (error) {
      tell(error.message || 'Não foi possível salvar as configurações.');
    }
  }

  async function promoteMember(member) {
    if (!member.hasUserAccount) {
      tell('O membro precisa criar uma conta pessoal antes de receber acesso administrativo.');
      return;
    }

    setBusyMemberId(member.id);
    try {
      await api('/api/church/settings/promote-admin', {
        method: 'POST',
        body: JSON.stringify({ memberId: member.id })
      });
      tell(`${member.name} agora possui acesso administrativo. Ele deve entrar novamente para atualizar a sessão.`);
      await refreshAccess();
    } catch (error) {
      tell(error.message || 'Não foi possível promover o membro.');
    } finally {
      setBusyMemberId(null);
    }
  }

  async function demoteAdmin(admin) {
    if (demo) {
      tell('A alteração de permissões está desativada na demonstração.');
      return;
    }

    setBusyMemberId(admin.userId);
    try {
      await api('/api/church/settings/demote-admin', {
        method: 'POST',
        body: JSON.stringify({ userId: admin.userId })
      });
      tell(`O acesso administrativo de ${admin.name} foi removido.`);
      await refreshAccess();
    } catch (error) {
      tell(error.message || 'Não foi possível remover o acesso administrativo.');
    } finally {
      setBusyMemberId(null);
    }
  }

  return (
    <section className="settings-stack">
      <section className="panel settings-panel">
        <div className="panel-heading">
          <div>
            <h2>Dados da instituição</h2>
            <p>Informações utilizadas na identificação do sistema</p>
          </div>
        </div>

        <form className="settings-grid" onSubmit={saveSettings}>
          <InputField label="Nome da igreja" name="churchName" value={data.settings.churchName} onChange={updateSetting} minLength={2} maxLength={100} required />
          <InputField label="E-mail institucional" name="email" type="email" value={data.settings.email} onChange={updateSetting} maxLength={254} required />
          <InputField label="Telefone (opcional)" name="phone" value={data.settings.phone} onChange={updateSetting} maxLength={40} />
          <InputField label="Endereço (opcional)" name="address" value={data.settings.address} onChange={updateSetting} maxLength={250} />
          <div className="settings-submit">
            <Button type="submit">Salvar configurações</Button>
          </div>
        </form>

        <div className="security-note">
          <Icon name="shield" />
          <div>
            <strong>Segurança e permissões</strong>
            <p>Somente administradores podem gerenciar os dados operacionais e os acessos administrativos da igreja.</p>
          </div>
        </div>
      </section>

      <section className="panel settings-panel access-management-panel">
        <div className="panel-heading">
          <div>
            <h2>Administradores da igreja</h2>
            <p>O perfil administrativo é atribuído a membros existentes, nunca durante o cadastro pessoal.</p>
          </div>
          <Button type="button" onClick={() => refreshAccess().catch(error => tell(error.message))}>Atualizar</Button>
        </div>

        <div className="access-list">
          {admins.length === 0 && <p className="access-empty">Nenhum administrador ativo encontrado.</p>}
          {admins.map(admin => (
            <div className="access-row" key={admin.userId}>
              <div>
                <strong>{admin.name}</strong>
                <span>{admin.email}</span>
                <small>Administrador</small>
              </div>
              <Button
                type="button"
                disabled={admins.length <= 1 || busyMemberId === admin.userId}
                onClick={() => demoteAdmin(admin)}
              >
                {busyMemberId === admin.userId ? 'Salvando...' : 'Remover acesso'}
              </Button>
            </div>
          ))}
        </div>

        <div className="access-section-heading">
          <h3>Promover membro</h3>
          <p>Somente membros com conta pessoal ativa podem receber acesso administrativo.</p>
        </div>

        <div className="access-list">
          {eligibleMembers.length === 0 && <p className="access-empty">Não há membros elegíveis para promoção.</p>}
          {eligibleMembers.map(member => (
            <div className="access-row" key={member.id}>
              <div>
                <strong>{member.name}</strong>
                <span>{member.email || 'Sem e-mail cadastrado'}</span>
                <small>{member.hasUserAccount ? 'Conta pessoal vinculada' : 'Precisa criar uma conta pessoal'}</small>
              </div>
              <Button
                type="button"
                disabled={!member.hasUserAccount || busyMemberId === member.id}
                onClick={() => promoteMember(member)}
              >
                {busyMemberId === member.id ? 'Salvando...' : 'Promover a administrador'}
              </Button>
            </div>
          ))}
        </div>
      </section>
    </section>
  );
}
