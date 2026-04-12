import { Component, computed, signal } from '@angular/core';
import { CommonModule } from '@angular/common';
import { FormsModule } from '@angular/forms';
import { AuthService } from '../../services/auth.service';

type ViewMode = 'login' | 'register' | 'forgot-password';

@Component({
  selector: 'app-login',
  standalone: true,
  imports: [CommonModule, FormsModule],
  templateUrl: './login.component.html',
  styleUrl: './login.component.scss',
})
export class LoginComponent {
  viewMode = signal<ViewMode>('login');
  loading = signal(false);
  errorMessage = signal('');
  successMessage = signal('');

  // Password visibility toggles
  showLoginPassword = signal(false);
  showRegisterPassword = signal(false);
  showRegisterConfirm = signal(false);

  // Login fields
  loginEmail = '';
  loginPassword = '';

  // Register fields
  registerName = '';
  registerEmail = '';
  registerPassword = signal('');
  registerConfirmPassword = signal('');
  registerTerms = false;

  passwordsMismatch = computed(() => {
    const confirm = this.registerConfirmPassword();
    return confirm.length > 0 && this.registerPassword() !== confirm;
  });

  // Forgot password fields
  forgotEmail = '';

  constructor(private authService: AuthService) {}

  setView(mode: ViewMode): void {
    this.viewMode.set(mode);
    this.errorMessage.set('');
    this.successMessage.set('');
    this.showLoginPassword.set(false);
    this.showRegisterPassword.set(false);
    this.showRegisterConfirm.set(false);
  }

  togglePassword(field: 'login' | 'register' | 'confirm'): void {
    const map = {
      login: this.showLoginPassword,
      register: this.showRegisterPassword,
      confirm: this.showRegisterConfirm,
    };
    map[field].update((v) => !v);
  }

  async onLogin(): Promise<void> {
    this.errorMessage.set('');
    this.loading.set(true);
    try {
      await this.authService.login(this.loginEmail, this.loginPassword);
    } catch (error) {
      this.errorMessage.set(this.authService.getErrorMessage(error));
    } finally {
      this.loading.set(false);
    }
  }

  async onRegister(): Promise<void> {
    this.errorMessage.set('');

    if (this.passwordsMismatch()) {
      this.errorMessage.set('Passwords do not match.');
      return;
    }
    if (!this.registerTerms) {
      this.errorMessage.set('You must agree to the Terms & Conditions.');
      return;
    }

    this.loading.set(true);
    try {
      await this.authService.register(this.registerEmail, this.registerPassword(), this.registerName);
      this.setView('login');
      this.successMessage.set('Account created! Check your email to verify.');
    } catch (error) {
      this.errorMessage.set(this.authService.getErrorMessage(error));
    } finally {
      this.loading.set(false);
    }
  }

  async onForgotPassword(): Promise<void> {
    this.errorMessage.set('');
    this.loading.set(true);
    try {
      await this.authService.forgotPassword(this.forgotEmail);
      this.successMessage.set('If an account exists, we\'ve sent a reset link. Check your inbox — and spam folder if you don\'t see it within a minute.');
      setTimeout(() => this.setView('login'), 5000);
    } catch {
      // Always show generic message (anti-enumeration)
      this.successMessage.set('If an account exists, we\'ve sent a reset link. Check your inbox — and spam folder if you don\'t see it within a minute.');
      setTimeout(() => this.setView('login'), 5000);
    } finally {
      this.loading.set(false);
    }
  }

  async onGoogleSignIn(): Promise<void> {
    this.errorMessage.set('');
    this.loading.set(true);
    try {
      await this.authService.loginWithGoogle();
    } catch (error) {
      const msg = this.authService.getErrorMessage(error);
      if (msg) this.errorMessage.set(msg);
    } finally {
      this.loading.set(false);
    }
  }
}
