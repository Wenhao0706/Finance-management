export const environment = {
  production: true,
  // Same-origin: nginx in the frontend container reverse-proxies /api → backend.
  // (Old value 'https://api.finance.manhou.de/api' pointed to deleted Fly app.)
  apiUrl: '/api',
  recaptchaSiteKey: '6LfPl7UsAAAAAAPOXSR-ZGNLeC1CKXwtXa9IKCxH',
  firebase: {
    apiKey: 'AIzaSyDekCoHUXjrMsBzh10bXWqD8zANV46D_Lc',
    authDomain: 'finance-management-10a57.firebaseapp.com',
    projectId: 'finance-management-10a57',
    storageBucket: 'finance-management-10a57.firebasestorage.app',
    messagingSenderId: '339646425354',
    appId: '1:339646425354:web:511fb558f500c9002032f3',
  },
};
