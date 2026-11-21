import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { provideRouter } from '@angular/router';
import { ApiService, DemoIdentity, demoIdentityInterceptor } from './api.service';
import { App } from './app';
import { routes } from './app.routes';

describe('API and application shell', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [App],
    providers: [provideHttpClient(withInterceptors([demoIdentityInterceptor])), provideHttpClientTesting(), provideRouter(routes)]
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());

  it('sends only a selected demo identity to API routes, not health', () => {
    const api = TestBed.inject(ApiService);
    const identity = TestBed.inject(DemoIdentity);
    const http = TestBed.inject(HttpTestingController);
    api.claims().subscribe();
    const noIdentity = http.expectOne('/api/claims');
    expect(noIdentity.request.headers.has('X-Demo-User-Id')).toBe(false);
    noIdentity.flush([]);
    identity.select('1');
    api.claims().subscribe();
    const claimant = http.expectOne('/api/claims');
    expect(claimant.request.headers.get('X-Demo-User-Id')).toBe('1');
    claimant.flush([]);
    identity.select('2');
    api.dashboard().subscribe();
    const officer = http.expectOne('/api/reports/dashboard');
    expect(officer.request.headers.get('X-Demo-User-Id')).toBe('2');
    officer.flush({});
    api.health().subscribe();
    const health = http.expectOne('/health');
    expect(health.request.headers.has('X-Demo-User-Id')).toBe(false);
    health.flush({ status: 'ok' });
  });

  it('shows loading, success, and error with retry for API health', () => {
    const fixture = TestBed.createComponent(App);
    const http = TestBed.inject(HttpTestingController);
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Checking API connection');
    http.expectOne('/health').flush({ status: 'ok' });
    fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('API connected');
    fixture.destroy();
    const failed = TestBed.createComponent(App);
    failed.detectChanges();
    http.expectOne('/health').flush('unavailable', { status: 503, statusText: 'Unavailable' });
    failed.detectChanges();
    expect(failed.nativeElement.textContent).toContain('API unavailable');
    (failed.nativeElement.querySelector('button') as HTMLButtonElement).click();
    failed.detectChanges();
    expect(failed.nativeElement.textContent).toContain('Checking API connection');
    http.expectOne('/health').flush({ status: 'ok' });
    failed.detectChanges();
    expect(failed.nativeElement.textContent).toContain('API connected');
  });
});
