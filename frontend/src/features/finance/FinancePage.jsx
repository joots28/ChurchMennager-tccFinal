import { useEffect, useState } from 'react';
import Badge from '../../components/ui/Badge.jsx';
import Button from '../../components/ui/Button.jsx';
import MetricCard from '../../components/ui/MetricCard.jsx';
import { api } from '../../services/httpClient.js';
import { apiResource } from '../../services/resourceService.js';
import { subscribeToUpdates } from '../../services/index.js';
import { currency, dateBR } from '../../utils/formatters.js';
import ResourceTablePage from '../management/ResourceTablePage.jsx';
import './FinancePage.css';

const columns = [
  {
    key: 'description',
    label: 'Descrição',
    render: (value, transaction) => (
      <div>
        <strong>{value}</strong>
        <small className="cell-subtitle">{transaction.category}</small>
      </div>
    ),
  },
  { key: 'date', label: 'Data', render: (value) => dateBR(value) },
  { key: 'method', label: 'Método' },
  { key: 'type', label: 'Tipo', render: (value) => <Badge>{value}</Badge> },
  {
    key: 'amount',
    label: 'Valor',
    render: (value, transaction) => (
      <strong className={transaction.type === 'Entrada' ? 'amount-positive' : 'amount-negative'}>
        {transaction.type === 'Entrada' ? '+ ' : '− '}{currency(value)}
      </strong>
    ),
  },
];

export default function FinancePage({ data, setData, meta, filtered, edit, remove, exportCsv, demo, tell }) {
  const [donations, setDonations] = useState([]);
  const [loadingDonations, setLoadingDonations] = useState(false);
  const [busyDonationId, setBusyDonationId] = useState(null);

  const income = data.transactions
    .filter(transaction => transaction.type === 'Entrada')
    .reduce((total, transaction) => total + Number(transaction.amount), 0);
  const expenses = data.transactions
    .filter(transaction => transaction.type === 'Saída')
    .reduce((total, transaction) => total + Number(transaction.amount), 0);

  async function refreshDonations() {
    if (demo) {
      setDonations([]);
      return;
    }

    setLoadingDonations(true);
    try {
      const items = await api('/api/donations');
      setDonations(Array.isArray(items) ? items : []);
    } catch (error) {
      tell?.(error.message || 'Não foi possível carregar as contribuições.');
    } finally {
      setLoadingDonations(false);
    }
  }

  useEffect(() => {
    refreshDonations();
    if (demo) return undefined;

    const unsubscribe = subscribeToUpdates(payload => {
      if (String(payload?.resource || '').toLowerCase() === 'donations') {
        refreshDonations();
      }
    });

    return unsubscribe;
  }, [demo]);

  async function resolveDonation(donation, action) {
    setBusyDonationId(donation.id);
    try {
      await api(`/api/donations/${donation.id}/${action}`, { method: 'POST' });
      await refreshDonations();

      if (action === 'confirm') {
        const transactions = await apiResource('transactions');
        setData(current => ({ ...current, transactions: transactions || [] }));
        tell?.('Recebimento confirmado e lançado no controle financeiro.');
      } else {
        tell?.('Pedido de contribuição recusado. Nenhuma entrada financeira foi criada.');
      }
    } catch (error) {
      tell?.(error.message || 'Não foi possível atualizar a contribuição.');
    } finally {
      setBusyDonationId(null);
    }
  }

  const donationStatus = status => status === 'Confirmada'
    ? 'Recebida'
    : status === 'Recusada'
      ? 'Não aprovada'
      : 'Pendente';

  return (
    <>
      <section className="panel finance-donations-panel">
        <div className="panel-heading">
          <div>
            <h2>Contribuições dos membros</h2>
            <p>Confirme somente os valores que a igreja já recebeu.</p>
          </div>
          <Button variant="secondary" size="sm" onClick={refreshDonations} disabled={loadingDonations}>Atualizar</Button>
        </div>

        <div className="finance-donations-list">
          {loadingDonations && <p className="finance-donations-empty">Carregando contribuições...</p>}
          {!loadingDonations && donations.length === 0 && (
            <p className="finance-donations-empty">Nenhuma contribuição foi registrada ainda.</p>
          )}
          {!loadingDonations && donations.map(donation => (
            <article className="finance-donation-row" key={donation.id}>
              <div className="finance-donation-info">
                <strong>{donation.donorName} · {donation.category}</strong>
                <small>{donation.paymentMethod} · {new Date(donation.donationDate).toLocaleDateString('pt-BR')}</small>
              </div>
              <div className="finance-donation-actions">
                <strong>{currency(donation.amount)}</strong>
                <Badge>{donationStatus(donation.status)}</Badge>
                {donation.status === 'Pendente' && (
                  <div className="finance-donation-buttons">
                    <Button
                      size="sm"
                      disabled={busyDonationId === donation.id}
                      onClick={() => resolveDonation(donation, 'confirm')}
                    >
                      {busyDonationId === donation.id ? 'Aguarde...' : 'Confirmar recebimento'}
                    </Button>
                    <Button
                      size="sm"
                      variant="secondary"
                      disabled={busyDonationId === donation.id}
                      onClick={() => resolveDonation(donation, 'reject')}
                    >
                      Recusar
                    </Button>
                  </div>
                )}
              </div>
            </article>
          ))}
        </div>
      </section>

      <div className="finance-metrics">
        <MetricCard label="Entradas registradas" value={currency(income)} sub="Lançamentos de entrada" icon="trend" />
        <MetricCard label="Saídas registradas" value={currency(expenses)} sub="Lançamentos de saída" icon="wallet" />
        <MetricCard label="Saldo do período" value={currency(income - expenses)} sub="Saldo demonstrativo" icon="wallet" />
      </div>
      <ResourceTablePage title={meta[0]} rows={filtered} columns={columns} onEdit={edit} onDelete={remove} onExport={exportCsv} />
    </>
  );
}
