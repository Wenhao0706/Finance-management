import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CategoryAmount } from '../../models/summary.model';
import { CategoryCapUsage } from '../../models/budget.model';

@Component({
  selector: 'app-category-bars',
  standalone: true,
  imports: [CommonModule],
  templateUrl: './category-bars.component.html',
  styleUrl: './category-bars.component.scss',
})
export class CategoryBarsComponent {
  @Input({ required: true }) categories: CategoryAmount[] = [];
  @Input() title: string = 'Categories';
  @Input() caps: CategoryCapUsage[] = [];

  capInfoFor(categoryName: string): CategoryCapUsage | null {
    return this.caps.find(c => c.name === categoryName) ?? null;
  }
}
