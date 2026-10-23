// All frontend settings come from VITE_* variables (.env.local locally, Vercel env vars in production).
// They are compiled into the JavaScript bundle, so they are PUBLIC by definition. No secrets here.
function required(name: string, value: string | undefined): string {
  if (!value) throw new Error(`Missing ${name}. Copy web/.env.example to web/.env.local (or set it on Vercel).`);
  return value;
}

const env = import.meta.env;

export const config = {
  apiBaseUrl: required("VITE_API_BASE_URL", env.VITE_API_BASE_URL).replace(/\/$/, ""),
  firebase: {
    apiKey: required("VITE_FIREBASE_API_KEY", env.VITE_FIREBASE_API_KEY),
    authDomain: required("VITE_FIREBASE_AUTH_DOMAIN", env.VITE_FIREBASE_AUTH_DOMAIN),
    projectId: required("VITE_FIREBASE_PROJECT_ID", env.VITE_FIREBASE_PROJECT_ID),
    messagingSenderId: required("VITE_FIREBASE_MESSAGING_SENDER_ID", env.VITE_FIREBASE_MESSAGING_SENDER_ID),
    appId: required("VITE_FIREBASE_APP_ID", env.VITE_FIREBASE_APP_ID),
  },
  vapidKey: env.VITE_FIREBASE_VAPID_KEY || undefined,
};
