import { provideHttpClient, withInterceptors } from '@angular/common/http';
import { HttpTestingController, provideHttpClientTesting } from '@angular/common/http/testing';
import { TestBed } from '@angular/core/testing';
import { ClaimResponse, DashboardResponse, DemoIdentity, demoIdentityInterceptor } from './api.service';
import { OfficerView } from './officer-view';

const claim: ClaimResponse = {
  id: 7, claimNumber: 'CL-123', claimantId: 1, assignedOfficerId: null, claimType: 'Motor',
  incidentDescription: 'Vehicle damage', incidentDate: '2026-01-03', marketCode: 'MY', currencyCode: 'MYR',
  claimedAmount: 12.34, estimatedLiability: 1.25, status: 'Submitted', createdAt: '2026-01-04T00:00:00Z', updatedAt: '2026-01-04T00:00:00Z',
  history: [{ actorId: 1, action: 'Submitted', notes: null, createdAt: '2026-01-04T00:00:00Z' }], informationRequests: []
};
const report: DashboardResponse = { totalClaimCount: 1, outstandingCount: 1, approvedCount: 0, rejectedCount: 0,
  unassignedOutstandingCount: 1, outstandingEstimatedLiability: 1.25, currencyCode: 'MYR',
  officerWorkload: [{ officerId: 2, displayName: 'Demo Claims Officer', outstandingCount: 0 }] };

describe('officer workspace', () => {
  beforeEach(() => TestBed.configureTestingModule({ imports: [OfficerView],
    providers: [provideHttpClient(withInterceptors([demoIdentityInterceptor])), provideHttpClientTesting()] }));
  afterEach(() => TestBed.inject(HttpTestingController).verify());
  function setup() {
    const identity = TestBed.inject(DemoIdentity); identity.select('2');
    const fixture = TestBed.createComponent(OfficerView); fixture.detectChanges();
    const http = TestBed.inject(HttpTestingController);
    const dashboard = http.expectOne('/api/reports/dashboard'); expect(dashboard.request.headers.get('X-Demo-User-Id')).toBe('2'); dashboard.flush(report);
    http.expectOne('/api/claims').flush([claim]); fixture.detectChanges();
    return { fixture, component: fixture.componentInstance, identity, http };
  }
  function refresh(http: HttpTestingController, updated: ClaimResponse) {
    http.expectOne('/api/reports/dashboard').flush(report);
    http.expectOne('/api/claims').flush([updated]);
    http.expectOne(`/api/claims/${updated.id}`).flush(updated);
  }
  it('renders dashboard, queue and details and assigns an eligible claim', () => {
    const { fixture, component, http } = setup();
    expect(fixture.nativeElement.textContent).toContain('Outstanding estimated liability');
    expect(fixture.nativeElement.textContent).toContain('MYR 1.25');
    expect(fixture.nativeElement.textContent).toContain('Demo Claims Officer');
    expect(fixture.nativeElement.textContent).toContain('CL-123');
    component.openClaim(7); http.expectOne('/api/claims/7').flush(claim); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Vehicle damage');
    expect(fixture.nativeElement.textContent).toContain('Assign to me');
    component.assign(); const request = http.expectOne('/api/claims/7/assign');
    expect(request.request.method).toBe('POST'); expect(request.request.body).toBeNull();
    const assigned = { ...claim, status: 'UnderReview', assignedOfficerId: 2 };
    request.flush(assigned); refresh(http, assigned); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Claim assigned to you.');
    expect(fixture.nativeElement.textContent).toContain('Make a decision');
  });
  it('requests information and renders the updated request and history', () => {
    const { fixture, component, http } = setup(); const assigned = { ...claim, status: 'UnderReview', assignedOfficerId: 2 };
    component.openClaim(7); http.expectOne('/api/claims/7').flush(assigned);
    component.message = ' '; component.requestInfo(); expect(component.actionError()).toContain('Enter a message');
    component.message = 'Photo please'; component.requestInfo(); const request = http.expectOne('/api/claims/7/information-requests');
    expect(request.request.body).toEqual({ message: 'Photo please' });
    const updated = { ...assigned, status: 'InformationRequired', informationRequests: [{ id: 1, requestedByOfficerId: 2, message: 'Photo please', response: null, requestedAt: 'now', respondedAt: null }] };
    request.flush(updated); refresh(http, updated); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('Photo please');
    expect(fixture.nativeElement.textContent).toContain('Awaiting claimant response');
    expect(fixture.nativeElement.textContent).not.toContain('Make a decision');
  });
  it.each([['Approve', 'Approved'], ['Reject', 'Rejected']] as const)('sends %s with a required reason and refreshes', (decision, status) => {
    const { fixture, component, http } = setup(); const assigned = { ...claim, status: 'UnderReview', assignedOfficerId: 2 };
    component.openClaim(7); http.expectOne('/api/claims/7').flush(assigned);
    component.reason = '  '; component.decide(decision); expect(component.actionError()).toContain('decision reason');
    component.reason = 'Reviewed'; component.decide(decision); const request = http.expectOne('/api/claims/7/decision');
    expect(request.request.body).toEqual({ decision, reason: 'Reviewed' }); const updated = { ...assigned, status };
    request.flush(updated); refresh(http, updated); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain(status); expect(fixture.nativeElement.textContent).not.toContain('Make a decision');
  });
  it('handles an assignment conflict and refreshes stale data', () => {
    const { fixture, component, http } = setup(); component.openClaim(7); http.expectOne('/api/claims/7').flush(claim);
    component.assign(); http.expectOne('/api/claims/7/assign').flush({}, { status: 409, statusText: 'Conflict' });
    const taken = { ...claim, status: 'UnderReview', assignedOfficerId: 3 }; refresh(http, taken); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain('another officer acted first');
    expect(fixture.nativeElement.textContent).not.toContain('Make a decision');
  });
  it('clears data and cancels pending requests when identity changes', () => {
    const { fixture, component, http, identity } = setup(); component.openClaim(7); const pending = http.expectOne('/api/claims/7');
    identity.select('1'); fixture.detectChanges(); expect(pending.cancelled).toBe(true); expect(component.claims()).toEqual([]);
    expect(component.dashboard()).toBeNull(); expect(fixture.nativeElement.textContent).not.toContain('CL-123');
  });
  it.each([[401, 'identity'], [403, 'not allowed'], [404, 'not found'], [0, 'Could not reach']] as const)('shows a useful detail error for %s', (status, text) => {
    const { fixture, component, http } = setup(); component.openClaim(7);
    http.expectOne('/api/claims/7').flush({}, { status, statusText: 'Error' }); fixture.detectChanges();
    expect(fixture.nativeElement.textContent).toContain(text);
  });
});
