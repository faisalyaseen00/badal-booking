import { createContext, useCallback, useContext, useEffect, useRef, useState, type ReactNode } from "react";
import { getMessaging, getToken, isSupported, onMessage } from "firebase/messaging";
import { apiFetch } from "../api/client";
import { useAuth } from "../auth/AuthContext";
import { config } from "../config";
import { firebaseApp } from "../firebase";
import { useToast } from "../components/Toasts";

export type PushState = "unsupported" | "off" | "blocked" | "on" | "working";

interface PushContextValue {
  state: PushState;
  enable(): Promise<void>;
  // Pages subscribe to refresh their data when a push arrives.
  onPush(listener: (bookingId?: string) => void): () => void;
}

const PushContext = createContext<PushContextValue | null>(null);
// The FCM token of THIS browser, so logout can tell the API to remove it.
const DEVICE_KEY = "badal.deviceToken";

export function PushProvider({ children }: { children: ReactNode }) {
  const { session, setBeforeLogout } = useAuth();
  const toast = useToast();
  const [state, setState] = useState<PushState>("off");
  const listeners = useRef(new Set<(bookingId?: string) => void>());
  const token = session?.token;

  const enable = useCallback(async () => {
    if (!token) return;
    if (!(await isSupported())) { setState("unsupported"); return; }
    setState("working");
    try {
      const permission = await Notification.requestPermission();
      if (permission !== "granted") { setState(permission === "denied" ? "blocked" : "off"); return; }

      const registration = await navigator.serviceWorker.register(
        `/firebase-messaging-sw.js?${new URLSearchParams(config.firebase)}`);
      const messaging = getMessaging(firebaseApp);
      const deviceToken = await getToken(messaging, { serviceWorkerRegistration: registration, vapidKey: config.vapidKey });

      // Save it for the signed-in user. If another user used this browser before, the API moves it.
      await apiFetch("/devices", { method: "POST", token, body: { token: deviceToken, platform: "web" } });
      localStorage.setItem(DEVICE_KEY, deviceToken);
      setState("on");
    } catch {
      setState("off");
      toast("error", "Couldn't turn on notifications", "Please try again.");
    }
  }, [token, toast]);

  // After sign-in: if the user already allowed notifications earlier, re-register silently (no prompt).
  useEffect(() => {
    if (!token) { setState("off"); return; }
    isSupported().then((ok) => {
      if (!ok) setState("unsupported");
      else if (Notification.permission === "granted") void enable();
      else setState(Notification.permission === "denied" ? "blocked" : "off");
    });
  }, [token, enable]);

  // Foreground messages (tab open and focused): show a toast and let pages refresh.
  useEffect(() => {
    if (!token) return;
    let unsubscribe = () => {};
    isSupported().then((ok) => {
      if (!ok) return;
      unsubscribe = onMessage(getMessaging(firebaseApp), (payload) => {
        toast("info", payload.notification?.title ?? "Booking update", payload.notification?.body);
        listeners.current.forEach((l) => l(payload.data?.bookingId));
      });
    });
    return () => unsubscribe();
  }, [token, toast]);

  // Logout = remove this device's token from the API first, so no more pushes arrive here.
  useEffect(() => {
    if (!token) { setBeforeLogout(null); return; }
    setBeforeLogout(async () => {
      const deviceToken = localStorage.getItem(DEVICE_KEY);
      await apiFetch("/auth/logout", { method: "POST", token, body: { deviceToken } });
      localStorage.removeItem(DEVICE_KEY);
    });
  }, [token, setBeforeLogout]);

  const onPush = useCallback((listener: (bookingId?: string) => void) => {
    listeners.current.add(listener);
    return () => { listeners.current.delete(listener); };
  }, []);

  return <PushContext.Provider value={{ state, enable, onPush }}>{children}</PushContext.Provider>;
}

export function usePush(): PushContextValue {
  const ctx = useContext(PushContext);
  if (!ctx) throw new Error("usePush must be used inside <PushProvider>");
  return ctx;
}
