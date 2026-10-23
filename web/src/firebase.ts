import { initializeApp } from "firebase/app";
import { getAuth } from "firebase/auth";
import { config } from "./config";

// One Firebase app for the whole frontend (public web config only).
export const firebaseApp = initializeApp(config.firebase);
export const firebaseAuth = getAuth(firebaseApp);
