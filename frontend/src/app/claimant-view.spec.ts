import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ApiService, ClaimResponse, DemoIdentity, demoIdentityInterceptor } from './api.service';
import { ClaimantView, malaysiaToday } from './claimant-view';
import { vi } from 'vitest';
import { parseMyr } from './money';

const claim: ClaimResponse = {
  id: 7, claimNumber: 'CL-123', claimantId: 1, assignedOfficerId: 2, claimType: 'Motor',
  incidentDescription: 'Vehicle damage', incidentDate: '2026-01-03', marketCode: 'MY', currencyCode: 'MYR',
  claimedAmount: 12.34, estimatedLiability: 0, status: 'InformationRequired', createdAt: '2026-01-04T00:00:00Z', updatedAt: '2026-01-04T00:00:00Z',
  history: [{ actorId: 2, action: 'InformationRequested', notes: 'Photo please', createdAt: '2026-01-04T00:00:00Z' }],
  informationRequests: [{ id: 5, requestedByOfficerId: 2, message: 'Photo please', response: null, requestedAt: '2026-01-04T00:00:00Z', respondedAt: null }]
};

describe('MYR input', () => {
  it('accepts whole sen exactly up to the backend limit', () => {
    expect(parseMyr('0.1')).toBe('0.10');
    expect(parseMyr('00012.34')).toBe('12.34');
    expect(parseMyr('00000000000000000001.00')).toBe('1.00');
    expect(parseMyr('92233720368547758.07')).toBe('92233720368547758.07');
  });
  it.each(['', '0', '0.00', '-1', '1.001', '1e2', '1,000', '.1', '1.', '92233720368547758.08'])('rejects invalid amount %s', value => {
    expect(parseMyr(value)).toBeNull();
  });
});

describe('Malaysia calendar', () => {
  it('uses Malaysia date on either side of midnight', () => {
    expect(malaysiaToday(new Date('2026-06-01T15:59:59Z'))).toBe('2026-06-01');
    expect(malaysiaToday(new Date('2026-06-01T16:00:00Z'))).toBe('2026-06-02');
  });
});

describe('claimant portal', () => {
  beforeEach(() => TestBed.configureTestingModule({
    imports: [ClaimantView], providers: [provideHttpClient(withInterceptors([demoIdentityInterceptor])), provideHttpClientTesting()]
  }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());
  function setup() {
    const identity = TestBed.inject(DemoIdentity); identity.select('1');
    const fixture = TestBed.createComponent(ClaimantView); fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    return { fixture, component: fixture.componentInstance, identity, http };
  }
  it('validates dates and amounts before submitting an exact JSON numeric token', () => {
    const { fixture, component, http } = setup(); http.expectOne('/api/claims').flush([]);
    component.claimType = 'Property'; component.description = 'Damage'; component.incidentDate = '2026-02-30'; component.amount = '1.00';
    component.submit(); expect(component.formError()).toContain('valid date');
    component.incidentDate = '2026-02-28'; component.amount = '0.001'; component.submit(); expect(component.formError()).toContain('positive MYR');
    http.expectNone(req => req.method === 'POST');
    component.amount = '92233720368547758.07'; component.submit();
    const post = http.expectOne('/api/claims'); expect(post.request.method).toBe('POST');
    expect(post.request.body).toContain('"claimedAmount":92233720368547758.07');
    expect(post.request.headers.get('X-Demo-User-Id')).toBe('1'); post.flush(claim, { status: 201, statusText: 'Created' });
    http.expectOne('/api/claims').flush([claim]); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Claim CL-123 submitted.');
  });
  it('allows yesterday and today, and rejects tomorrow using Malaysia time', () => {
    vi.useFakeTimers(); vi.setSystemTime(new Date('2026-06-01T16:00:00Z'));
    try {
      const { fixture, component, http } = setup(); http.expectOne('/api/claims').flush([]);
      fixture.detectChanges();
      expect((fixture.nativeElement.querySelector('input[name="incidentDate"]') as HTMLInputElement).max).toBe('2026-06-02');
      component.claimType = 'Motor'; component.description = 'Damage'; component.amount = '1.00';
      for (const date of ['2026-06-01', '2026-06-02']) {
        component.incidentDate = date; component.submit();
        http.expectOne('/api/claims').flush(claim, { status: 201, statusText: 'Created' });
        http.expectOne('/api/claims').flush([]);
        component.claimType = 'Motor'; component.description = 'Damage'; component.amount = '1.00';
      }
      component.incidentDate = '2026-06-03'; component.submit(); fixture.detectChanges();
      expect(fixture.nativeElement.textContent).toContain('Incident date cannot be in the future in Malaysia');
      http.expectNone(req => req.method === 'POST');
    } finally { vi.useRealTimers(); }
  });
  it('renders list, details, history and sends an information response', () => {
    const { fixture, component, http } = setup(); http.expectOne('/api/claims').flush([claim]); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('CL-123');
    expect(fixture.nativeElement.textContent).toContain('MYR 12.34');
    component.openClaim(7); http.expectOne('/api/claims/7').flush(claim); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Vehicle damage');
    expect(fixture.nativeElement.textContent).toContain('InformationRequested');
    expect(fixture.nativeElement.textContent).toContain('Photo please');
    component.response = '  '; component.respond(); expect(component.responseError()).toContain('Enter a response');
    component.response = 'Photo sent'; component.respond();
    const post = http.expectOne('/api/claims/7/respond'); expect(post.request.body).toEqual({ message: 'Photo sent' });
    post.flush({ ...claim, status: 'UnderReview', informationRequests: [{ ...claim.informationRequests[0], response: 'Photo sent', respondedAt: '2026-01-05T00:00:00Z' }] });
    http.expectOne('/api/claims').flush([claim]); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Response sent.');
    expect(fixture.nativeElement.textContent).toContain('Photo sent');
  });
  it('does not replace another selected claim when an earlier response completes', () => {
    const { fixture, component, http } = setup();
    const other = { ...claim, id: 8, claimNumber: 'CL-456', incidentDescription: 'Other claim' };
    http.expectOne('/api/claims').flush([claim, other]);
    component.openClaim(7); http.expectOne('/api/claims/7').flush(claim);
    component.response = 'Photo sent'; component.respond();
    const pending = http.expectOne('/api/claims/7/respond');
    component.openClaim(8); http.expectOne('/api/claims/8').flush(other); fixture.detectChanges();
    pending.flush({ ...claim, status: 'UnderReview' }); fixture.detectChanges();
    expect(component.detail()?.id).toBe(8);
    expect(component.responding()).toBe(false);
    expect(component.success()).toBe('');
    expect(fixture.nativeElement.textContent).toContain('Other claim');
    expect(fixture.nativeElement.textContent).not.toContain('Response sent.');
    http.expectNone('/api/claims');
  });
  it('ignores a stale response error even after returning to the original claim', () => {
    const { component, http } = setup(); http.expectOne('/api/claims').flush([claim]);
    component.openClaim(7); http.expectOne('/api/claims/7').flush(claim);
    component.response = 'Info'; component.respond(); const pending = http.expectOne('/api/claims/7/respond');
    component.openClaim(8); http.expectOne('/api/claims/8').flush({ ...claim, id: 8 });
    component.openClaim(7); http.expectOne('/api/claims/7').flush(claim);
    pending.flush({}, { status: 409, statusText: 'Conflict' });
    expect(component.detail()?.id).toBe(7); expect(component.responseError()).toBe(''); expect(component.responding()).toBe(false);
  });
  it('cancels an in-flight response when the demo identity changes', () => {
    const { fixture, component, identity, http } = setup(); http.expectOne('/api/claims').flush([claim]);
    component.openClaim(7); http.expectOne('/api/claims/7').flush(claim);
    component.response = 'Info'; component.respond(); const pending = http.expectOne('/api/claims/7/respond');
    identity.select('2'); fixture.detectChanges();
    expect(pending.cancelled).toBe(true); expect(component.detail()).toBeNull();
    expect(component.responding()).toBe(false); expect(component.success()).toBe('');
  });
  it('clears private data and cancels pending requests when identity changes', () => {
    const { fixture, component, identity, http } = setup(); http.expectOne('/api/claims').flush([claim]); fixture.detectChanges();
    component.openClaim(7); const pending = http.expectOne('/api/claims/7');
    identity.select('2'); fixture.detectChanges();
    expect(pending.cancelled).toBe(true);
    expect(component.claims()).toEqual([]); expect(component.detail()).toBeNull();
    expect(fixture.nativeElement.textContent).not.toContain('CL-123');
    identity.select('1'); fixture.detectChanges(); http.expectOne('/api/claims').flush([]); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('No claims yet');
  });
  it('shows useful errors for unavailable details and conflicting responses', () => {
    const { fixture, component, http } = setup(); http.expectOne('/api/claims').flush([claim]);
    component.openClaim(7); http.expectOne('/api/claims/7').flush({}, { status: 404, statusText: 'Not Found' }); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('not found or is not available');
    component.openClaim(7); http.expectOne('/api/claims/7').flush(claim); component.response = 'More info'; component.respond();
    http.expectOne('/api/claims/7/respond').flush({}, { status: 409, statusText: 'Conflict' }); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('claim has changed');
  });
});
