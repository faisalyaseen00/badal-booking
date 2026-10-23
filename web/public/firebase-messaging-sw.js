// Service worker: shows push notifications when the app is in the background or closed,
// and opens the booking when the notification is clicked.
// The (public) Firebase config is passed in the registration URL, so this file needs no build step.
importScripts("https://www.gstatic.com/firebasejs/13.0.0/firebase-app-compat.js");
importScripts("https://www.gstatic.com/firebasejs/13.0.0/firebase-messaging-compat.js");

const params = new URL(self.location.href).searchParams;
firebase.initializeApp({
  apiKey: params.get("apiKey"),
  authDomain: params.get("authDomain"),
  projectId: params.get("projectId"),
  messagingSenderId: params.get("messagingSenderId"),
  appId: params.get("appId"),
});
firebase.messaging(); // FCM shows "notification" messages automatically while in the background

self.addEventListener("notificationclick", (event) => {
  event.notification.close();
  const data = event.notification.data?.FCM_MSG?.data ?? event.notification.data ?? {};
  const url = data.bookingId ? `/bookings/${data.bookingId}` : "/bookings";
  event.waitUntil((async () => {
    const tabs = await clients.matchAll({ type: "window", includeUncontrolled: true });
    for (const tab of tabs) {
      if (new URL(tab.url).origin === self.location.origin) { await tab.focus(); return tab.navigate(url); }
    }
    return clients.openWindow(url);
  })());
});
