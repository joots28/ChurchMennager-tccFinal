import * as signalR from '@microsoft/signalr';

let connection = null;
const listeners = new Set();

export function initSignalR(apiUrl, onEntityChanged) {
  if (onEntityChanged) listeners.add(onEntityChanged);

  if (connection && connection.state === signalR.HubConnectionState.Connected) {
    return connection;
  }

  const token = sessionStorage.getItem('churchmanager-token');
  if (!token) return null;

  const hubUrl = `${apiUrl.replace(/\/$/, '')}/hub/church`;

  connection = new signalR.HubConnectionBuilder()
    .withUrl(hubUrl, {
      accessTokenFactory: () => sessionStorage.getItem('churchmanager-token') || '',
      skipNegotiation: false,
      transport: signalR.HttpTransportType.WebSockets | signalR.HttpTransportType.LongPolling
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000])
    .configureLogging(signalR.LogLevel.Warning)
    .build();

  connection.on('EntityChanged', (payload) => {
    listeners.forEach((listener) => {
      try {
        listener(payload);
      } catch (err) {
        console.error('Error in SignalR listener:', err);
      }
    });
  });

  connection
    .start()
    .catch((err) => {
      // Falha de conexão inicial com SignalR não deve travar a navegação HTTP
      console.warn('SignalR connection failed (will retry):', err.message);
    });

  return connection;
}

export function subscribeToUpdates(callback) {
  listeners.add(callback);
  return () => listeners.delete(callback);
}

export function stopSignalR() {
  if (connection) {
    connection.stop().catch(() => {});
    connection = null;
  }
}
