import { HttpErrorResponse } from '@angular/common/http';
import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { ApiService, ClaimResponse, DashboardResponse, DemoIdentity } from './api.service';

@Component({ selector: 'app-officer-view', imports: [FormsModule], templateUrl: './officer-view.html', styleUrl: './officer-view.css' })
export class OfficerView {
  readonly identity = inject(DemoIdentity);
  private readonly api = inject(ApiService);
  private requests = new Subscription();
  private queueRequest?: Subscription;
  private dashboardRequest?: Subscription;
  private detailRequest?: Subscription;
  private selectedId: number | null = null;
  readonly dashboard = signal<DashboardResponse | null>(null);
  readonly claims = signal<ClaimResponse[]>([]);
  readonly detail = signal<ClaimResponse | null>(null);
  readonly loadingDashboard = signal(false);
  readonly loadingQueue = signal(false);
  readonly loadingDetail = signal(false);
  readonly busy = signal(false);
  readonly dashboardError = signal('');
  readonly queueError = signal('');
  readonly detailError = signal('');
  readonly actionError = signal('');
  readonly success = signal('');
  message = '';
  reason = '';

  constructor() {
    effect(onCleanup => {
      const id = this.identity.selected();
      this.requests.unsubscribe(); this.requests = new Subscription(); this.selectedId = null;
      this.dashboard.set(null); this.claims.set([]); this.detail.set(null);
      this.loadingDashboard.set(false); this.loadingQueue.set(false); this.loadingDetail.set(false); this.busy.set(false);
      this.dashboardError.set(''); this.queueError.set(''); this.detailError.set(''); this.actionError.set(''); this.success.set('');
      this.message = ''; this.reason = '';
      if (id === 2) this.refresh();
      onCleanup(() => this.requests.unsubscribe());
    });
  }
  private errorMessage(error: unknown): string {
    if (!(error instanceof HttpErrorResponse)) return 'Something went wrong. Please try again.';
    switch (error.status) {
      case 400: return 'Please check the information you entered.';
      case 401: return 'Your demo identity is missing or invalid. Select the officer identity again.';
      case 403: return 'This action is not allowed for the selected officer.';
      case 404: return 'This claim was not found or is not available to you.';
      case 409: return 'The claim has changed, possibly because another officer acted first. Refresh and try again.';
      case 422: return 'The dashboard total exceeds the supported range.';
      default: return 'Could not reach the claims service. Please try again.';
    }
  }
  refresh(): void { this.loadDashboard(); this.loadQueue(); }
  private loadDashboard(): void {
    if (this.identity.selected() !== 2) return;
    this.dashboardRequest?.unsubscribe(); this.loadingDashboard.set(true); this.dashboardError.set('');
    this.dashboardRequest = this.api.dashboard().subscribe({
      next: value => { if (this.identity.selected() !== 2) return; this.dashboard.set(value); this.loadingDashboard.set(false); },
      error: err => { if (this.identity.selected() !== 2) return; this.dashboard.set(null); this.dashboardError.set(this.errorMessage(err)); this.loadingDashboard.set(false); }
    });
    this.requests.add(this.dashboardRequest);
  }
  private loadQueue(): void {
    if (this.identity.selected() !== 2) return;
    this.queueRequest?.unsubscribe(); this.loadingQueue.set(true); this.queueError.set('');
    this.queueRequest = this.api.claims().subscribe({
      next: claims => { if (this.identity.selected() !== 2) return; this.claims.set(claims); this.loadingQueue.set(false); },
      error: err => { if (this.identity.selected() !== 2) return; this.claims.set([]); this.queueError.set(this.errorMessage(err)); this.loadingQueue.set(false); }
    });
    this.requests.add(this.queueRequest);
  }
  openClaim(id: number): void {
    if (this.identity.selected() !== 2 || this.busy()) return;
    this.detailRequest?.unsubscribe(); this.selectedId = id; this.detail.set(null); this.detailError.set(''); this.actionError.set(''); this.message = ''; this.reason = '';
    this.loadDetail(id);
  }
  private loadDetail(id: number): void {
    this.detailRequest?.unsubscribe(); this.loadingDetail.set(true); this.detailError.set('');
    this.detailRequest = this.api.claim(id).subscribe({
      next: claim => { if (this.identity.selected() !== 2 || this.selectedId !== id) return; this.detail.set(claim); this.loadingDetail.set(false); },
      error: err => { if (this.identity.selected() !== 2 || this.selectedId !== id) return; this.detail.set(null); this.loadingDetail.set(false); this.detailError.set(this.errorMessage(err)); }
    });
    this.requests.add(this.detailRequest);
  }
  assign(): void {
    const claim = this.detail();
    if (!claim || claim.status !== 'Submitted' || claim.assignedOfficerId !== null || this.busy() || this.loadingDetail() || this.identity.selected() !== 2) return;
    this.mutate(claim.id, this.api.assign(claim.id), 'Claim assigned to you.');
  }
  requestInfo(): void {
    const claim = this.detail();
    if (!claim || !this.canReview(claim) || this.busy()) return;
    if (!this.message.trim() || this.message.length > 2000) { this.actionError.set('Enter a message of up to 2,000 characters.'); return; }
    this.mutate(claim.id, this.api.requestInformation(claim.id, this.message), 'Information requested.');
  }
  decide(decision: 'Approve' | 'Reject'): void {
    const claim = this.detail();
    if (!claim || !this.canReview(claim) || this.busy()) return;
    if (!this.reason.trim() || this.reason.length > 2000) { this.actionError.set('Enter a decision reason of up to 2,000 characters.'); return; }
    this.mutate(claim.id, this.api.decide(claim.id, decision, this.reason), `Claim ${decision === 'Approve' ? 'approved' : 'rejected'}.`);
  }
  canReview(claim: ClaimResponse): boolean { return !this.loadingDetail() && this.identity.selected() === 2 && claim.status === 'UnderReview' && claim.assignedOfficerId === this.identity.selected(); }
  private mutate(id: number, request: ReturnType<ApiService['assign']>, success: string): void {
    this.busy.set(true); this.actionError.set(''); this.success.set('');
    this.requests.add(request.subscribe({
      next: () => {
        if (this.identity.selected() !== 2 || this.selectedId !== id) return;
        this.busy.set(false); this.success.set(success); this.message = ''; this.reason = '';
        this.refresh(); this.loadDetail(id);
      },
      error: err => {
        if (this.identity.selected() !== 2 || this.selectedId !== id) return;
        this.busy.set(false); this.actionError.set(this.errorMessage(err));
        if (err instanceof HttpErrorResponse && err.status === 409) { this.refresh(); this.loadDetail(id); }
      }
    }));
  }
}
