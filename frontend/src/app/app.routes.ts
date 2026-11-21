import { Component } from '@angular/core';
import { Routes } from '@angular/router';

@Component({ template: `<section class="view"><span class="eyebrow">CLAIMANT WORKSPACE</span><h2>Your claims, in one place.</h2><p>Submit a claim and follow its progress from here.</p><div class="placeholder"><span class="placeholder-icon" aria-hidden="true">↗</span><div><strong>Claimant tools are coming next</strong><p>Claim submission and tracking will be available in the next phase.</p></div></div></section>` })
export class ClaimantView {}

@Component({ template: `<section class="view"><span class="eyebrow">OFFICER WORKSPACE</span><h2>A clear view of your workload.</h2><p>Review claims and manage the next steps in one place.</p><div class="placeholder"><span class="placeholder-icon" aria-hidden="true">↗</span><div><strong>Officer tools are coming next</strong><p>The workload dashboard and claim actions will be available in a later phase.</p></div></div></section>` })
export class OfficerView {}

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'claimant' },
  { path: 'claimant', component: ClaimantView, title: 'Claimant | Chubb Claims' },
  { path: 'officer', component: OfficerView, title: 'Claims officer | Chubb Claims' },
  { path: '**', redirectTo: 'claimant' }
];
