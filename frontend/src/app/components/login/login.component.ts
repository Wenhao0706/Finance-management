import { Component, signal } from '@angular/core';
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

  // Login fields
  loginEmail = '';
  loginPassword = '';

  // Register fields
  registerName = '';
  registerEmail = '';
  registerPassword = '';
  registerConfirmPassword = '';
  registerTerms = false;

  // Forgot password fields
  forgotEmail = '';

  constructor(private authService: AuthService) {}

  setView(mode: ViewMode): void {
    this.viewMode.set(mode);
    this.errorMessage.set('');
    this.successMessage.set('');
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

    if (this.registerPassword !== this.registerConfirmPassword) {
      this.errorMessage.set('Passwords do not match.');
      return;
    }
    if (!this.registerTerms) {
      this.errorMessage.set('You must agree to the Terms & Conditions.');
      return;
    }

    this.loading.set(true);
    try {
      await this.authService.register(this.registerEmail, this.registerPassword, this.registerName);
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
      this.successMessage.set('If an account exists, we\'ve sent a reset link.');
      setTimeout(() => this.setView('login'), 3000);
    } catch {
      // Always show generic message (anti-enumeration)
      this.successMessage.set('If an account exists, we\'ve sent a reset link.');
      setTimeout(() => this.setView('login'), 3000);
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
