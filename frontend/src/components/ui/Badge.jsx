import './Badge.css';

export default function Badge({ children }) {
  const status = String(children).toLowerCase();
  const variant = status.includes('recus') || status.includes('não aprovada')
    ? 'danger'
    : status.includes('inativ') || status.includes('pend') || status.includes('aguardando') || status.includes('saída')
      ? 'warning'
      : status.includes('planej')
        ? 'info'
        : 'success';

  return <span className={`badge badge-${variant}`}>{children}</span>;
}
