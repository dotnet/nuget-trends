import { Component } from '@angular/core';

@Component({
  selector: 'app-navigation',
  template: `
    <nav class="navbar has-shadow">
      <div class="container">
        <div class="navbar-brand">
          <a class="navbar-item" href="../">
            <img src="../../assets/images/logo.png" alt="NuGet Trends brand logo"/>
          </a>
        </div>
        <div class="navbar-menu">
          <div class="navbar-end">
            <a class="navbar-item is-active">
              Home
            </a>
            <span class="navbar-item">
          <a class="button is-dark is-medium">
            <span class="icon">
              <i class="fab fa fa-github"></i>
            </span>
            <span>Check it out</span>
          </a>
        </span>
          </div>
        </div>
      </div>
    </nav>`
})
export class NavigationComponent {
}
