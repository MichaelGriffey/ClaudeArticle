Feature: Out-of-range sensor reading detection
  As a flight operations engineer
  I want every sensor reading classified against its calibrated limits
  So that anomalies are surfaced before they affect mission decisions

  # Scenarios tagged @in-process inject faults or inspect the store, so they run only
  # against the in-memory host. The staging run filters them out by tag.

  Background:
    Given sensor "TMP-07" has limits -40.0 to 125.0 degrees C
    And readings may be at most 5 minutes old and 2 seconds ahead of server time

  @AC-1
  Scenario Outline: Limits are inclusive
    When a reading of <value> is received
    Then the reading is classified "<classification>"

    Examples:
      | value      | classification |
      | -40.0      | Nominal        |
      | 125.0      | Nominal        |
      | -40.000001 | Low            |
      | 125.000001 | High           |

  @AC-2 @in-process
  Scenario: Non-finite reading is rejected, not classified
    When a reading of NaN is received
    Then the reading is rejected with error "NonFiniteValue"
    And no classification event is published

  @AC-3 @in-process
  Scenario: Duplicate reading is idempotent
    Given a reading for "TMP-07" was accepted
    When a reading with the same sensor and timestamp arrives again
    Then no second event is published

  @AC-4 @in-process
  Scenario: Event store is unavailable
    Given the event store is not reachable
    When a valid reading is received
    Then the API returns 503 with a Retry-After header
    And the reading is not partially persisted

  @AC-5
  Scenario Outline: Readings outside the freshness window are rejected
    When a reading observed <offset> is received
    Then the reading is rejected with error "<error>"

    Examples:
      | offset                  | error           |
      | 5 minutes 1 second ago  | StaleReading    |
      | 3 seconds in the future | FutureTimestamp |
