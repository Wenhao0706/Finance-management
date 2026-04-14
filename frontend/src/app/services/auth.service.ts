import { Injectable, NgZone } from '@angular/core';
import { Router } from '@angular/router';
import { BehaviorSubject, Observable } from 'rxjs';
import { initializeApp, FirebaseApp } from 'firebase/app';
import { initializeAppCheck, AppCheck, ReCaptchaV3Provider, getToken } from 'firebase/app-check';
import {
  getAuth,
  Auth,
  onAuthStateChanged,
  createUserWithEmailAndPassword,
  signInWithEmailAndPassword,
  signInWithPopup,
  GoogleAuthProvider,
  sendPasswordResetEmail,
  sendEmailVerification,
  updateProfile,
  signOut,
  User,
} from 'firebase/auth';
import { environment } from '../../environments/environment';

interface LockoutDecision {
  isLocked: boolean;
  retryAfterSeconds: number;
  failedAttemptsInWindow: number;
}

class LockoutError extends Error {
  readonly code = 'app/locked';
  constructor(public readonly retryAfterSeconds: number) {
    super('Too many failed attempts.');
  }
}

@Injectable({ providedIn: 'root' })
export class AuthService {
  private app: FirebaseApp;
  private appCheck: AppCheck;
  private auth: Auth;
  // undefined = not yet initialized, null = no user, User = logged in
  private currentUserSubject = new BehaviorSubject<User | null | undefined>(undefined);

  // Suppress emissions to currentUser$ during multi-step flows like
  // register() where Firebase briefly authenticates a user that we're
  // about to sign out. Without this, the app shell flashes the logged-in
  // layout (sidebar, dashboard) for ~1s while register() is still
  // running profile-update + send-verification + signout — looks like a
  // duplicated screen to the user.
  private suppressAuthEmit = false;

  currentUser$: Observable<User | null | undefined> = this.currentUserSubject.asObservable();

  private firebaseErrors: Record<string, string> = {
    'auth/email-already-in-use': 'An account with this email already exists.',
    'auth/invalid-credential': 'Invalid email or password.',
    'auth/too-many-requests': 'Too many attempts. Please try again later.',
    'auth/weak-password': 'Password is too weak. Use at least 8 characters.',
    'auth/invalid-email': 'Please enter a valid email address.',
  };

  constructor(private router: Router, private ngZone: NgZone) {
    this.app = initializeApp(environment.firebase);

    this.appCheck = initializeAppCheck(this.app, {
      provider: new ReCaptchaV3Provider(environment.recaptchaSiteKey),
      isTokenAutoRefreshEnabled: true,
    });

    this.auth = getAuth(this.app);

    onAuthStateChanged(this.auth, (user) => {
      // Skip emitting transient auth states during register() — see
      // suppressAuthEmit declaration above.
      if (this.suppressAuthEmit) return;
      this.ngZone.run(() => {
        this.currentUserSubject.next(user);
      });
    });
  }

  async register(email: string, password: string, fullName: string): Promise<void> {
    this.suppressAuthEmit = true;
    try {
      const credential = await createUserWithEmailAndPassword(this.auth, email, password);
      await updateProfile(credential.user, { displayName: fullName });
      await sendEmailVerification(credential.user);
      await signOut(this.auth);
    } finally {
      this.suppressAuthEmit = false;
      // Manually emit the final state (signed out) since we suppressed
      // the intermediate callbacks above.
      this.ngZone.run(() => {
        this.currentUserSubject.next(this.auth.currentUser);
      });
    }
  }

  async login(email: string, password: string): Promise<void> {
    // Pre-check the backend's progressive lockout before hitting Firebase —
    // saves a Firebase round-trip when we already know the user is locked,
    // and gives us a precise countdown to show in the UI.
    const lockout = await this.checkLockout(email);
    if (lockout.isLocked) {
      throw new LockoutError(lockout.retryAfterSeconds);
    }

    try {
      await signInWithEmailAndPassword(this.auth, email, password);
      // Fire-and-forget: report success but don't block navigation on it.
      void this.reportAuthEvent(email, true);
      this.ngZone.run(() => this.router.navigate(['/dashboard']));
    } catch (error) {
      const code = (error && typeof error === 'object' && 'code' in error)
        ? (error as { code: string }).code
        : null;
      void this.reportAuthEvent(email, false, code);
      throw error;
    }
  }

  // Best-effort backend calls. If the lockout API is unreachable we fall
  // through to the Firebase attempt — the user shouldn't be locked out of
  // their app because our backend hiccupped. Firebase's own throttling
  // (auth/too-many-requests) is still the underlying brute-force defense.
  private async checkLockout(email: string): Promise<LockoutDecision> {
    try {
      const response = await fetch(
        `${environment.apiUrl}/auth-events/check?email=${encodeURIComponent(email.trim())}`,
      );
      if (!response.ok) return { isLocked: false, retryAfterSeconds: 0, failedAttemptsInWindow: 0 };
      return await response.json();
    } catch {
      return { isLocked: false, retryAfterSeconds: 0, failedAttemptsInWindow: 0 };
    }
  }

  private async reportAuthEvent(email: string, success: boolean, errorCode?: string | null): Promise<void> {
    try {
      await fetch(`${environment.apiUrl}/auth-events`, {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({
          email: email.trim(),
          success,
          errorCode: errorCode ?? null,
        }),
      });
    } catch {
      // Silently swallow — telemetry shouldn't break the login flow.
    }
  }

  async loginWithGoogle(): Promise<void> {
    const provider = new GoogleAuthProvider();
    await signInWithPopup(this.auth, provider);
    this.ngZone.run(() => this.router.navigate(['/dashboard']));
  }

  async forgotPassword(email: string): Promise<void> {
    await sendPasswordResetEmail(this.auth, email);
  }

  async logout(): Promise<void> {
    await signOut(this.auth);
    this.ngZone.run(() => this.router.navigate(['/login']));
  }

  async getIdToken(): Promise<string | null> {
    const user = this.auth.currentUser;
    if (!user) return null;
    return user.getIdToken();
  }

  // Returns the current App Check token (reCAPTCHA v3 attestation proving
  // the request came from the real app). Attached to every outbound API call
  // via the auth interceptor. Returns null if App Check can't produce a token
  // (offline, reCAPTCHA blocked by ad-blocker, etc.) — the backend rejects
  // missing tokens only in enforce mode.
  async getAppCheckToken(): Promise<string | null> {
    try {
      const tokenResult = await getToken(this.appCheck, /* forceRefresh */ false);
      return tokenResult.token;
    } catch {
      return null;
    }
  }

  getErrorMessage(error: unknown): string {
    if (error instanceof LockoutError) {
      return `Too many failed attempts. Please wait ${this.formatRetry(error.retryAfterSeconds)} before trying again.`;
    }
    if (error && typeof error === 'object' && 'code' in error) {
      const code = (error as { code: string }).code;
      if (code === 'auth/popup-closed-by-user') return '';
      return this.firebaseErrors[code] || 'Something went wrong. Please try again.';
    }
    return 'Something went wrong. Please try again.';
  }

  private formatRetry(seconds: number): string {
    if (seconds < 60) return `${seconds} second${seconds === 1 ? '' : 's'}`;
    const minutes = Math.ceil(seconds / 60);
    return `${minutes} minute${minutes === 1 ? '' : 's'}`;
  }
}
