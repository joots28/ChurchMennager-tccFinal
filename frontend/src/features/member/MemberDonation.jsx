import { useState } from 'react';
import Button from '../../components/ui/Button.jsx';
import InputField from '../../components/ui/InputField.jsx';
import Modal from '../../components/ui/Modal.jsx';
import './MemberDonation.css';

export default function MemberDonation({ onDonate }) {
  const [open, setOpen] = useState(false);
  const [category, setCategory] = useState('Dízimo');
  const [amount, setAmount] = useState('');
  const [receiptUrl, setReceiptUrl] = useState('');
  const [saving, setSaving] = useState(false);
  const [error, setError] = useState('');

  async function submit(event) {
    event.preventDefault();
    setError('');
    const numericAmount = Number(String(amount).replace(',', '.'));
    if (!Number.isFinite(numericAmount) || numericAmount <= 0) {
      setError('Informe um valor válido maior que zero.');
      return;
    }

    setSaving(true);
    try {
      await onDonate({
        category,
        amount: numericAmount,
        receiptUrl: receiptUrl.trim() || null
      });
      setOpen(false);
      setAmount('');
      setReceiptUrl('');
    } catch (submitError) {
      setError(submitError.message || 'Não foi possível registrar a contribuição.');
    } finally {
      setSaving(false);
    }
  }

  return (
    <>
      <div className="member-donation">
        <div>
          <h2>Contribua com a comunidade</h2>
          <p>Registre uma oferta ou dízimo já realizado. A contribuição ficará pendente até a conferência da igreja.</p>
        </div>
        <Button icon="heart" onClick={() => { setError(''); setOpen(true); }}>Registrar contribuição</Button>
      </div>

      {open && (
        <Modal
          title="Registrar contribuição"
          onClose={() => setOpen(false)}
          onSubmit={submit}
          saving={saving}
        >
          <div className="donation-form">
            <label className="field">
              <span className="field-label">Tipo de contribuição</span>
              <select value={category} onChange={event => setCategory(event.target.value)} required>
                <option value="Dízimo">Dízimo</option>
                <option value="Oferta">Oferta</option>
              </select>
            </label>
            <InputField
              label="Valor (R$)"
              name="amount"
              type="number"
              value={amount}
              onChange={(_, value) => setAmount(value)}
              placeholder="0,00"
              min="0.01"
              step="0.01"
              required
            />
            <InputField
              label="Link do comprovante (opcional)"
              name="receiptUrl"
              type="url"
              value={receiptUrl}
              onChange={(_, value) => setReceiptUrl(value)}
              placeholder="https://..."
              maxLength={500}
            />
            {error && <p className="donation-form-error" role="alert">{error}</p>}
            <p className="donation-form-note">Este registro não realiza pagamento. A tesouraria confere a contribuição antes de lançá-la no financeiro.</p>
          </div>
        </Modal>
      )}
    </>
  );
}
