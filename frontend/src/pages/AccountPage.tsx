import { useState, type FormEvent } from 'react';
import { Alert, Badge, Button, Form, Modal, Spinner } from 'react-bootstrap';
import { apiErrorMessage } from '../api';
import { useAuth } from '../auth';

export default function AccountPage() {
  const { user, deleteAccount } = useAuth();
  const [showDelete, setShowDelete] = useState(false);
  const [password, setPassword] = useState('');
  const [confirmEmail, setConfirmEmail] = useState('');
  const [confirmation, setConfirmation] = useState('');
  const [deleting, setDeleting] = useState(false);
  const [error, setError] = useState('');
  if (!user) return null;
  const hasPassword = user.hasPassword !== false;
  const ready = confirmation === 'DELETE' && (hasPassword ? password.length > 0 : confirmEmail.trim().length > 0);

  function close() {
    if (deleting) return;
    setShowDelete(false);
    setPassword('');
    setConfirmEmail('');
    setConfirmation('');
    setError('');
  }

  async function submit(event: FormEvent<HTMLFormElement>) {
    event.preventDefault();
    if (!ready) return;
    setDeleting(true);
    setError('');
    try {
      await deleteAccount(hasPassword ? { password, confirmation } : { confirmEmail, confirmation });
      // deleteAccount reloads the app on success.
    } catch (deleteError) {
      setError(apiErrorMessage(deleteError));
      setDeleting(false);
    }
  }

  return <>
    <div className="page-heading">
      <div><div className="eyebrow">YOUR ACCOUNT</div><h1>Account</h1><p className="text-secondary mb-0">Your sign-in details and privacy controls.</p></div>
    </div>

    <div className="content-card p-3 p-md-4 mb-4">
      <h2 className="section-title mb-3">Profile</h2>
      <dl className="row mb-0 account-details">
        <dt className="col-sm-3">Email</dt><dd className="col-sm-9 text-break">{user.email}</dd>
        <dt className="col-sm-3">Sign-in method</dt><dd className="col-sm-9">{hasPassword ? <Badge bg="light" text="dark">Email &amp; password</Badge> : <Badge bg="light" text="dark">Google</Badge>}</dd>
      </dl>
    </div>

    <div className="content-card danger-zone p-3 p-md-4">
      <div className="d-flex flex-wrap justify-content-between align-items-start gap-3">
        <div>
          <div className="eyebrow text-danger">DANGER ZONE</div>
          <h2 className="section-title mb-1">Delete my account</h2>
          <p className="small text-secondary mb-0">Permanently removes your account and everything in it. This cannot be undone.</p>
        </div>
        <Button variant="outline-danger" onClick={() => setShowDelete(true)}>Delete account…</Button>
      </div>
    </div>

    <Modal show={showDelete} onHide={close} centered>
      <Form onSubmit={submit}>
        <Modal.Header closeButton={!deleting}><Modal.Title>Delete your account?</Modal.Title></Modal.Header>
        <Modal.Body>
          <Alert variant="danger" className="small">
            <strong>This is permanent.</strong> We will delete:
            <ul className="mb-0 mt-1">
              <li>All accounts, transactions and transfers</li>
              <li>Your private categories and auto-categorization rules</li>
              <li>Every uploaded statement file</li>
              <li>Your sign-in (email/password and Google)</li>
            </ul>
          </Alert>
          {error && <Alert variant="danger">{error}</Alert>}
          {hasPassword
            ? <Form.Group className="mb-3" controlId="delete-password"><Form.Label>Your password</Form.Label>
                <Form.Control type="password" autoComplete="current-password" value={password} onChange={event => setPassword(event.target.value)} autoFocus disabled={deleting} /></Form.Group>
            : <Form.Group className="mb-3" controlId="delete-email"><Form.Label>Type your email <span className="text-secondary fw-normal">({user.email})</span></Form.Label>
                <Form.Control type="email" autoComplete="off" value={confirmEmail} onChange={event => setConfirmEmail(event.target.value)} autoFocus disabled={deleting} /></Form.Group>}
          <Form.Group controlId="delete-confirm"><Form.Label>Type <strong>DELETE</strong> to confirm</Form.Label>
            <Form.Control autoComplete="off" value={confirmation} onChange={event => setConfirmation(event.target.value)} disabled={deleting} placeholder="DELETE" /></Form.Group>
        </Modal.Body>
        <Modal.Footer>
          <Button variant="outline-secondary" onClick={close} disabled={deleting}>Cancel</Button>
          <Button variant="danger" type="submit" disabled={!ready || deleting}>{deleting ? <><Spinner size="sm" className="me-2" />Deleting…</> : 'Permanently delete'}</Button>
        </Modal.Footer>
      </Form>
    </Modal>
  </>;
}
