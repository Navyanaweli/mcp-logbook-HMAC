import { TestBed } from '@angular/core/testing';
import { HttpClientTestingModule } from '@angular/common/http/testing';

import { LogbookService } from './logbook.service';

describe('LogbookService', () => {
  let service: LogbookService;

  beforeEach(() => {
    TestBed.configureTestingModule({
      imports: [HttpClientTestingModule]
    });
    service = TestBed.inject(LogbookService);
  });

  it('should be created', () => {
    expect(service).toBeTruthy();
  });
});
