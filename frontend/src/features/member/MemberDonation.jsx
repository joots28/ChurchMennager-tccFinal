import { useEffect, useState } from 'react';
import Badge from '../../components/ui/Badge.jsx';
import Button from '../../components/ui/Button.jsx';
import InputField from '../../components/ui/InputField.jsx';
import Modal from '../../components/ui/Modal.jsx';
import SelectField from '../../components/ui/SelectField.jsx';
import { api } from '../../services/httpClient.js';
import { currency } from '../../utils/formatters.js';
import './MemberDonation.css';

const categories = ['Dízimo', 'Oferta', 'Missões', 'Ação social', 'Construção', 'Outra'];
const paymentMethods = ['PIX', 'Dinheiro', 'Transferência bancária'];

export default function MemberDonation() {
  const [showForm, setShowForm] = useState(false);
  const [donations, setDonations] = useState([]);
  const [loading, setLoading] = useState(true);
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');
  const [form, setForm] = useState({ amount: '', category: 'Dízimo', paymentMethod: 'PIX' });

  useEffect(() => {
    let active = true;

    api('/api/donations/my')
      .then(items => {
        if (active) setDonations(Array.isArray(items) ? items : []);
      })
      .catch(() => {
        if (active) setError('Não foi possível carregar seu histórico de contribuições.');
      })
      .finally(() => {
        if (active) setLoading(false);
      });

    return () => { active = false; };
  }, []);

  const change = (name, value) => setForm(current => ({ ...current, [name]: value }));

  async function submitDonation(event) {
    event.preventDefault();
    setError('');

    const amount = Number(form.amount);
    if (!Number.isFinite(amount) || amount < 0.01 || amount > 1000000) {
      setError('Informe um valor entre R$ 0,01 e R$ 1.000.000,00.');
      return;
    }

    setSaving(true);
    try {
      const donation = await api('/api/donations', {
        method: 'POST',
        body: JSON.stringify({
          amount,
          category: form.category,
          paymentMethod: form.paymentMethod
        })
      });

      setDonations(current => [donation, ...current]);
      setShowForm(false);
      setForm({ amount: '', category: 'Dízimo', paymentMethod: 'PIX' });
    } catch (submitError) {
      setError(submitError.message || 'Não foi possível registrar sua contribuição.');
    } finally {
      setSaving(false);
    }
  }

  const statusLabel = status => {
    if (status === 'Confirmada') return 'Recebida';
    if (status === 'Recusada') return 'Não aprovada';
    return 'Aguardando confirmação';
  };

  return (
    <>
      <div className="member-donation">
        <div>
          <h2>Contribua com a comunidade</h2>
          <p>Escolha a finalidade, o valor e a forma de contribuição. A igreja confirma o recebimento.</p>
        </div>
        <Button icon="heart" onClick={() => { setError(''); setShowForm(true); }}>Ver opções</Button>
      </div>

      <section className="panel member-donation-history">
        <div className="panel-heading">
          <div>
            <h2>Minhas contribuições</h2>
            <p>Acompanhe os pedidos e as confirmações da igreja</p>
          </div>
          <Button variant="secondary" size="sm" onClick={() => {
            setLoading(true);
            api('/api/donations/my').then(items => setDonations(Array.isArray(items) ? items : []))
              .catch(loadError => setError(loadError.message || 'Não foi possível atualizar o histórico.'))
              .finally(() => setLoading(false));
          }}>Atualizar</Button>
        </div>

        <div className="member-donation-history-list">
          {loading && <p className="donation-empty">Carregando contribuições...</p>}
          {!loading && donations.length === 0 && <p className="donation-empty">Você ainda não registrou contribuições.</p>}
          {!loading && donations.map(donation => (
            <article className="member-donation-history-row" key={donation.id}>
              <div className="member-donation-history-info">
                <strong>{donation.category}</strong>
                <small>{donation.paymentMethod} · {new Date(donation.donationDate).toLocaleDateString('pt-BR')}</small>
              </div>
              <div className="member-donation-history-value">
                <strong>{currency(donation.amount)}</strong>
                <Badge>{statusLabel(donation.status)}</Badge>
              </div>
            </article>
          ))}
        </div>
      </section>

      {showForm && (
        <Modal
          title="Registrar contribuição"
          onClose={() => { if (!saving) setShowForm(false); }}
          onSubmit={submitDonation}
          saving={saving}
          submitLabel="Enviar pedido"
          savingLabel="Enviando..."
        >
          <div className="donation-form-note">
            Este formulário registra sua intenção de contribuir. O aplicativo não processa pagamentos. A contribuição só será lançada no financeiro depois que a administração confirmar o recebimento.
          </div>
          <InputField
            label="Valor da contribuição"
            name="amount"
            type="number"
            min="0.01"
            max="1000000"
            step="0.01"
            placeholder="Ex.: 50,00"
            value={form.amount}
            onChange={change}
            required
          />
          <SelectField label="Finalidade" name="category" value={form.category} onChange={change} options={categories} required />
          <SelectField label="Forma de contribuição" name="paymentMethod" value={form.paymentMethod} onChange={change} options={paymentMethods} required />
          {error && <div className="alert alert-error donation-form-error" role="alert">{error}</div>}
        </Modal>
      )}
    </>
  );
}
