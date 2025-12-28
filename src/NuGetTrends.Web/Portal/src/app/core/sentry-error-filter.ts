import { ErrorEvent, EventHint } from '@sentry/angular';

/**
 * Filters out known noisy errors that don't affect user experience.
 * Used as the beforeSend callback for Sentry.
 *
 * @param event The Sentry error event
 * @param hint Additional information about the error
 * @returns The event to send, or null to drop it
 */
export function filterNoisyErrors(
  event: ErrorEvent,
  hint: EventHint
): ErrorEvent | null {
  const error = hint.originalException;

  // Filter out Angular animation timing errors that occur when navigating
  // away from a page before animations complete. These don't affect UX.
  // See: https://nugettrends.sentry.io/issues/SPA-ZT
  if (
    error instanceof TypeError &&
    error.message?.includes("Cannot read properties of null (reading 'addEventListener')")
  ) {
    return null;
  }

  return event;
}
