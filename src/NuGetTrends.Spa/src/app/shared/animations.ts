import { trigger, animate, transition, style } from '@angular/animations';

export const AppAnimations = {
  slideInOutAnimation:   trigger('slideInOut', [
    transition(':enter', [
      style({opacity: 0}),
      animate(300, style({opacity: 1}))
    ]),
    transition(':leave', [
      animate(300, style({opacity: 0}))
    ])
  ])
};
