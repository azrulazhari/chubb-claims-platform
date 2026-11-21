import { Component, inject, isDevMode, signal } from '@angular/core';
import { RouterLink, RouterLinkActive, RouterOutlet } from '@angular/router';
import { ApiService, DemoIdentity } from './api.service';

@Component({
  selector: 'app-root', imports: [RouterLink, RouterLinkActive, RouterOutlet],
  styleUrl: './app.css', templateUrl: './app.html'
})
export class App {
  protected readonly identity = inject(DemoIdentity);
  protected readonly development = isDevMode();
  protected readonly health = signal<'loading' | 'success' | 'error'>('loading');
  private readonly api = inject(ApiService);

  constructor() { this.checkHealth(); }
  protected checkHealth(): void {
    this.health.set('loading');
    this.api.health().subscribe({
      next: value => this.health.set(value.status === 'ok' ? 'success' : 'error'),
      error: () => this.health.set('error')
    });
  }
  protected selectIdentity(event: Event): void { this.identity.select((event.target as HTMLSelectElement).value); }
}
