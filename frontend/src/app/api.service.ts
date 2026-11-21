import { HttpClient, HttpInterceptorFn } from '@angular/common/http';
import { Injectable, inject, isDevMode, signal } from '@angular/core';

export type DemoUserId = 1 | 2;
export interface HealthResponse { status: string; }
export interface ClaimResponse {
  id: number; claimNumber: string; claimantId: number; assignedOfficerId: number | null;
  claimType: 'Motor' | 'Property'; incidentDescription: string; incidentDate: string;
  marketCode: string; currencyCode: string; claimedAmount: number; estimatedLiability: number;
  status: string; createdAt: string; updatedAt: string;
  history: { actorId: number; action: string; notes: string | null; createdAt: string }[];
  informationRequests: { id: number; requestedByOfficerId: number; message: string; response: string | null; requestedAt: string; respondedAt: string | null }[];
}
export interface SubmitClaimRequest { claimType: 'Motor' | 'Property'; incidentDescription: string; incidentDate: string; claimedAmount: string; }
export interface DashboardResponse {
  totalClaimCount: number; outstandingCount: number; approvedCount: number; rejectedCount: number;
  unassignedOutstandingCount: number; outstandingEstimatedLiability: number; currencyCode: string;
  officerWorkload: { officerId: number; displayName: string; outstandingCount: number }[];
}

@Injectable({ providedIn: 'root' })
export class DemoIdentity {
  readonly selected = signal<DemoUserId | null>(null);
  select(value: string): void { this.selected.set(value === '1' ? 1 : value === '2' ? 2 : null); }
}

// Same-origin API routes only; never attach a demo identity in a production build.
export const demoIdentityInterceptor: HttpInterceptorFn = (request, next) => {
  const id = inject(DemoIdentity).selected();
  if (isDevMode() && id !== null && (request.url === '/api' || request.url.startsWith('/api/'))) {
    return next(request.clone({ setHeaders: { 'X-Demo-User-Id': String(id) } }));
  }
  return next(request);
};

@Injectable({ providedIn: 'root' })
export class ApiService {
  private readonly http = inject(HttpClient);
  health() { return this.http.get<HealthResponse>('/health'); }
  claims() { return this.http.get<ClaimResponse[]>('/api/claims'); }
  claim(id: number) { return this.http.get<ClaimResponse>(`/api/claims/${id}`); }
  submitClaim(request: SubmitClaimRequest) {
    // Angular serializes a string body as-is: inject only a separately validated numeric token.
    const { claimedAmount, ...other } = request;
    if (!/^(?:0|[1-9]\d*)\.\d{2}$/.test(claimedAmount)) throw new Error('Invalid amount token');
    const body = `${JSON.stringify(other).slice(0, -1)},"claimedAmount":${claimedAmount}}`;
    return this.http.post<ClaimResponse>('/api/claims', body, { headers: { 'Content-Type': 'application/json' } });
  }
  respond(id: number, message: string) { return this.http.post<ClaimResponse>(`/api/claims/${id}/respond`, { message }); }
  assign(id: number) { return this.http.post<ClaimResponse>(`/api/claims/${id}/assign`, null); }
  requestInformation(id: number, message: string) { return this.http.post<ClaimResponse>(`/api/claims/${id}/information-requests`, { message }); }
  decide(id: number, decision: 'Approve' | 'Reject', reason: string) { return this.http.post<ClaimResponse>(`/api/claims/${id}/decision`, { decision, reason }); }
  dashboard() { return this.http.get<DashboardResponse>('/api/reports/dashboard'); }
}
