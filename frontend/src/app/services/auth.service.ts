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
    await signInWithEmailAndPassword(this.auth, email, password);
    this.ngZone.run(() => this.router.navigate(['/dashboard']));
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
    if (error && typeof error === 'object' && 'code' in error) {
      const code = (error as { code: string }).code;
      if (code === 'auth/popup-closed-by-user') return '';
      return this.firebaseErrors[code] || 'Something went wrong. Please try again.';
    }
    return 'Something went wrong. Please try again.';
  }
}
