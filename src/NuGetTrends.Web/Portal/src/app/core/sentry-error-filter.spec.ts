import { ErrorEvent, EventHint } from '@sentry/angular';
import { filterNoisyErrors } from './sentry-error-filter';

describe('Sentry Error Filter', () => {

  describe('filterNoisyErrors', () => {

    it('should filter out Angular animation addEventListener errors', () => {
      const mockEvent = { event_id: 'test-123' } as ErrorEvent;
      const mockHint = {
        originalException: new TypeError("Cannot read properties of null (reading 'addEventListener')")
      } as EventHint;

      const result = filterNoisyErrors(mockEvent, mockHint);

      expect(result).toBeNull();
    });

    it('should not filter out other TypeError errors', () => {
      const mockEvent = { event_id: 'test-456' } as ErrorEvent;
      const mockHint = {
        originalException: new TypeError('Some other error')
      } as EventHint;

      const result = filterNoisyErrors(mockEvent, mockHint);

      expect(result).toEqual(mockEvent);
    });

    it('should not filter out non-TypeError errors', () => {
      const mockEvent = { event_id: 'test-789' } as ErrorEvent;
      const mockHint = {
        originalException: new Error('Some error')
      } as EventHint;

      const result = filterNoisyErrors(mockEvent, mockHint);

      expect(result).toEqual(mockEvent);
    });

    it('should not filter out errors without originalException', () => {
      const mockEvent = { event_id: 'test-abc' } as ErrorEvent;
      const mockHint = {} as EventHint;

      const result = filterNoisyErrors(mockEvent, mockHint);

      expect(result).toEqual(mockEvent);
    });

    it('should handle null/undefined originalException gracefully', () => {
      const mockEvent = { event_id: 'test-def' } as ErrorEvent;
      const mockHint = { originalException: null } as unknown as EventHint;

      const result = filterNoisyErrors(mockEvent, mockHint);

      expect(result).toEqual(mockEvent);
    });

  });

});
