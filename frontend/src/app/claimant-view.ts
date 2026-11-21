import { HttpErrorResponse } from '@angular/common/http';
import { Component, effect, inject, signal } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { Subscription } from 'rxjs';
import { ApiService, ClaimResponse, DemoIdentity } from './api.service';
import { parseMyr } from './money';

function validDate(value: string): boolean {
  const m = /^(\d{4})-(\d{2})-(\d{2})$/.exec(value);
  if (!m || m[1] === '0000') return false;
  const year = Number(m[1]), month = Number(m[2]), day = Number(m[3]);
  const leap = year % 4 === 0 && (year % 100 !== 0 || year % 400 === 0);
  const days = [31, leap ? 29 : 28, 31, 30, 31, 30, 31, 31, 30, 31, 30, 31];
  return month >= 1 && month <= 12 && day >= 1 && day <= days[month - 1];
}

@Component({
  selector: 'app-claimant-view', imports: [FormsModule], templateUrl: './claimant-view.html', styleUrl: './claimant-view.css'
})
export class ClaimantView {
  readonly identity = inject(DemoIdentity);
  private readonly api = inject(ApiService);
  private requests = new Subscription();
  readonly claims = signal<ClaimResponse[]>([]);
  readonly detail = signal<ClaimResponse | null>(null);
  readonly listLoading = signal(false);
  readonly detailLoading = signal(false);
  readonly submitting = signal(false);
  readonly responding = signal(false);
  readonly error = signal('');
  readonly detailError = signal('');
  readonly formError = signal('');
  readonly responseError = signal('');
  readonly success = signal('');
  claimType = '';
  description = '';
  incidentDate = '';
  amount = '';
  response = '';
  private detailRequest?: Subscription;

  constructor() {
    effect(onCleanup => {
      const id = this.identity.selected();
      this.requests.unsubscribe();
      this.requests = new Subscription();
      this.claims.set([]); this.detail.set(null); this.error.set(''); this.detailError.set('');
      this.formError.set(''); this.responseError.set(''); this.success.set('');
      this.listLoading.set(false); this.detailLoading.set(false); this.submitting.set(false); this.responding.set(false);
      this.response = ''; this.claimType = ''; this.description = ''; this.incidentDate = ''; this.amount = '';
      if (id === 1) this.loadClaims();
      onCleanup(() => this.requests.unsubscribe());
    });
  }
  private message(err: unknown): string {
    if (!(err instanceof HttpErrorResponse)) return 'Something went wrong. Please try again.';
    switch (err.status) {
      case 400: return 'Please check the information you entered.';
      case 401: return 'Your demo identity is missing or invalid. Select the claimant identity again.';
      case 403: return 'This action is not allowed for the selected identity.';
      case 404: return 'This claim was not found or is not available to you.';
      case 409: return 'The claim has changed and this action is no longer available. Refresh and try again.';
      default: return 'Could not reach the claims service. Please try again.';
    }
  }
  loadClaims(): void {
    if (this.identity.selected() !== 1) return;
    this.listLoading.set(true); this.error.set('');
    this.requests.add(this.api.claims().subscribe({
      next: claims => { if (this.identity.selected() !== 1) return; this.claims.set(claims); this.listLoading.set(false); },
      error: err => { if (this.identity.selected() !== 1) return; this.error.set(this.message(err)); this.listLoading.set(false); }
    }));
  }
  submit(): void {
    if (this.identity.selected() !== 1 || this.submitting()) return;
    const amount = parseMyr(this.amount);
    if ((this.claimType !== 'Motor' && this.claimType !== 'Property') || !this.description.trim() || this.description.length > 2000 || !validDate(this.incidentDate) || !amount) {
      this.formError.set('Choose a claim type, enter a valid date and description (up to 2,000 characters), and a positive MYR amount with at most two decimal places within the supported range.'); return;
    }
    this.formError.set(''); this.success.set(''); this.submitting.set(true);
    this.requests.add(this.api.submitClaim({ claimType: this.claimType, incidentDescription: this.description, incidentDate: this.incidentDate, claimedAmount: amount }).subscribe({
      next: claim => { if (this.identity.selected() !== 1) return; this.submitting.set(false); this.success.set(`Claim ${claim.claimNumber} submitted.`); this.description = ''; this.incidentDate = ''; this.amount = ''; this.claimType = ''; this.loadClaims(); },
      error: err => { if (this.identity.selected() !== 1) return; this.submitting.set(false); this.formError.set(this.message(err)); }
    }));
  }
  openClaim(id: number): void {
    if (this.identity.selected() !== 1) return;
    this.detailRequest?.unsubscribe(); this.detail.set(null); this.detailError.set(''); this.responseError.set(''); this.response = ''; this.detailLoading.set(true);
    this.detailRequest = this.api.claim(id).subscribe({
      next: claim => { if (this.identity.selected() !== 1) return; this.detail.set(claim); this.detailLoading.set(false); },
      error: err => { if (this.identity.selected() !== 1) return; this.detailLoading.set(false); this.detailError.set(this.message(err)); }
    });
    this.requests.add(this.detailRequest);
  }
  respond(): void {
    const claim = this.detail();
    if (!claim || this.identity.selected() !== 1 || this.responding()) return;
    if (!this.response.trim() || this.response.length > 2000) { this.responseError.set('Enter a response of up to 2,000 characters.'); return; }
    this.responseError.set(''); this.responding.set(true);
    this.requests.add(this.api.respond(claim.id, this.response).subscribe({
      next: updated => { if (this.identity.selected() !== 1) return; this.responding.set(false); this.detail.set(updated); this.response = ''; this.success.set('Response sent.'); this.loadClaims(); },
      error: err => { if (this.identity.selected() !== 1) return; this.responding.set(false); this.responseError.set(this.message(err)); }
    }));
  }
}
