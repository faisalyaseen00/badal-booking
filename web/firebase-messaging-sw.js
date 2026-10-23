// Service worker: shows push notifications when the page is in the background or closed.
// Must live at the site root so it can receive pushes for the whole page.
importScripts("https://www.gstatic.com/firebasejs/13.0.0/firebase-app-compat.js");
importScripts("https://www.gstatic.com/firebasejs/13.0.0/firebase-messaging-compat.js");
importScripts("firebase-config.js"); // same public config as the page

firebase.initializeApp(self.BADAL_CONFIG.firebase);
const messaging = firebase.messaging();

// FCM displays "notification" payloads automatically in the background.
// This handler only covers data-only messages, so they're visible too.
messaging.onBackgroundMessage((payload) => {
  if (payload.notification) return;
  self.registration.showNotification(payload.data?.title ?? "Badal Booking", {
    body: payload.data?.body ?? "",
    data: payload.data,
  });
});
