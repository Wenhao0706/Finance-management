import { Component, Input } from '@angular/core';
import { CommonModule } from '@angular/common';
import { CategoryAmount } from '../../models/summary.model';

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
}
