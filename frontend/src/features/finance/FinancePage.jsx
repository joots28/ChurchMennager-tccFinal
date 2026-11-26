import Badge from '../../components/ui/Badge.jsx';
import Button from '../../components/ui/Button.jsx';
import MetricCard from '../../components/ui/MetricCard.jsx';
import EmptyState from '../../components/ui/EmptyState.jsx';
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

export default function FinancePage({ data, meta, filtered, edit, remove, exportCsv, onDonationStatusChange }) {
  const transactions = data.transactions || [];
  const donations = data.donations || [];
  const pendingDonations = donations.filter(donation => donation.status === 'Pendente');
  const income = transactions
    .filter((transaction) => transaction.type === 'Entrada')
    .reduce((total, transaction) => total + Number(transaction.amount), 0);
  const expenses = transactions
    .filter((transaction) => transaction.type === 'Saída')
    .reduce((total, transaction) => total + Number(transaction.amount), 0);

  return (
    <>
      <div className="finance-metrics">
        <MetricCard label="Entradas registradas" value={currency(income)} sub="Contribuições já confirmadas" icon="trend" />
        <MetricCard label="Saídas registradas" value={currency(expenses)} sub="Lançamentos de saída" icon="wallet" />
        <MetricCard label="Saldo do período" value={currency(income - expenses)} sub="Entradas menos saídas" icon="wallet" />
      </div>

      <section className="panel pending-donations-panel">
        <div className="panel-heading">
          <div>
            <h2>Ofertas e dízimos pendentes</h2>
            <p>{pendingDonations.length} contribuição(ões) aguardando conferência</p>
          </div>
          <Badge>{pendingDonations.length} pendente(s)</Badge>
        </div>

        {pendingDonations.length ? (
          <div className="pending-donations-list">
            {pendingDonations.map(donation => (
              <article className="pending-donation-row" key={donation.id}>
                <div className="pending-donation-main">
                  <strong>{donation.donorName}</strong>
                  <span>{donation.category} · {dateBR(donation.donationDate)}</span>
                </div>
                <strong className="pending-donation-amount">{currency(donation.amount)}</strong>
                {donation.receiptUrl && (
                  <a href={donation.receiptUrl} target="_blank" rel="noreferrer">Ver comprovante</a>
                )}
                <div className="pending-donation-actions">
                  <Button size="sm" onClick={() => onDonationStatusChange(donation.id, 'Confirmada')}>Confirmar</Button>
                  <Button size="sm" variant="secondary" onClick={() => onDonationStatusChange(donation.id, 'Recusada')}>Recusar</Button>
                </div>
              </article>
            ))}
          </div>
        ) : (
          <EmptyState text="Nenhuma oferta ou dízimo aguardando conferência." />
        )}
      </section>

      <ResourceTablePage title={meta[0]} rows={filtered} columns={columns} onEdit={edit} onDelete={remove} onExport={exportCsv} />
    </>
  );
}
