import { BehaviorSubject, Subject } from 'rxjs';
import { ParamMap, convertToParamMap, ActivatedRoute, Router } from '@angular/router';
import { TestBed } from '@angular/core/testing';

export class MockedRouter {
  url = '';
  events: Subject<any> = new Subject();

  static injectMockRouter() {
    return (TestBed.inject(Router) as unknown) as MockedRouter;
  }

  navigate(_commands: any[]): Promise<boolean> {
    return Promise.resolve(true);
  }
}

export class MockedActivatedRoute  {

  constructor() {
    this.testParamMap = {};
  }

  private subject = new BehaviorSubject(convertToParamMap(this.testParamMap));
  paramMap = this.subject.asObservable();
  private _testParamMap!: ParamMap;

  get testParamMap() {
    return this._testParamMap;
  }

  set testParamMap(params: {}) {
    this._testParamMap = convertToParamMap(params);
    this.subject.next(this._testParamMap);
  }

  get snapshot() {
    return { queryParamMap: this.testParamMap };
  }

  static injectMockActivatedRoute() {
    return (TestBed.inject(ActivatedRoute) as unknown) as MockedActivatedRoute;
  }
}
