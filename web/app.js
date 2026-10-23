// Test client for the Badal Booking API.
// Flow: Firebase sign-in (client SDK) -> ID token -> POST /auth/firebase -> API JWT -> API calls.
// The API JWT is kept in memory only (gone on reload), never in localStorage.
import { initializeApp } from "https://www.gstatic.com/firebasejs/13.0.0/firebase-app.js";
import {
  getAuth, onAuthStateChanged, signInWithEmailAndPassword, createUserWithEmailAndPassword,
  sendEmailVerification, signOut,
} from "https://www.gstatic.com/firebasejs/13.0.0/firebase-auth.js";
import { getMessaging, getToken, onMessage, isSupported } from "https://www.gstatic.com/firebasejs/13.0.0/firebase-messaging.js";

const config = self.BADAL_CONFIG;
const app = initializeApp(config.firebase);
const auth = getAuth(app);

const $ = (id) => document.getElementById(id);
const state = { apiToken: null, apiUser: null, deviceToken: null };

// ---------- helpers ----------
function log(message, kind = "") {
  const line = `${new Date().toLocaleTimeString()}  ${message}\n`;
  const span = document.createElement("span");
  span.textContent = line; // textContent only: never inject server/user text as HTML
  if (kind) span.className = kind;
  $("log").prepend(span);
}

function set(id, text, kind = "") {
  $(id).textContent = text;
  $(id).className = kind;
}

const mask = (token) => (token ? `${token.slice(0, 12)}…${token.slice(-6)} (${token.length} chars)` : "–");

async function callApi(path, options = {}) {
  const headers = { "Content-Type": "application/json", ...(options.headers || {}) };
  if (state.apiToken) headers.Authorization = `Bearer ${state.apiToken}`;
  const response = await fetch(config.apiBaseUrl + path, { ...options, headers });
  const text = await response.text();
  const body = text ? JSON.parse(text) : null;
  if (!response.ok) throw new Error(`${response.status} ${body?.title ?? response.statusText}`);
  return body;
}

async function run(label, fn) {
  try { await fn(); }
  catch (error) { log(`${label} failed: ${error.message}`, "bad"); }
}

// ---------- 1. Firebase account ----------
$("signIn").onclick = () => run("Sign in", async () => {
  await signInWithEmailAndPassword(auth, $("email").value, $("password").value);
  log("Signed in to Firebase.", "ok");
});

$("signUp").onclick = () => run("Create account", async () => {
  const { user } = await createUserWithEmailAndPassword(auth, $("email").value, $("password").value);
  await sendEmailVerification(user);
  log("Firebase account created. Verification email sent – open it, then press 'I verified – refresh'.", "ok");
});

$("sendVerification").onclick = () => run("Send verification", async () => {
  await sendEmailVerification(auth.currentUser);
  log("Verification email sent.", "ok");
});

$("refresh").onclick = () => run("Refresh", async () => {
  await auth.currentUser.reload();
  await auth.currentUser.getIdToken(true); // new ID token that includes email_verified = true
  render(auth.currentUser);
  log(`Email verified: ${auth.currentUser.emailVerified}`);
});

$("signOut").onclick = () => run("Sign out", async () => {
  await signOut(auth);
  state.apiToken = null; state.apiUser = null;
  render(null);
  log("Signed out.");
});

onAuthStateChanged(auth, (user) => render(user));

function render(user) {
  set("fbUser", user ? user.email : "signed out");
  set("fbVerified", user ? (user.emailVerified ? "yes" : "no – verify before using the API") : "–",
      user ? (user.emailVerified ? "ok" : "bad") : "");
  $("sendVerification").disabled = !user || user.emailVerified;
  $("refresh").disabled = !user;
  $("signOut").disabled = !user;
  $("exchange").disabled = !user;
  $("enablePush").disabled = !state.apiToken;
  $("registerDevice").disabled = !state.apiToken || !state.deviceToken;
  $("logout").disabled = !state.apiToken;
  set("apiUser", state.apiUser ? `${state.apiUser.email} (${state.apiUser.role})` : "–", state.apiUser ? "ok" : "");
}

// ---------- 2. Exchange the Firebase ID token for the API's JWT ----------
$("exchange").onclick = () => run("POST /auth/firebase", async () => {
  const idToken = await auth.currentUser.getIdToken();
  const result = await callApi("/auth/firebase", { method: "POST", body: JSON.stringify({ idToken }) });
  state.apiToken = result.accessToken;
  state.apiUser = result.user;
  render(auth.currentUser);
  log(`API session ready: ${result.user.email} as ${result.user.role}.`, "ok");
});

// ---------- 3. Push notifications ----------
$("enablePush").onclick = () => run("Push setup", async () => {
  if (!(await isSupported())) throw new Error("This browser doesn't support web push.");
  const permission = await Notification.requestPermission();
  set("pushPermission", permission, permission === "granted" ? "ok" : "bad");
  if (permission !== "granted") throw new Error("Notification permission was not granted.");

  const registration = await navigator.serviceWorker.register("firebase-messaging-sw.js");
  const messaging = getMessaging(app);
  state.deviceToken = await getToken(messaging, {
    serviceWorkerRegistration: registration,
    ...(config.vapidKey ? { vapidKey: config.vapidKey } : {}),
  });
  set("pushToken", mask(state.deviceToken), "ok");
  render(auth.currentUser);
  log("Got an FCM device token. Next: Register device.", "ok");

  // Messages that arrive while this tab is open and focused.
  onMessage(messaging, (payload) => showNotification(payload));
});

// Save this device's token for the signed-in user, so the API can push to it.
$("registerDevice").onclick = () => run("POST /devices", async () => {
  const device = await callApi("/devices", {
    method: "POST",
    body: JSON.stringify({ token: state.deviceToken, platform: "web" }),
  });
  state.deviceId = device.id;
  set("deviceRegistered", `yes (id ${device.id})`, "ok");
  log("Device registered with the API.", "ok");
});

// Log out: first tell the API to forget this device's token (no more pushes here),
// then drop the API JWT and sign out of Firebase.
$("logout").onclick = () => run("Log out", async () => {
  await callApi("/auth/logout", { method: "POST", body: JSON.stringify({ deviceToken: state.deviceToken }) });
  state.apiToken = null; state.apiUser = null; state.deviceId = null;
  set("deviceRegistered", "no (removed on logout)");
  await signOut(auth);
  render(null);
  log("Logged out: device token removed from the API, API token discarded, signed out of Firebase.", "ok");
});

function showNotification(payload) {
  const li = document.createElement("li");
  const title = payload.notification?.title ?? "(no title)";
  const body = payload.notification?.body ?? "";
  li.textContent = `${new Date().toLocaleTimeString()} – ${title}: ${body} ${payload.data?.bookingId ? `(booking ${payload.data.bookingId})` : ""}`;
  $("notifications").prepend(li);
  log(`Push received: ${title}`, "ok");
}

set("apiUrl", config.apiBaseUrl);
log(`Ready. Firebase project: ${config.firebase.projectId}`);
