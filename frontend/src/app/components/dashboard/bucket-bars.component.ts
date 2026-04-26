import { Component, Input } from '@angular/core';
import { CommonModule, CurrencyPipe } from '@angular/common';
import { RouterLink } from '@angular/router';
import { BudgetSnapshot, BucketUsage } from '../../models/budget.model';

@Component({
  selector: 'app-bucket-bars',
  standalone: true,
  imports: [CommonModule, RouterLink],
  templateUrl: './bucket-bars.component.html',
  styleUrl: './bucket-bars.component.scss',
})
export class BucketBarsComponent {
  @Input({ required: true }) budget!: BudgetSnapshot;

  // Display labels match the friendly explanation in the card heading.
  // Internal bucket names ("Needs"/"Wants"/"Savings") stay unchanged so
  // the data model is unaffected — only what the user reads is translated.
  private static readonly DISPLAY_NAMES: Record<string, string> = {
    Needs: 'Needs (the things I need)',
    Wants: 'Wants (the things I want)',
    Savings: 'Savings (money put away)',
  };

  bucketEntries(): { name: string; usage: BucketUsage }[] {
    return [
      { name: 'Needs',   usage: this.budget.buckets.needs },
      { name: 'Wants',   usage: this.budget.buckets.wants },
      { name: 'Savings', usage: this.budget.buckets.savings },
    ];
  }

  bucketDisplayName(name: string): string {
    return BucketBarsComponent.DISPLAY_NAMES[name] ?? name;
  }

  // Visual bar is capped at 100% — anything over that is communicated by
  // the "over budget" colour and the status message rather than letting
  // the bar physically overflow the track.
  visualPct(usage: BucketUsage): number {
    return Math.min(usage.pctUsed, 100);
  }

  statusMessage(status: 'ok' | 'warn' | 'over', remaining: number): string {
    const cp = new CurrencyPipe('en-US');
    const fmt = (n: number) => cp.transform(Math.abs(n)) ?? '$0.00';
    if (status === 'over')   return `Over by ${fmt(remaining)} — try to slow this down`;
    if (status === 'warn')   return `Only ${fmt(remaining)} left for this month — go gentle`;
    return `${fmt(remaining)} left for this month`;
  }
}
