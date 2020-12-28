import { BehaviorSubject, Subject } from 'rxjs';
import { ParamMap, convertToParamMap } from '@angular/router';

export class MockedRouter {
  url: string;
  events: Subject<any> = new Subject();
  navigate(_commands: any[]): Promise<boolean> {
    return Promise.resolve(true);
  }
}

export class MockedActivatedRoute {

  constructor() {
    this.testParamMap = {};
  }

  private subject = new BehaviorSubject(convertToParamMap(this.testParamMap));
  paramMap = this.subject.asObservable();
  private _testParamMap: ParamMap;

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
}
