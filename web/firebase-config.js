// Public configuration for the browser pages. NOTHING here is secret:
// Firebase web config is designed to be public (access is controlled by Firebase Auth rules and
// authorized domains). The private Admin service-account key lives ONLY on the server (.env / Render).
//
// Loaded as a classic script by index.html AND by firebase-messaging-sw.js, so both use the same values.
self.BADAL_CONFIG = {
  firebase: {
    apiKey: "AIzaSyBfJ1lomMWQe1uAUU_TUudkrTHCIPH8_3U",
    authDomain: "badal-booking.firebaseapp.com",
    projectId: "badal-booking",
    storageBucket: "badal-booking.firebasestorage.app",
    messagingSenderId: "2059613865",
    appId: "1:2059613865:web:91cd3077273fcccf1e5bb8",
  },

  // Optional Web Push (VAPID) public key from Firebase Console -> Cloud Messaging -> Web Push certificates.
  // Empty = Firebase's default key is used, which works for this demo.
  vapidKey: "",

  // Where the API runs. Locally: the dotnet run port. On Vercel: the Render URL (filled in at step 3.3).
  apiBaseUrl: ["localhost", "127.0.0.1"].includes(self.location.hostname)
    ? "http://localhost:5080"
    : "https://badal-booking-api.onrender.com",
};
