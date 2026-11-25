import { useEffect, useState } from 'react';
import { api } from '../../services/httpClient.js';
import Button from '../../components/ui/Button.jsx';
import Brand from '../../components/ui/Brand.jsx';
import ThemeToggle from '../../components/ui/ThemeToggle.jsx';
import './ChurchInstitutionalPortal.css';

export default function ChurchInstitutionalPortal({ user, data, onLogout, theme, onToggleTheme, tell }) {
  const [admins, setAdmins] = useState(data.admins || []);
  const [members, setMembers] = useState(data.eligibleMembers || []);
  const [loading, setLoading] = useState(false);
  const [promotingId, setPromotingId] = useState(null);

  async function refresh() {
    setLoading(true);
    try {
      const [nextAdmins, nextMembers] = await Promise.all([
        api('/api/church/settings/admins'),
        api('/api/church/settings/eligible-members')
      ]);
      setAdmins(nextAdmins || []);
      setMembers(nextMembers || []);
    } catch (error) {
      tell(error.message || 'Não foi possível carregar os dados institucionais.');
    } finally {
      setLoading(false);
    }
  }

  useEffect(() => {
    refresh();
  }, []);

  async function designateFirstAdmin(member) {
    if (!member.hasUserAccount) {
      tell('Este membro precisa criar uma conta pessoal pelo Portal do Membro antes de receber acesso administrativo.');
      return;
    }

    setPromotingId(member.id);
    try {
      await api('/api/church/settings/promote-admin', {
        method: 'POST',
        body: JSON.stringify({ memberId: member.id })
      });
      tell('Primeiro administrador designado. Ele já pode entrar pelo acesso de Administrador com a própria conta.');
      await refresh();
    } catch (error) {
      tell(error.message || 'Não foi possível designar o administrador.');
    } finally {
      setPromotingId(null);
    }
  }

  const hasAdmin = admins.length > 0;

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
          <p>Este ambiente administra a identidade institucional e o provisionamento inicial da igreja.</p>
        </div>

        <section className="institution-card">
          <div className="institution-card-heading">
            <div>
              <h2>Administrador responsável</h2>
              <p>
                {hasAdmin
                  ? 'O primeiro administrador já foi designado. A gestão cotidiana deve ser realizada pelo acesso administrativo.'
                  : 'Selecione um membro existente que já tenha criado sua conta pessoal.'}
              </p>
            </div>
            <span className={hasAdmin ? 'institution-status complete' : 'institution-status pending'}>
              {hasAdmin ? 'Configurado' : 'Configuração inicial'}
            </span>
          </div>

          {hasAdmin ? (
            <div className="institution-admin-list">
              {admins.map(admin => (
                <div className="institution-admin-row" key={admin.userId}>
                  <div>
                    <strong>{admin.name}</strong>
                    <span>{admin.email}</span>
                  </div>
                  <span className="institution-role">{admin.role}</span>
                </div>
              ))}
              <p className="institution-help">Para promover outros membros, entre no sistema pelo acesso de Administrador e utilize Configurações.</p>
            </div>
          ) : (
            <div className="institution-member-list">
              {members.length === 0 && <p className="institution-empty">Ainda não há membros cadastrados nesta igreja.</p>}
              {members.map(member => (
                <div className="institution-member-row" key={member.id}>
                  <div>
                    <strong>{member.name}</strong>
                    <span>{member.email || 'Sem e-mail cadastrado'} · {member.ministry || 'Sem ministério'}</span>
                    {!member.hasUserAccount && <small>Precisa criar uma conta pessoal antes da designação.</small>}
                  </div>
                  <Button
                    type="button"
                    disabled={!member.hasUserAccount || loading || promotingId === member.id}
                    onClick={() => designateFirstAdmin(member)}
                  >
                    {promotingId === member.id ? 'Designando...' : 'Designar administrador'}
                  </Button>
                </div>
              ))}
            </div>
          )}
        </section>

        <p className="institution-footnote">
          A conta institucional não é uma conta pessoal e não substitui o perfil do administrador ou do membro.
        </p>
      </section>
    </main>
  );
}
