import { Routes } from '@angular/router';
import { ClaimantView } from './claimant-view';

import { OfficerView } from './officer-view';

export const routes: Routes = [
  { path: '', pathMatch: 'full', redirectTo: 'claimant' },
  { path: 'claimant', component: ClaimantView, title: 'Claimant | Chubb Claims' },
  { path: 'officer', component: OfficerView, title: 'Claims officer | Chubb Claims' },
  { path: '**', redirectTo: 'claimant' }
];
