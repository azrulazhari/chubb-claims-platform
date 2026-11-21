import { Component } from '@angular/core';
import { Routes } from '@angular/router';
import { ClaimantView } from './claimant-view';

@Component({ template: `<section class="view"><span class="eyebrow">OFFICER WORKSPACE</span><h2>A clear view of your workload.</h2><p>Review claims and manage the next steps in one place.</p><div class="placeholder"><span class="placeholder-icon" aria-hidden="true">↗</span><div><strong>Officer tools are coming next</strong><p>The workload dashboard and claim actions will be available in a later phase.</p></div></div></section>` })
export class OfficerView {}

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'claimant' },
  { path: 'claimant', component: ClaimantView, title: 'Claimant | Chubb Claims' },
  { path: 'officer', component: OfficerView, title: 'Claims officer | Chubb Claims' },
  { path: '**', redirectTo: 'claimant' }
];
